0095bbe0 mov      eax, 0xaded60
0095bbe5 call     0xa60b98
0095bbea sub      esp, 0x10
0095bbed push     ebx
0095bbee mov      ebx, ecx
0095bbf0 cmp      dword ptr [ebx + 0x2ae4], 0
0095bbf7 push     esi
0095bbf8 push     edi
0095bbf9 mov      dword ptr [ebp - 0x10], ebx
0095bbfc je       0x95bdc2
0095bc02 mov      esi, dword ptr [0xbe7918]
0095bc08 lea      eax, [ebp - 0x1c]
0095bc0b push     eax
0095bc0c mov      ecx, esi
0095bc0e call     0x425d0b
0095bc13 mov      edi, dword ptr [eax + 4]
0095bc16 lea      ecx, [ebp - 0x1c]
0095bc19 call     0x4289b7
0095bc1e mov      eax, dword ptr [ebx + 0x1edc]
0095bc24 mov      eax, dword ptr [eax + 4]
0095bc27 test     eax, eax
0095bc29 je       0x95bdc2
0095bc2f push     dword ptr [eax + 0x178]
0095bc35 add      eax, 0x170
0095bc3a push     eax
0095bc3b call     0x508a95
0095bc40 test     eax, eax
0095bc42 pop      ecx
0095bc43 pop      ecx
0095bc44 je       0x95bdc2
0095bc4a mov      ecx, ebx
0095bc4c call     0x94be9c
0095bc51 test     eax, eax
0095bc53 jne      0x95bdc2
0095bc59 push     dword ptr [edi + 0x75]
0095bc5c lea      eax, [edi + 0x71]
0095bc5f push     eax
0095bc60 call     0x4746dd
0095bc65 push     dword ptr [esi + 0x2130]
0095bc6b add      esi, 0x2128
0095bc71 push     esi
0095bc72 mov      ebx, eax
0095bc74 call     0x416563
0095bc79 mov      ecx, eax
0095bc7b movsx    eax, bx
0095bc7e sub      eax, dword ptr [ebp + 8]
0095bc81 xor      esi, esi
0095bc83 imul     eax, eax, 0x64
0095bc86 cdq      
0095bc87 idiv     ecx
0095bc89 mov      ecx, dword ptr [0xbebf9c]
0095bc8f mov      ecx, dword ptr [ecx + 0x84]
0095bc95 lea      ecx, [ecx + ecx*4]
0095bc98 add      esp, 0x10
0095bc9b cmp      ecx, esi
0095bc9d je       0x95bca3
0095bc9f cmp      eax, ecx
0095bca1 jl       0x95bcac
0095bca3 cmp      dword ptr [ebp + 0xc], esi
0095bca6 je       0x95bdc2
0095bcac mov      eax, dword ptr [0xbed5a0]
0095bcb1 mov      eax, dword ptr [eax + 0x3c4]
0095bcb7 cmp      eax, esi
0095bcb9 mov      dword ptr [ebp + 0xc], eax
0095bcbc je       0x95bdc2
0095bcc2 mov      ebx, dword ptr [ebp - 0x10]
0095bcc5 mov      eax, dword ptr [ebx + 0x1edc]
0095bccb mov      eax, dword ptr [eax + esi*8 + 4]
0095bccf test     eax, eax
0095bcd1 je       0x95bdc2
0095bcd7 push     dword ptr [eax + 0x178]
0095bcdd add      eax, 0x170
0095bce2 push     eax
0095bce3 call     0x508a95
0095bce8 test     eax, eax
0095bcea pop      ecx
0095bceb pop      ecx
0095bcec jne      0x95bcf9
0095bcee inc      esi
0095bcef cmp      esi, 3
0095bcf2 jl       0x95bcc2
0095bcf4 jmp      0x95bdc2
0095bcf9 mov      eax, dword ptr [ebx + 0x1edc]
0095bcff mov      ebx, dword ptr [eax + esi*8 + 4]
0095bd03 test     ebx, ebx
0095bd05 je       0x95bdc2
0095bd0b mov      eax, dword ptr [edi + 0x44f]
0095bd11 test     eax, eax
0095bd13 je       0x95bd18
0095bd15 mov      eax, dword ptr [eax - 4]
0095bd18 push     1
0095bd1a dec      eax
0095bd1b pop      ecx
0095bd1c cmp      eax, ecx
0095bd1e mov      dword ptr [ebp - 0x14], eax
0095bd21 mov      dword ptr [ebp + 8], ecx
0095bd24 jl       0x95bd74
0095bd26 push     dword ptr [ebp + 8]
0095bd29 lea      eax, [ebp - 0x1c]
0095bd2c push     2
0095bd2e push     eax
0095bd2f mov      ecx, edi
0095bd31 call     0x4282f7
0095bd36 cmp      dword ptr [ebp - 0x18], 0
0095bd3a mov      esi, dword ptr [eax + 4]
0095bd3d je       0x95bd4d
0095bd3f push     0
0095bd41 lea      ecx, [ebp - 0x1c]
0095bd44 call     0x428a50
0095bd49 and      dword ptr [ebp - 0x18], 0
0095bd4d test     esi, esi
0095bd4f je       0x95bd69
0095bd51 lea      ecx, [esi + 0xc]
0095bd54 call     0x42873d
0095bd59 cmp      eax, dword ptr [ebp + 0xc]
0095bd5c jne      0x95bd69
0095bd5e mov      eax, dword ptr [esi]
0095bd60 mov      ecx, esi
0095bd62 call     dword ptr [eax + 4]
0095bd65 test     eax, eax
0095bd67 je       0x95bdd3
0095bd69 inc      dword ptr [ebp + 8]
0095bd6c mov      eax, dword ptr [ebp + 8]
0095bd6f cmp      eax, dword ptr [ebp - 0x14]
0095bd72 jle      0x95bd26
0095bd74 mov      ecx, dword ptr [ebp - 0x10]
0095bd77 inc      dword ptr [ecx + 0x325c]
0095bd7d cmp      dword ptr [ecx + 0x325c], 3
0095bd84 lea      eax, [ecx + 0x325c]
0095bd8a jg       0x95bd93
0095bd8c push     5
0095bd8e call     0x9436d2
0095bd93 lea      eax, [ebp + 8]
0095bd96 push     0xd6a
0095bd9b push     eax
0095bd9c call     0x79e805
0095bda1 mov      ecx, eax
0095bda3 call     0x406455
0095bda8 and      dword ptr [ebp - 4], 0
0095bdac push     0xc
0095bdae push     eax
0095bdaf call     0x4906b5
0095bdb4 or       dword ptr [ebp - 4], 0xffffffff
0095bdb8 pop      ecx
0095bdb9 pop      ecx
0095bdba lea      ecx, [ebp + 8]
0095bdbd call     0x4062df
0095bdc2 mov      ecx, dword ptr [ebp - 0xc]
0095bdc5 pop      edi
0095bdc6 pop      esi
0095bdc7 pop      ebx
0095bdc8 mov      dword ptr fs:[0], ecx
0095bdcf leave    
0095bdd0 ret      0xc
0095bdd3 cmp      dword ptr [ebp + 8], 0
0095bdd7 je       0x95bd74
0095bdd9 mov      ecx, dword ptr [0xbe7918]
0095bddf push     1
0095bde1 push     dword ptr [ebp + 0x10]
0095bde4 push     1
0095bde6 push     dword ptr [ebp + 0xc]
0095bde9 push     dword ptr [ebp + 8]
0095bdec push     dword ptr [ebx + 0xa4]
0095bdf2 push     dword ptr [ebx + 0xa0]
0095bdf8 call     0xa0955c
0095bdfd jmp      0x95bdc2
