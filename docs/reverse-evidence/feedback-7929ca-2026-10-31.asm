007929CA push ebp
007929CB mov ebp, esp
007929CD sub esp, 0x10
007929D0 push ebx
007929D1 push esi
007929D2 mov esi, dword ptr [ebp + 0x14]
007929D5 push edi
007929D6 push dword ptr [esi + 0xf8]
007929DC lea eax, [esi + 0xf0]
007929E2 push eax
007929E3 call 0x416563
007929E8 pop ecx
007929E9 pop ecx
007929EA push dword ptr [ebp + 0xc]
007929ED mov ecx, esi
007929EF mov edi, eax
007929F1 call 0x77deb2
007929F6 add eax, edi
007929F8 test eax, eax
007929FA jg 0x7929fe
007929FC xor eax, eax
007929FE mov ecx, 0x3e7
00792A03 cmp eax, ecx
00792A05 mov esi, eax
00792A07 jl 0x792a0b
00792A09 mov esi, ecx
00792A0B mov ebx, dword ptr [ebp + 0x10]
00792A0E push dword ptr [ebx + 0x14]
00792A11 lea eax, [ebx + 0xc]
00792A14 push eax
00792A15 mov dword ptr [ebp + 0x14], eax
00792A18 call 0x416563
00792A1D mov edi, dword ptr [ebp + 8]
00792A20 cmp eax, dword ptr [edi]
00792A22 pop ecx
00792A23 pop ecx
00792A24 jge 0x792a4a
00792A26 push dword ptr [ebx + 0x14]
00792A29 push dword ptr [ebp + 0x14]
00792A2C call 0x416563
00792A31 pop ecx
00792A32 pop ecx
00792A33 mov ecx, eax
00792A35 mov eax, dword ptr [edi]
00792A37 sub eax, ecx
00792A39 cdq 
00792A3A sub eax, edx
00792A3C sar eax, 1
00792A3E sub esi, eax
00792A40 test esi, esi
00792A42 jg 0x792a4a
00792A44 and dword ptr [ebp + 0x14], 0
00792A48 jmp 0x792a4d
00792A4A mov dword ptr [ebp + 0x14], esi
00792A4D fild dword ptr [ebp + 0x14]
00792A50 fld st(0)
00792A52 fmul qword ptr [0xafe380]
00792A58 fstp qword ptr [ebp - 8]
00792A5B fcom qword ptr [ebp - 8]
00792A5E fst qword ptr [ebp - 0x10]
00792A61 fnstsw ax
00792A63 sahf 
00792A64 jbe 0x792a79
00792A66 lea eax, [ebp - 8]
00792A69 fstp st(0)
00792A6B push eax
00792A6C lea eax, [ebp - 0x10]
00792A6F push eax
00792A70 call 0x79460a
00792A75 pop ecx
00792A76 pop ecx
00792A77 jmp 0x792a83
00792A79 fcom qword ptr [ebp - 8]
00792A7C fnstsw ax
00792A7E sahf 
00792A7F je 0x792aaa
00792A81 fstp st(0)
00792A83 mov eax, dword ptr [ebp + 0x18]
00792A86 fld qword ptr [ebp - 8]
00792A89 xor edx, edx
00792A8B fsub qword ptr [ebp - 0x10]
00792A8E mov ecx, 0x989680
00792A93 div ecx
00792A95 and dword ptr [ebp - 4], 0
00792A99 mov dword ptr [ebp - 8], edx
00792A9C fild qword ptr [ebp - 8]
00792A9F fmulp st(1)
00792AA1 fmul qword ptr [0xafe8a8]
00792AA7 fadd qword ptr [ebp - 0x10]
00792AAA mov eax, dword ptr [edi + 0x68]
00792AAD add eax, dword ptr [edi + 0x64]
00792AB0 pop edi
00792AB1 test eax, eax
00792AB3 pop esi
00792AB4 pop ebx
00792AB5 jg 0x792ab9
00792AB7 xor eax, eax
00792AB9 mov ecx, 0x3e7
00792ABE cmp eax, ecx
00792AC0 mov dword ptr [ebp + 0x14], eax
00792AC3 jl 0x792ac8
00792AC5 mov dword ptr [ebp + 0x14], ecx
00792AC8 fild dword ptr [ebp + 0x14]
00792ACB fld st(1)
00792ACD fcompp 
00792ACF fnstsw ax
00792AD1 fstp st(0)
00792AD3 sahf 
00792AD4 jb 0x792adb
00792AD6 push 1
00792AD8 pop eax
00792AD9 jmp 0x792add
00792ADB xor eax, eax
00792ADD leave 
00792ADE ret 0x14