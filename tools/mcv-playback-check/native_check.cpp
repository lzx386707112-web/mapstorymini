#include "McvFormat.h"
#include <cstdint>
#include <fstream>
#include <iostream>
#include <iterator>
#include <vector>
extern "C" void* mcv_create(uint32_t,uint32_t,uint32_t,int);
extern "C" void mcv_destroy(void*);
extern "C" int mcv_decode(void*,const uint8_t*,uint32_t,uint32_t,uint32_t,uint32_t,uint32_t,uint8_t*,uint32_t);
int main(int argc,char** argv) {
    if(argc!=2) return 2;
    std::ifstream stream(argv[1],std::ios::binary);
    std::vector<uint8_t> data{std::istreambuf_iterator<char>(stream),std::istreambuf_iterator<char>()};
    bdv::McvVideo video; char error[256]={};
    if(!bdv::ParseMcv(data.data(),data.size(),&video,error,sizeof(error))) { std::cerr<<error; return 3; }
    void* decoder=mcv_create(video.fourCc,video.width,video.height,video.flags&1);
    if(!decoder) return 4;
    std::vector<uint8_t> rgba(video.width*video.height*4);
    uint64_t hash=14695981039346656037ull,transparent=0;
    for(const auto& f:video.frames) {
        if(!mcv_decode(decoder,data.data(),data.size(),f.colorOffset,f.colorSize,f.alphaOffset,f.alphaSize,rgba.data(),rgba.size())) return 5;
        for(size_t i=0;i<rgba.size();i++) { hash^=rgba[i]; hash*=1099511628211ull; }
        for(size_t i=3;i<rgba.size();i+=4) transparent+=rgba[i]<255;
    }
    mcv_destroy(decoder);
    std::cout<<"MCV_NATIVE_PASS frames="<<video.frames.size()<<" rgba_fnv64="<<std::hex<<hash<<std::dec<<" transparent="<<transparent<<"\n";
}
