00969B10 push esi
00969B11 mov esi, ecx
00969B13 call 0x95b85a
00969B18 test eax, eax
00969B1A jne 0x969b4f
00969B1C mov eax, dword ptr [esi + 0x570]
00969B22 and eax, 0xfffffffe
00969B25 cmp eax, 0xa
00969B28 je 0x969b3f
00969B2A xor eax, eax
00969B2C push eax
00969B2D push eax
00969B2E push dword ptr [esp + 0x14]
00969B32 mov ecx, esi
00969B34 push dword ptr [esp + 0x14]
00969B38 call 0x95571f
00969B3D jmp 0x969b51
00969B3F xor eax, eax
00969B41 push eax
00969B42 push eax
00969B43 push eax
00969B44 push eax
00969B45 push eax
00969B46 push eax
00969B47 push eax
00969B48 mov ecx, esi
00969B4A call 0x950921
00969B4F xor eax, eax
00969B51 pop esi
00969B52 ret 8