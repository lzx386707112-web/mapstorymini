0079286e push     ebp
0079286f mov      ebp, esp
00792871 push     ecx
00792872 push     ecx
00792873 push     ebx
00792874 push     esi
00792875 mov      esi, dword ptr [ebp + 0x14]
00792878 push     edi
00792879 push     dword ptr [esi + 0xf8]
0079287f lea      eax, [esi + 0xf0]
00792885 push     eax
00792886 call     0x416563
0079288b pop      ecx
0079288c pop      ecx
0079288d push     dword ptr [ebp + 0xc]
00792890 mov      ecx, esi
00792892 mov      edi, eax
00792894 call     0x77deb2
00792899 add      eax, edi
0079289b test     eax, eax
0079289d jg       0x7928a1
0079289f xor      eax, eax
007928a1 mov      ecx, 0x3e7
007928a6 cmp      eax, ecx
007928a8 mov      esi, eax
007928aa jl       0x7928ae
007928ac mov      esi, ecx
007928ae mov      ebx, dword ptr [ebp + 0x10]
007928b1 push     dword ptr [ebx + 0x14]
007928b4 lea      eax, [ebx + 0xc]
007928b7 push     eax
007928b8 call     0x416563
007928bd mov      edi, dword ptr [ebp + 8]
007928c0 cmp      eax, dword ptr [edi]
007928c2 pop      ecx
007928c3 pop      ecx
007928c4 jge      0x7928eb
007928c6 push     dword ptr [ebx + 0x14]
007928c9 lea      eax, [ebx + 0xc]
007928cc push     eax
007928cd call     0x416563
007928d2 pop      ecx
007928d3 pop      ecx
007928d4 mov      ecx, eax
007928d6 mov      eax, dword ptr [edi]
007928d8 sub      eax, ecx
007928da cdq      
007928db sub      eax, edx
007928dd sar      eax, 1
007928df sub      esi, eax
007928e1 test     esi, esi
007928e3 jg       0x7928eb
007928e5 and      dword ptr [ebp + 0x14], 0
007928e9 jmp      0x7928ee
007928eb mov      dword ptr [ebp + 0x14], esi
007928ee mov      eax, dword ptr [edi + 0x68]
007928f1 fild     dword ptr [ebp + 0x14]
007928f4 add      eax, dword ptr [edi + 0x64]
007928f7 test     eax, eax
007928f9 jg       0x7928fd
007928fb xor      eax, eax
007928fd mov      ecx, 0x3e7
00792902 cmp      eax, ecx
00792904 mov      dword ptr [ebp + 0x14], eax
00792907 jl       0x79290c
00792909 mov      dword ptr [ebp + 0x14], ecx
0079290c fild     dword ptr [ebp + 0x14]
0079290f push     dword ptr [ebx + 0x20]
00792912 add      ebx, 0x18
00792915 push     ebx
00792916 fmul     qword ptr [0xafe910]
0079291c fdivr    st(1)
0079291e fmul     qword ptr [0xaf29e0]
00792924 fstp     qword ptr [ebp - 8]
00792927 fstp     st(0)
00792929 call     0x416563
0079292e pop      ecx
0079292f pop      ecx
00792930 cdq      
00792931 mov      ecx, 0x3e8
00792936 idiv     ecx
00792938 push     0x64
0079293a pop      ecx
0079293b pop      edi
0079293c pop      esi
0079293d pop      ebx
0079293e mov      eax, edx
00792940 cdq      
00792941 idiv     ecx
00792943 cmp      eax, 4
00792946 je       0x792970
00792948 fld      qword ptr [0xafe908]
0079294e fcom     qword ptr [ebp - 8]
00792951 fnstsw   ax
00792953 sahf     
00792954 jae      0x79295b
00792956 fstp     st(0)
00792958 fld      qword ptr [ebp - 8]
0079295b fcom     qword ptr [0xafe900]
00792961 fnstsw   ax
00792963 sahf     
00792964 jb       0x792996
00792966 fstp     st(0)
00792968 fld      qword ptr [0xafe900]
0079296e jmp      0x792996
00792970 fld      qword ptr [0xaf8298]
00792976 fcom     qword ptr [ebp - 8]
00792979 fnstsw   ax
0079297b sahf     
0079297c jae      0x792983
0079297e fstp     st(0)
00792980 fld      qword ptr [ebp - 8]
00792983 fcom     qword ptr [0xafe8f8]
00792989 fnstsw   ax
0079298b sahf     
0079298c jb       0x792996
0079298e fstp     st(0)
00792990 fld      qword ptr [0xafe8f8]
00792996 mov      eax, dword ptr [ebp + 0x18]
00792999 xor      edx, edx
0079299b mov      ecx, 0x989680
007929a0 div      ecx
007929a2 and      dword ptr [ebp - 4], 0
007929a6 mov      dword ptr [ebp - 8], edx
007929a9 fild     qword ptr [ebp - 8]
007929ac fmul     qword ptr [0xafe838]
007929b2 fld      st(1)
007929b4 fcomp    st(1)
007929b6 fnstsw   ax
007929b8 fstp     st(0)
007929ba sahf     
007929bb fstp     st(0)
007929bd jbe      0x7929c4
007929bf push     1
007929c1 pop      eax
007929c2 jmp      0x7929c6
007929c4 xor      eax, eax
007929c6 leave    
007929c7 ret      0x14
