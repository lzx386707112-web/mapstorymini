006789ed push     ebp
006789ee mov      ebp, esp
006789f0 sub      esp, 0x1c
006789f3 and      dword ptr [ebp - 8], 0
006789f7 cmp      dword ptr [ebp + 0x1c], 0
006789fb push     ebx
006789fc push     esi
006789fd push     edi
006789fe mov      dword ptr [ebp - 0xc], ecx
00678a01 je       0x678a39
00678a03 mov      ecx, dword ptr [0xbebf98]
00678a09 mov      eax, dword ptr [ecx]
00678a0b call     dword ptr [eax + 8]
00678a0e mov      ecx, dword ptr [eax + 0xccc]
00678a14 lea      esi, [eax + 0xccc]
00678a1a mov      eax, dword ptr [ecx]
00678a1c call     dword ptr [eax + 4]
00678a1f test     al, al
00678a21 je       0x678ae3
00678a27 mov      ecx, dword ptr [esi]
00678a29 test     ecx, ecx
00678a2b je       0x678ae3
00678a31 call     0x6635cd
00678a36 mov      dword ptr [ebp - 8], eax
00678a39 mov      esi, dword ptr [ebp + 0xc]
00678a3c mov      ebx, dword ptr [ebp + 0x10]
00678a3f mov      edi, dword ptr [ebp + 8]
00678a42 xor      eax, eax
00678a44 cmp      esi, ebx
00678a46 setg     al
00678a49 sub      edi, esi
00678a4b mov      dword ptr [ebp + 0xc], eax
00678a4e cmp      dword ptr [ebp + 0xc], 0
00678a52 je       0x678a5d
00678a54 xor      eax, eax
00678a56 cmp      esi, ebx
00678a58 setg     al
00678a5b jmp      0x678a64
00678a5d xor      eax, eax
00678a5f cmp      esi, ebx
00678a61 setl     al
00678a64 test     eax, eax
00678a66 je       0x678ae3
00678a68 cmp      dword ptr [ebp + 0xc], 0
00678a6c je       0x678a87
00678a6e mov      dword ptr [ebp - 0x14], esi
00678a71 mov      eax, edi
00678a73 sub      esi, 0x14
00678a76 add      edi, 0x14
00678a79 cmp      ebx, esi
00678a7b jge      0x678a82
00678a7d mov      dword ptr [ebp - 0x1c], esi
00678a80 jmp      0x678a9f
00678a82 mov      dword ptr [ebp - 0x1c], ebx
00678a85 jmp      0x678a9f
00678a87 mov      eax, esi
00678a89 sub      eax, dword ptr [ebp + 8]
00678a8c mov      dword ptr [ebp - 0x1c], esi
00678a8f add      esi, 0x14
00678a92 sub      edi, 0x14
00678a95 cmp      esi, ebx
00678a97 mov      dword ptr [ebp - 0x14], esi
00678a9a jl       0x678a9f
00678a9c mov      dword ptr [ebp - 0x14], ebx
00678a9f cdq      
00678aa0 idiv     dword ptr [ebp + 0x18]
00678aa3 mov      edx, dword ptr [ebp + 0x14]
00678aa6 mov      ecx, edx
00678aa8 sub      ecx, eax
00678aaa add      eax, edx
00678aac mov      dword ptr [ebp - 0x10], eax
00678aaf xor      eax, eax
00678ab1 cmp      dword ptr [ebp + 0x1c], eax
00678ab4 push     eax
00678ab5 push     eax
00678ab6 mov      dword ptr [ebp - 0x18], ecx
00678ab9 push     eax
00678aba je       0x678adb
00678abc push     dword ptr [ebp - 8]
00678abf mov      ecx, dword ptr [ebp - 0xc]
00678ac2 push     eax
00678ac3 push     1
00678ac5 lea      eax, [ebp - 4]
00678ac8 push     eax
00678ac9 lea      eax, [ebp - 0x1c]
00678acc push     eax
00678acd call     0x678476
00678ad2 test     eax, eax
00678ad4 jne      0x678ade
00678ad6 jmp      0x678a4e
00678adb push     eax
00678adc jmp      0x678abf
00678ade mov      eax, dword ptr [ebp - 4]
00678ae1 jmp      0x678ae5
00678ae3 xor      eax, eax
00678ae5 pop      edi
00678ae6 pop      esi
00678ae7 pop      ebx
00678ae8 leave    
00678ae9 ret      0x18
