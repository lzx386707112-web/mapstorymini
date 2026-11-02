0095b9a4 mov      eax, 0xaded4c
0095b9a9 call     0xa60b98
0095b9ae sub      esp, 0x10
0095b9b1 push     ebx
0095b9b2 push     esi
0095b9b3 mov      esi, dword ptr [0xbe7918]
0095b9b9 push     edi
0095b9ba lea      eax, [ebp - 0x1c]
0095b9bd mov      edi, ecx
0095b9bf push     eax
0095b9c0 mov      ecx, esi
0095b9c2 call     0x425d0b
0095b9c7 mov      ebx, dword ptr [eax + 4]
0095b9ca lea      ecx, [ebp - 0x1c]
0095b9cd call     0x4289b7
0095b9d2 mov      eax, dword ptr [edi + 0x1edc]
0095b9d8 mov      eax, dword ptr [eax + 4]
0095b9db test     eax, eax
0095b9dd je       0x95bb9a
0095b9e3 push     dword ptr [eax + 0x16c]
0095b9e9 add      eax, 0x164
0095b9ee push     eax
0095b9ef call     0x508a95
0095b9f4 test     eax, eax
0095b9f6 pop      ecx
0095b9f7 pop      ecx
0095b9f8 je       0x95bb9a
0095b9fe mov      ecx, edi
0095ba00 call     0x94be9c
0095ba05 test     eax, eax
0095ba07 jne      0x95bb9a
0095ba0d push     dword ptr [ebx + 0x65]
0095ba10 lea      eax, [ebx + 0x61]
0095ba13 push     eax
0095ba14 call     0x4746dd
0095ba19 push     dword ptr [esi + 0x2124]
0095ba1f add      esi, 0x211c
0095ba25 push     esi
0095ba26 mov      dword ptr [ebp - 0x10], eax
0095ba29 call     0x416563
0095ba2e mov      ecx, eax
0095ba30 movsx    eax, word ptr [ebp - 0x10]
0095ba34 sub      eax, dword ptr [ebp + 8]
0095ba37 xor      esi, esi
0095ba39 imul     eax, eax, 0x64
0095ba3c cdq      
0095ba3d idiv     ecx
0095ba3f mov      ecx, dword ptr [0xbebf9c]
0095ba45 mov      ecx, dword ptr [ecx + 0x80]
0095ba4b lea      ecx, [ecx + ecx*4]
0095ba4e add      esp, 0x10
0095ba51 cmp      ecx, esi
0095ba53 je       0x95ba59
0095ba55 cmp      eax, ecx
0095ba57 jl       0x95ba62
0095ba59 cmp      dword ptr [ebp + 0xc], esi
0095ba5c je       0x95bb9a
0095ba62 mov      eax, dword ptr [0xbed5a0]
0095ba67 mov      eax, dword ptr [eax + 0x3c0]
0095ba6d cmp      eax, esi
0095ba6f mov      dword ptr [ebp + 0xc], eax
0095ba72 je       0x95bb9a
0095ba78 mov      eax, dword ptr [edi + 0x1edc]
0095ba7e mov      eax, dword ptr [eax + esi*8 + 4]
0095ba82 test     eax, eax
0095ba84 je       0x95bab6
0095ba86 push     dword ptr [eax + 0x16c]
0095ba8c add      eax, 0x164
0095ba91 push     eax
0095ba92 call     0x508a95
0095ba97 test     eax, eax
0095ba99 pop      ecx
0095ba9a pop      ecx
0095ba9b jne      0x95baa5
0095ba9d inc      esi
0095ba9e cmp      esi, 3
0095baa1 jl       0x95ba78
0095baa3 jmp      0x95bab6
0095baa5 mov      eax, dword ptr [edi + 0x1edc]
0095baab mov      esi, dword ptr [eax + esi*8 + 4]
0095baaf test     esi, esi
0095bab1 mov      dword ptr [ebp - 0x14], esi
0095bab4 jne      0x95badd
0095bab6 inc      dword ptr [edi + 0x3260]
0095babc cmp      dword ptr [edi + 0x3260], 3
0095bac3 lea      eax, [edi + 0x3260]
0095bac9 jg       0x95bb9a
0095bacf push     3
0095bad1 mov      ecx, edi
0095bad3 call     0x9436d2
0095bad8 jmp      0x95bb9a
0095badd and      dword ptr [edi + 0x3260], 0
0095bae4 mov      eax, dword ptr [ebx + 0x44f]
0095baea test     eax, eax
0095baec je       0x95baf1
0095baee mov      eax, dword ptr [eax - 4]
0095baf1 push     1
0095baf3 dec      eax
0095baf4 pop      ecx
0095baf5 cmp      eax, ecx
0095baf7 mov      dword ptr [ebp - 0x10], eax
0095bafa mov      dword ptr [ebp + 8], ecx
0095bafd jl       0x95bb4d
0095baff push     dword ptr [ebp + 8]
0095bb02 lea      eax, [ebp - 0x1c]
0095bb05 push     2
0095bb07 push     eax
0095bb08 mov      ecx, ebx
0095bb0a call     0x4282f7
0095bb0f cmp      dword ptr [ebp - 0x18], 0
0095bb13 mov      esi, dword ptr [eax + 4]
0095bb16 je       0x95bb26
0095bb18 push     0
0095bb1a lea      ecx, [ebp - 0x1c]
0095bb1d call     0x428a50
0095bb22 and      dword ptr [ebp - 0x18], 0
0095bb26 test     esi, esi
0095bb28 je       0x95bb42
0095bb2a lea      ecx, [esi + 0xc]
0095bb2d call     0x42873d
0095bb32 cmp      eax, dword ptr [ebp + 0xc]
0095bb35 jne      0x95bb42
0095bb37 mov      eax, dword ptr [esi]
0095bb39 mov      ecx, esi
0095bb3b call     dword ptr [eax + 4]
0095bb3e test     eax, eax
0095bb40 je       0x95bbab
0095bb42 inc      dword ptr [ebp + 8]
0095bb45 mov      eax, dword ptr [ebp + 8]
0095bb48 cmp      eax, dword ptr [ebp - 0x10]
0095bb4b jle      0x95baff
0095bb4d inc      dword ptr [edi + 0x3258]
0095bb53 cmp      dword ptr [edi + 0x3258], 3
0095bb5a lea      eax, [edi + 0x3258]
0095bb60 jg       0x95bb6b
0095bb62 push     4
0095bb64 mov      ecx, edi
0095bb66 call     0x9436d2
0095bb6b lea      eax, [ebp + 8]
0095bb6e push     0xd69
0095bb73 push     eax
0095bb74 call     0x79e805
0095bb79 mov      ecx, eax
0095bb7b call     0x406455
0095bb80 and      dword ptr [ebp - 4], 0
0095bb84 push     0xc
0095bb86 push     eax
0095bb87 call     0x4906b5
0095bb8c or       dword ptr [ebp - 4], 0xffffffff
0095bb90 pop      ecx
0095bb91 pop      ecx
0095bb92 lea      ecx, [ebp + 8]
0095bb95 call     0x4062df
0095bb9a mov      ecx, dword ptr [ebp - 0xc]
0095bb9d pop      edi
0095bb9e pop      esi
0095bb9f pop      ebx
0095bba0 mov      dword ptr fs:[0], ecx
0095bba7 leave    
0095bba8 ret      0xc
0095bbab cmp      dword ptr [ebp + 8], 0
0095bbaf je       0x95bb4d
0095bbb1 mov      eax, dword ptr [ebp - 0x14]
0095bbb4 mov      ecx, dword ptr [0xbe7918]
0095bbba xor      esi, esi
0095bbbc push     esi
0095bbbd push     dword ptr [ebp + 0x10]
0095bbc0 push     esi
0095bbc1 push     dword ptr [ebp + 0xc]
0095bbc4 push     dword ptr [ebp + 8]
0095bbc7 push     dword ptr [eax + 0xa4]
0095bbcd push     dword ptr [eax + 0xa0]
0095bbd3 call     0xa0955c
0095bbd8 mov      dword ptr [edi + 0x3258], esi
0095bbde jmp      0x95bb9a
