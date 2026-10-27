// VPX and pixel conversion port of BeiDouVideo.cpp. Output is top-down RGBA.
#include <vpx/vp8dx.h>
#include <vpx/vpx_decoder.h>
#include <vpx/vpx_image.h>
#include <algorithm>
#include <cstdint>
#include <cstring>
#include <new>
#include <vector>
namespace {
struct Decoder {
    vpx_codec_ctx_t color{}, alpha{};
    bool colorReady=false, alphaReady=false;
    uint32_t width, height;
    ~Decoder() { if(alphaReady) vpx_codec_destroy(&alpha); if(colorReady) vpx_codec_destroy(&color); }
};
    static bool IsPlanar420(vpx_img_fmt_t format) {
        const vpx_img_fmt_t base = static_cast<vpx_img_fmt_t>(format & ~VPX_IMG_FMT_HIGHBITDEPTH);
        return base == VPX_IMG_FMT_I420 || base == VPX_IMG_FMT_YV12;
    }

    static uint8_t Clamp(int value) {
        if (value < 0) {
            return 0;
        }
        if (value > 255) {
            return 255;
        }
        return static_cast<uint8_t>(value);
    }

    static void YuvToRgb(int y, int u, int v, uint8_t* red, uint8_t* green, uint8_t* blue) {
        const int c = std::max(0, y - 16);
        const int d = u - 128;
        const int e = v - 128;
        *red = Clamp((298 * c + 409 * e + 128) >> 8);
        *green = Clamp((298 * c - 100 * d - 208 * e + 128) >> 8);
        *blue = Clamp((298 * c + 516 * d + 128) >> 8);
    }

    static bool ConvertFrame(
        const vpx_image_t* color,
        const vpx_image_t* alpha,
        uint32_t width,
        uint32_t height,
        std::vector<uint8_t>* output) {
        if (color == nullptr || color->d_w != width || color->d_h != height || !IsPlanar420(color->fmt)) {
            return false;
        }
        if (alpha != nullptr && (alpha->d_w != width || alpha->d_h != height || !IsPlanar420(alpha->fmt))) {
            return false;
        }
        output->resize(static_cast<size_t>(width) * height * 4);
        const bool colorYv12 = (color->fmt & ~VPX_IMG_FMT_HIGHBITDEPTH) == VPX_IMG_FMT_YV12;
        const int colorUPlane = colorYv12 ? VPX_PLANE_V : VPX_PLANE_U;
        const int colorVPlane = colorYv12 ? VPX_PLANE_U : VPX_PLANE_V;
        const bool alphaYv12 = alpha != nullptr &&
            (alpha->fmt & ~VPX_IMG_FMT_HIGHBITDEPTH) == VPX_IMG_FMT_YV12;
        const int alphaUPlane = alphaYv12 ? VPX_PLANE_V : VPX_PLANE_U;
        const int alphaVPlane = alphaYv12 ? VPX_PLANE_U : VPX_PLANE_V;

        for (uint32_t y = 0; y < height; ++y) {
            const uint8_t* colorY = color->planes[VPX_PLANE_Y] + y * color->stride[VPX_PLANE_Y];
            const uint8_t* colorU = color->planes[colorUPlane] + (y / 2) * color->stride[colorUPlane];
            const uint8_t* colorV = color->planes[colorVPlane] + (y / 2) * color->stride[colorVPlane];
            const uint8_t* alphaY = alpha == nullptr ? nullptr : alpha->planes[VPX_PLANE_Y] + y * alpha->stride[VPX_PLANE_Y];
            const uint8_t* alphaU = alpha == nullptr ? nullptr : alpha->planes[alphaUPlane] + (y / 2) * alpha->stride[alphaUPlane];
            const uint8_t* alphaV = alpha == nullptr ? nullptr : alpha->planes[alphaVPlane] + (y / 2) * alpha->stride[alphaVPlane];
            uint8_t* destination = output->data() + static_cast<size_t>(y) * width * 4;
            for (uint32_t x = 0; x < width; ++x) {
                uint8_t red = 0;
                uint8_t green = 0;
                uint8_t blue = 0;
                YuvToRgb(colorY[x], colorU[x / 2], colorV[x / 2], &red, &green, &blue);
                uint8_t opacity = 255;
                if (alpha != nullptr) {
                    uint8_t ignoredGreen = 0;
                    uint8_t ignoredBlue = 0;
                    YuvToRgb(alphaY[x], alphaU[x / 2], alphaV[x / 2], &opacity, &ignoredGreen, &ignoredBlue);
                }
                destination[x * 4] = red;
                destination[x * 4 + 1] = green;
                destination[x * 4 + 2] = blue;
                destination[x * 4 + 3] = opacity;
            }
        }
        return true;
    }

}
#define API extern "C" __attribute__((visibility("default")))
API void* mcv_create(uint32_t fourcc, uint32_t width, uint32_t height, int alpha) {
    if ((fourcc != 0x30385056 && fourcc != 0x30395056) || !width || !height || width>8192 || height>8192) return nullptr;
    auto* d = new(std::nothrow) Decoder;
    if (!d) return nullptr;
    d->width=width; d->height=height;
    auto* codec=fourcc==0x30385056 ? vpx_codec_vp8_dx() : vpx_codec_vp9_dx();
    vpx_codec_dec_cfg_t cfg{}; cfg.threads=2; cfg.w=width; cfg.h=height;
    d->colorReady=vpx_codec_dec_init(&d->color,codec,&cfg,0)==VPX_CODEC_OK;
    if (!d->colorReady) { delete d; return nullptr; }
    if (alpha) {
        d->alphaReady=vpx_codec_dec_init(&d->alpha,codec,&cfg,0)==VPX_CODEC_OK;
        if(!d->alphaReady) { delete d; return nullptr; }
    }
    return d;
}
API void mcv_destroy(void* handle) { delete static_cast<Decoder*>(handle); }
API int mcv_decode(void* handle, const uint8_t* bytes, uint32_t length,
    uint32_t colorOffset, uint32_t colorSize, uint32_t alphaOffset, uint32_t alphaSize,
    uint8_t* output, uint32_t capacity) {
    auto* d=static_cast<Decoder*>(handle);
    if(!d || !bytes || !output || capacity < uint64_t(d->width)*d->height*4 ||
       colorOffset>length || colorSize>length-colorOffset || !colorSize ||
       alphaOffset>length || alphaSize>length-alphaOffset || (alphaSize && !d->alphaReady)) return 0;
    if(vpx_codec_decode(&d->color,bytes+colorOffset,colorSize,nullptr,0)!=VPX_CODEC_OK) return 0;
    vpx_codec_iter_t it=nullptr;
    auto* color=vpx_codec_get_frame(&d->color,&it);
    vpx_image_t* alpha=nullptr;
    if(alphaSize) {
        if(vpx_codec_decode(&d->alpha,bytes+alphaOffset,alphaSize,nullptr,0)!=VPX_CODEC_OK) return 0;
        it=nullptr; alpha=vpx_codec_get_frame(&d->alpha,&it);
        if(!alpha) return 0;
    }
    std::vector<uint8_t> rgba;
    if(!ConvertFrame(color,alpha,d->width,d->height,&rgba)) return 0;
    memcpy(output,rgba.data(),rgba.size()); return 1;
}
