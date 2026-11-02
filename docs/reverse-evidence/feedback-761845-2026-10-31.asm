00761845 push     ebp
00761846 mov      ebp, esp
00761848 push     ecx
00761849 push     ecx
0076184a push     ebx
0076184b push     esi
0076184c xor      esi, esi
0076184e xor      ebx, ebx
00761850 cmp      dword ptr [ebp + 0xc], esi
00761853 push     edi
00761854 mov      edi, dword ptr [ebp + 8]
00761857 mov      dword ptr [ebp - 8], ecx
0076185a mov      dword ptr [ebp - 4], esi
0076185d je       0x76186e
0076185f lea      eax, [ebp - 4]
00761862 push     eax
00761863 push     dword ptr [ebp + 0xc]
00761866 push     edi
00761867 call     0x7616f6
0076186c mov      ebx, eax
0076186e push     dword ptr [edi + 0x3d]
00761871 lea      eax, [edi + 0x39]
00761874 push     eax
00761875 call     0x4746dd
0076187a pop      ecx
0076187b pop      ecx
0076187c mov      ecx, dword ptr [ebp - 4]
0076187f cmp      ecx, esi
00761881 movsx    eax, ax
00761884 mov      dword ptr [ebp + 8], eax
00761887 je       0x7618c9
00761889 push     ebx
0076188a call     0x760f23
0076188f push     dword ptr [eax + 0x1a4]
00761895 add      eax, 0x19c
0076189a push     eax
0076189b call     0x416563
007618a0 test     eax, eax
007618a2 pop      ecx
007618a3 pop      ecx
007618a4 jle      0x7618c9
007618a6 mov      ecx, dword ptr [ebp - 4]
007618a9 push     ebx
007618aa call     0x760f23
007618af push     dword ptr [eax + 0x1a4]
007618b5 add      eax, 0x19c
007618ba push     eax
007618bb call     0x416563
007618c0 pop      ecx
007618c1 pop      ecx
007618c2 mov      esi, eax
007618c4 jmp      0x7619a7
007618c9 cmp      dword ptr [ebp + 0xc], 0x3ee1ab
007618d0 je       0x7619a2
007618d6 cmp      dword ptr [ebp + 0xc], 0x40684b
007618dd je       0x7619a2
007618e3 mov      eax, dword ptr [ebp + 0x10]
007618e6 cmp      eax, 0x2d
007618e9 jl       0x7619a7
007618ef cmp      eax, 0x2e
007618f2 jle      0x761965
007618f4 cmp      eax, 0x2f
007618f7 je       0x76190c
007618f9 cmp      eax, 0x31
007618fc jne      0x7619a7
00761902 mov      esi, 0xc8
00761907 jmp      0x7619a7
0076190c lea      eax, [ebp + 0xc]
0076190f push     eax
00761910 mov      eax, dword ptr [ebp + 8]
00761913 cdq      
00761914 mov      ecx, 0x3e8
00761919 idiv     ecx
0076191b and      dword ptr [ebp + 0xc], 0
0076191f mov      ecx, dword ptr [ebp - 8]
00761922 mov      ebx, 0xc8
00761927 mov      esi, ebx
00761929 dec      eax
0076192a neg      eax
0076192c sbb      eax, eax
0076192e and      eax, 0xff676980
00761933 add      eax, 0xd59f81
00761938 push     eax
00761939 push     edi
0076193a call     0x7616f6
0076193f test     eax, eax
00761941 je       0x7619a7
00761943 mov      ecx, dword ptr [ebp + 0xc]
00761946 push     eax
00761947 call     0x760f23
0076194c push     dword ptr [eax + 0x1a4]
00761952 add      eax, 0x19c
00761957 push     eax
00761958 call     0x416563
0076195d pop      ecx
0076195e mov      esi, eax
00761960 pop      ecx
00761961 add      esi, ebx
00761963 jmp      0x7619a7
00761965 lea      eax, [ebp + 0xc]
00761968 push     eax
00761969 mov      eax, dword ptr [ebp + 8]
0076196c cdq      
0076196d mov      ecx, 0x3e8
00761972 idiv     ecx
00761974 and      dword ptr [ebp + 0xc], 0
00761978 mov      ecx, dword ptr [ebp - 8]
0076197b mov      ebx, 0x12c
00761980 mov      esi, ebx
00761982 dec      eax
00761983 neg      eax
00761985 sbb      eax, eax
00761987 and      eax, 0xff676981
0076198c add      eax, 0xc65d41
00761991 push     eax
00761992 push     edi
00761993 call     0x7616f6
00761998 test     eax, eax
0076199a je       0x7619a7
0076199c mov      ecx, dword ptr [ebp + 0xc]
0076199f push     eax
007619a0 jmp      0x761947
007619a2 mov      esi, 0x12c
007619a7 pop      edi
007619a8 mov      eax, esi
007619aa pop      esi
007619ab pop      ebx
007619ac leave    
007619ad ret      0xc
