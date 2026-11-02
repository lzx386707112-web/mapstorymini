008dea76 mov      eax, 0xad3118
008dea7b call     0xa60b98
008dea80 sub      esp, 0x70
008dea83 push     ebx
008dea84 push     esi
008dea85 push     edi
008dea86 mov      edi, ecx
008dea88 mov      esi, 0xbf6300
008dea8d push     esi
008dea8e lea      ecx, [ebp - 0x6c]
008dea91 call     0x402f85
008dea96 xor      ebx, ebx
008dea98 push     esi
008dea99 lea      ecx, [ebp - 0x5c]
008dea9c mov      dword ptr [ebp - 4], ebx
008dea9f call     0x402f85
008deaa4 push     ebx
008deaa5 push     ebx
008deaa6 lea      eax, [ebp - 0x6c]
008deaa9 push     eax
008deaaa lea      eax, [ebp - 0x5c]
008deaad push     eax
008deaae push     ecx
008deaaf mov      eax, esp
008deab1 mov      dword ptr [ebp - 0x24], esp
008deab4 push     0xb64
008deab9 push     eax
008deaba mov      byte ptr [ebp - 4], 1
008deabe call     0x79e805
008deac3 mov      ecx, eax
008deac5 call     0x406292
008deaca lea      eax, [ebp - 0x7c]
008deacd push     eax
008deace mov      byte ptr [ebp - 4], 1
008dead2 call     0x406472
008dead7 mov      ecx, eax
008dead9 call     0x402680
008deade mov      ecx, eax
008deae0 call     0x403a93
008deae5 mov      ecx, eax
008deae7 mov      byte ptr [ebp - 4], 3
008deaeb call     0x4032b2
008deaf0 cmp      eax, ebx
008deaf2 mov      dword ptr [ebp - 0x18], eax
008deaf5 mov      dword ptr [ebp - 0x14], ebx
008deaf8 je       0x8deb17
008deafa lea      eax, [ebp - 0x18]
008deafd push     eax
008deafe lea      ecx, [ebp - 0x14]
008deb01 call     0x4052ad
008deb06 cmp      eax, ebx
008deb08 jge      0x8deb17
008deb0a cmp      eax, 0x80004002
008deb0f je       0x8deb17
008deb11 push     eax
008deb12 call     0xa5fde4
008deb17 lea      eax, [ebp - 0x7c]
008deb1a push     eax
008deb1b mov      byte ptr [ebp - 4], 7
008deb1f call     0x40291d
008deb24 cmp      eax, ebx
008deb26 pop      ecx
008deb27 jge      0x8deb2f
008deb29 push     eax
008deb2a call     0xa5fde4
008deb2f lea      eax, [ebp - 0x5c]
008deb32 push     eax
008deb33 mov      byte ptr [ebp - 4], 6
008deb37 call     0x40291d
008deb3c cmp      eax, ebx
008deb3e pop      ecx
008deb3f jge      0x8deb47
008deb41 push     eax
008deb42 call     0xa5fde4
008deb47 lea      eax, [ebp - 0x6c]
008deb4a push     eax
008deb4b mov      byte ptr [ebp - 4], 5
008deb4f call     0x40291d
008deb54 cmp      eax, ebx
008deb56 pop      ecx
008deb57 jge      0x8deb5f
008deb59 push     eax
008deb5a call     0xa5fde4
008deb5f mov      dword ptr [ebp - 0x10], ebx
008deb62 mov      ecx, dword ptr [0xbec20c]
008deb68 lea      eax, [ebp - 0x24]
008deb6b push     eax
008deb6c mov      byte ptr [ebp - 4], 8
008deb70 call     0x48bba5
008deb75 mov      eax, dword ptr [eax]
008deb77 push     ebx
008deb78 push     0xff
008deb7d push     ebx
008deb7e push     ecx
008deb7f add      edi, 0xbb8
008deb85 mov      ecx, esp
008deb87 mov      dword ptr [ebp - 0x28], esp
008deb8a push     edi
008deb8b mov      dword ptr [ebp - 0x1c], eax
008deb8e call     0x4145ab
008deb93 push     0x244
008deb98 push     0xda
008deb9d push     ecx
008deb9e lea      eax, [ebp - 0x1c]
008deba1 mov      ecx, esp
008deba3 mov      dword ptr [ebp - 0x2c], esp
008deba6 push     eax
008deba7 mov      byte ptr [ebp - 4], 0xa
008debab call     0x596572
008debb0 push     ebx
008debb1 push     ecx
008debb2 lea      eax, [ebp - 0x14]
008debb5 mov      ecx, esp
008debb7 mov      dword ptr [ebp - 0x2c], esp
008debba push     eax
008debbb call     0x4281c2
008debc0 lea      eax, [ebp - 0x20]
008debc3 push     eax
008debc4 mov      byte ptr [ebp - 4], 9
008debc8 call     0x43ea3e
008debcd add      esp, 0x28
008debd0 push     dword ptr [eax]
008debd2 lea      ecx, [ebp - 0x10]
008debd5 call     0x428712
008debda mov      eax, dword ptr [ebp - 0x20]
008debdd cmp      eax, ebx
008debdf je       0x8debe7
008debe1 mov      ecx, dword ptr [eax]
008debe3 push     eax
008debe4 call     dword ptr [ecx + 8]
008debe7 mov      eax, dword ptr [ebp - 0x24]
008debea cmp      eax, ebx
008debec mov      byte ptr [ebp - 4], 8
008debf0 je       0x8debf8
008debf2 mov      ecx, dword ptr [eax]
008debf4 push     eax
008debf5 call     dword ptr [ecx + 8]
008debf8 mov      edi, dword ptr [0xaf0268]
008debfe lea      eax, [ebp - 0x4c]
008dec01 push     eax
008dec02 call     edi
008dec04 lea      eax, [ebp - 0x4c]
008dec07 push     esi
008dec08 push     eax
008dec09 call     0x402b8c
008dec0e cmp      eax, ebx
008dec10 pop      ecx
008dec11 pop      ecx
008dec12 jge      0x8dec1a
008dec14 push     eax
008dec15 call     0xa5fde4
008dec1a lea      eax, [ebp - 0x3c]
008dec1d push     eax
008dec1e mov      byte ptr [ebp - 4], 0xb
008dec22 call     edi
008dec24 lea      eax, [ebp - 0x3c]
008dec27 push     esi
008dec28 push     eax
008dec29 call     0x402b8c
008dec2e cmp      eax, ebx
008dec30 pop      ecx
008dec31 pop      ecx
008dec32 jge      0x8dec3a
008dec34 push     eax
008dec35 call     0xa5fde4
008dec3a mov      eax, dword ptr [ebp - 0x10]
008dec3d cmp      eax, ebx
008dec3f mov      byte ptr [ebp - 4], 0xc
008dec43 jne      0x8dec52
008dec45 push     0x80004003
008dec4a call     0xa5fde4
008dec4f mov      eax, dword ptr [ebp - 0x10]
008dec52 sub      esp, 0x10
008dec55 mov      edi, esp
008dec57 lea      esi, [ebp - 0x4c]
008dec5a movsd    dword ptr es:[edi], dword ptr [esi]
008dec5b movsd    dword ptr es:[edi], dword ptr [esi]
008dec5c mov      ecx, dword ptr [eax]
008dec5e movsd    dword ptr es:[edi], dword ptr [esi]
008dec5f movsd    dword ptr es:[edi], dword ptr [esi]
008dec60 sub      esp, 0x10
008dec63 mov      edi, esp
008dec65 lea      esi, [ebp - 0x3c]
008dec68 movsd    dword ptr es:[edi], dword ptr [esi]
008dec69 movsd    dword ptr es:[edi], dword ptr [esi]
008dec6a movsd    dword ptr es:[edi], dword ptr [esi]
008dec6b push     ebx
008dec6c push     eax
008dec6d mov      dword ptr [ebp - 0x24], eax
008dec70 movsd    dword ptr es:[edi], dword ptr [esi]
008dec71 call     dword ptr [ecx + 0x110]
008dec77 cmp      eax, ebx
008dec79 jge      0x8dec89
008dec7b push     0xbd8358
008dec80 push     dword ptr [ebp - 0x24]
008dec83 push     eax
008dec84 call     0xa5fdf2
008dec89 lea      eax, [ebp - 0x3c]
008dec8c push     eax
008dec8d mov      byte ptr [ebp - 4], 0xb
008dec91 call     0x40291d
008dec96 cmp      eax, ebx
008dec98 pop      ecx
008dec99 jge      0x8deca1
008dec9b push     eax
008dec9c call     0xa5fde4
008deca1 lea      eax, [ebp - 0x4c]
008deca4 push     eax
008deca5 mov      byte ptr [ebp - 4], 8
008deca9 call     0x40291d
008decae cmp      eax, ebx
008decb0 pop      ecx
008decb1 jge      0x8decb9
008decb3 push     eax
008decb4 call     0xa5fde4
008decb9 push     ecx
008decba mov      ecx, dword ptr [ebp - 0x10]
008decbd mov      eax, esp
008decbf mov      dword ptr [eax], ecx
008decc1 mov      eax, dword ptr [ebp - 0x10]
008decc4 cmp      eax, ebx
008decc6 mov      dword ptr [ebp - 0x2c], esp
008decc9 je       0x8decd1
008deccb mov      ecx, dword ptr [eax]
008deccd push     eax
008decce call     dword ptr [ecx + 4]
008decd1 mov      byte ptr [ebp - 4], 8
008decd5 call     0x432cbc
008decda mov      ecx, eax
008decdc call     0x435d6f
008dece1 mov      eax, dword ptr [ebp - 0x10]
008dece4 cmp      eax, ebx
008dece6 mov      byte ptr [ebp - 4], 5
008decea je       0x8decf2
008decec mov      ecx, dword ptr [eax]
008decee push     eax
008decef call     dword ptr [ecx + 8]
008decf2 mov      eax, dword ptr [ebp - 0x14]
008decf5 or       dword ptr [ebp - 4], 0xffffffff
008decf9 cmp      eax, ebx
008decfb je       0x8ded03
008decfd mov      ecx, dword ptr [eax]
008decff push     eax
008ded00 call     dword ptr [ecx + 8]
008ded03 mov      ecx, dword ptr [ebp - 0xc]
008ded06 pop      edi
008ded07 pop      esi
008ded08 mov      dword ptr fs:[0], ecx
008ded0f pop      ebx
008ded10 leave    
008ded11 ret      
