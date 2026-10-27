# MCV native decoder

`mcv_decoder.cpp` ports the decoder and YUV/alpha conversion in source `tool/client-video/BeiDouVideo.cpp`. It outputs top-down straight RGBA rather than D3D BGRA. The alpha stream uses converted red, exactly as the source does, rather than copying raw Y. libvpx is statically linked; Android needs only system libc/libm/libdl. `LICENSE.libvpx` contains the upstream BSD license.

From MapleDash (prefix local commands with `rtk proxy`):

```sh
python3 Native/Mcv/build.py --platform macos
python3 Native/Mcv/build.py --ndk "$ANDROID_NDK_ROOT"
python3 tools/mcv-playback-check/verify_decode.py
dotnet run --project tools/mcv-playback-check/PlaybackCheck.csproj -- ../../Beidou-Server/clien/Data/Video/galaxy-star-burst.mcv
python3 tools/mcv-playback-check/verify_android.py --ndk "$ANDROID_NDK_ROOT" --adb "$ANDROID_SDK_ROOT/platform-tools/adb"
```

The build pins and verifies libvpx 1.15.2; cache defaults to `~/.cache/mapledash-mcv`. macOS builds a universal arm64/x86_64 editor library; Android builds ARM64/API23 with 16KB ELF LOAD alignment and a stable SONAME. The project currently targets ARM64/minSDK26. Android native verification creates and removes its own temporary directory, without installing an APK or accessing player profiles.

Runtime: `McvPlayback` owns a decoder worker, a three-frame bounded queue and real-time clock. `NativeSkillVideo` owns texture/sprite lifecycle, separate skill and Boss channels, Boss-before-skill ordering, same-channel skill replacement and map/caster cleanup. `NativeBossVideoEffect` resolves original `Map/Effect.img` Canvas markers and the actual DLL mapping. Boss scenes keep one pending scene (first wins); marker33 stops the Boss channel and clears pending playback. Skills trigger only after successful cast validation/MP consumption. This adds video presentation; it does not replace or complete remaining V/VI damage replay logic.

MCVs are loaded unchanged from `WzAssets.DataRoot/Video`, already covered by the native resource packager's file inventory. No transcoding or substitute artwork is used. The original DLL's Root Abyss mappings34–37 refer to four MCVs absent from the current source Data/Video directory; those assets have not been fabricated. Karing mappings are available to the generic FIELD_EFFECT handler; a full Karing encounter is not implemented here.

Unity checker: `-executeMethod McvPlaybackChecks.Run` verifies native loading, channel coexistence/replacement/cleanup, all19 real Lucid IMG markers and the Boss-only stop signal. Pixel decode verification covers first8 frames of all74 current MCVs against the original conversion function. All74 supplied files use VP9; VP8 remains supported by libvpx but has no original fixture in this data set. Android/host comparison covers every frame of two original videos; it does not certify APK rendering, frame rate or the final field-layer composition on a phone.
