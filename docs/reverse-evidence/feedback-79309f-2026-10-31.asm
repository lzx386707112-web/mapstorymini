0079309f push     ebp
007930a0 mov      ebp, esp
007930a2 sub      esp, 0x20
007930a5 push     ebx
007930a6 push     esi
007930a7 mov      esi, dword ptr [ebp + 0x10]
007930aa push     edi
007930ab push     dword ptr [ebp + 0x1c]
007930ae mov      edi, dword ptr [ebp + 8]
007930b1 push     dword ptr [ebp + 0x14]
007930b4 mov      ebx, ecx
007930b6 push     esi
007930b7 push     dword ptr [ebp + 0xc]
007930ba mov      dword ptr [ebp - 0x18], ebx
007930bd push     edi
007930be call     0x79286e
007930c3 test     eax, eax
007930c5 je       0x7930ce
007930c7 xor      eax, eax
007930c9 jmp      0x793457
007930ce push     dword ptr [esi + 0x20]
007930d1 lea      eax, [esi + 0x18]
007930d4 push     eax
007930d5 call     0x416563
007930da pop      ecx
007930db cdq      
007930dc pop      ecx
007930dd mov      ecx, 0x3e8
007930e2 idiv     ecx
007930e4 push     0x64
007930e6 pop      ecx
007930e7 mov      eax, edx
007930e9 cdq      
007930ea idiv     ecx
007930ec mov      ecx, dword ptr [ebp + 0x18]
007930ef test     ecx, ecx
007930f1 mov      dword ptr [ebp + 0x10], eax
007930f4 mov      eax, dword ptr [edi + 0x28]
007930f7 je       0x7930fe
007930f9 add      eax, dword ptr [ecx + 0x18]
007930fc jmp      0x793101
007930fe add      eax, dword ptr [edi + 0x24]
00793101 test     eax, eax
00793103 jg       0x793107
00793105 xor      eax, eax
00793107 mov      ecx, 0x7cf
0079310c cmp      eax, ecx
0079310e mov      dword ptr [ebp + 0x1c], eax
00793111 jl       0x793116
00793113 mov      dword ptr [ebp + 0x1c], ecx
00793116 lea      ecx, [ebx + 0xa0]
0079311c call     0x4165ec
00793121 fild     dword ptr [ebp + 0x1c]
00793124 mov      edi, eax
00793126 fst      qword ptr [ebp - 0x14]
00793129 fmul     qword ptr [0xaf1210]
0079312f fst      qword ptr [ebp - 0xc]
00793132 fld      qword ptr [ebp - 0x14]
00793135 fmul     qword ptr [0xaf0d40]
0079313b fstp     qword ptr [ebp - 0x20]
0079313e fcom     qword ptr [ebp - 0x20]
00793141 fnstsw   ax
00793143 sahf     
00793144 jbe      0x79315c
00793146 lea      eax, [ebp - 0x20]
00793149 fstp     st(0)
0079314b push     eax
0079314c lea      eax, [ebp - 0xc]
0079314f push     eax
00793150 call     0x79460a
00793155 fld      qword ptr [ebp - 0xc]
00793158 pop      ecx
00793159 pop      ecx
0079315a jmp      0x793164
0079315c fcom     qword ptr [ebp - 0x20]
0079315f fnstsw   ax
00793161 sahf     
00793162 je       0x793188
00793164 xor      edx, edx
00793166 fld      qword ptr [ebp - 0x20]
00793169 mov      eax, edi
0079316b fsub     st(1)
0079316d mov      ecx, 0x989680
00793172 div      ecx
00793174 and      dword ptr [ebp - 0x1c], 0
00793178 mov      dword ptr [ebp - 0x20], edx
0079317b fild     qword ptr [ebp - 0x20]
0079317e fmulp    st(1)
00793180 fmul     qword ptr [0xafe8a8]
00793186 faddp    st(1)
00793188 push     dword ptr [ebp + 0xc]
0079318b mov      ecx, dword ptr [ebp + 0x14]
0079318e fmul     qword ptr [ebp - 0x14]
00793191 fmul     qword ptr [0xaf14f0]
00793197 fstp     qword ptr [ebp - 0x20]
0079319a call     0x77e067
0079319f push     dword ptr [esi + 0x14]
007931a2 mov      edi, eax
007931a4 lea      ebx, [esi + 0xc]
007931a7 push     ebx
007931a8 mov      dword ptr [ebp + 0x18], edi
007931ab call     0x416563
007931b0 push     eax
007931b1 push     dword ptr [ebp + 0x10]
007931b4 call     0x792f75
007931b9 add      esp, 0x10
007931bc cmp      dword ptr [ebp + 0x10], 1
007931c0 push     dword ptr [esi + 0x2c]
007931c3 mov      dword ptr [ebp + 0x1c], eax
007931c6 lea      eax, [esi + 0x24]
007931c9 push     eax
007931ca jne      0x793226
007931cc call     0x416563
007931d1 push     dword ptr [esi + 0x38]
007931d4 mov      dword ptr [ebp - 0x10], eax
007931d7 lea      eax, [esi + 0x30]
007931da push     eax
007931db call     0x416563
007931e0 push     dword ptr [esi + 0x44]
007931e3 mov      dword ptr [ebp - 4], eax
007931e6 lea      eax, [esi + 0x3c]
007931e9 push     eax
007931ea call     0x416563
007931ef push     dword ptr [esi + 0x50]
007931f2 mov      dword ptr [ebp - 8], eax
007931f5 lea      eax, [esi + 0x48]
007931f8 push     eax
007931f9 call     0x416563
007931fe mov      dword ptr [ebp + 0x10], eax
00793201 fild     dword ptr [ebp + 0x10]
00793204 add      esp, 0x20
00793207 fiadd    dword ptr [ebp - 4]
0079320a fmul     qword ptr [0xafe988]
00793210 fild     dword ptr [ebp - 8]
00793213 fmul     qword ptr [0xafe980]
00793219 faddp    st(1)
0079321b fild     dword ptr [ebp - 0x10]
0079321e fmul     qword ptr [0xafe978]
00793224 jmp      0x793286
00793226 call     0x416563
0079322b push     dword ptr [esi + 0x38]
0079322e mov      dword ptr [ebp - 4], eax
00793231 lea      eax, [esi + 0x30]
00793234 push     eax
00793235 call     0x416563
0079323a push     dword ptr [esi + 0x44]
0079323d mov      dword ptr [ebp - 8], eax
00793240 lea      eax, [esi + 0x3c]
00793243 push     eax
00793244 call     0x416563
00793249 push     dword ptr [esi + 0x50]
0079324c mov      dword ptr [ebp - 0x10], eax
0079324f lea      eax, [esi + 0x48]
00793252 push     eax
00793253 call     0x416563
00793258 mov      dword ptr [ebp + 0x10], eax
0079325b fild     dword ptr [ebp + 0x10]
0079325e add      esp, 0x20
00793261 fmul     qword ptr [0xafe988]
00793267 fild     dword ptr [ebp - 0x10]
0079326a fmul     qword ptr [0xafe980]
00793270 faddp    st(1)
00793272 fild     dword ptr [ebp - 8]
00793275 fmul     qword ptr [0xafe978]
0079327b faddp    st(1)
0079327d fild     dword ptr [ebp - 4]
00793280 fmul     qword ptr [0xafe970]
00793286 faddp    st(1)
00793288 call     0xa62018
0079328d cmp      edi, dword ptr [ebp + 0x1c]
00793290 mov      dword ptr [ebp + 0x10], eax
00793293 jl       0x7932f3
00793295 push     dword ptr [esi + 0x14]
00793298 fild     dword ptr [ebp + 0x10]
0079329b push     ebx
0079329c fstp     qword ptr [ebp - 0x14]
0079329f call     0x416563
007932a4 sub      edi, dword ptr [ebp + 0x1c]
007932a7 mov      dword ptr [ebp + 0x10], eax
007932aa fild     dword ptr [ebp + 0x10]
007932ad mov      dword ptr [ebp + 0x1c], edi
007932b0 pop      ecx
007932b1 pop      ecx
007932b2 fmul     qword ptr [0xafe968]
007932b8 fld      qword ptr [ebp - 0x14]
007932bb fmul     qword ptr [0xafe960]
007932c1 faddp    st(1)
007932c3 fadd     qword ptr [0xafe958]
007932c9 fild     dword ptr [ebp + 0x1c]
007932cc fmulp    st(1)
007932ce fmul     qword ptr [0xafe7f8]
007932d4 fld      qword ptr [ebp - 0x14]
007932d7 fmul     qword ptr [0xafe950]
007932dd fadd     qword ptr [0xafe958]
007932e3 fimul    dword ptr [ebp + 0x18]
007932e6 faddp    st(1)
007932e8 fsubr    qword ptr [ebp - 0x20]
007932eb fstp     qword ptr [ebp - 0x20]
007932ee jmp      0x7933b2
007932f3 push     dword ptr [esi + 0x14]
007932f6 push     ebx
007932f7 call     0x416563
007932fc pop      ecx
007932fd pop      ecx
007932fe mov      ecx, dword ptr [ebp + 8]
00793301 cmp      eax, dword ptr [ecx]
00793303 jge      0x793347
00793305 fild     dword ptr [ebp + 0x10]
00793308 push     dword ptr [esi + 0x14]
0079330b push     ebx
0079330c fmul     qword ptr [0xafe950]
00793312 fstp     qword ptr [ebp - 0x14]
00793315 call     0x416563
0079331a sub      edi, dword ptr [ebp + 0x1c]
0079331d mov      dword ptr [ebp + 0x10], eax
00793320 fild     dword ptr [ebp + 0x10]
00793323 mov      dword ptr [ebp + 0x1c], edi
00793326 pop      ecx
00793327 pop      ecx
00793328 fmul     qword ptr [0xafe948]
0079332e fadd     qword ptr [ebp - 0x14]
00793331 fadd     qword ptr [0xafe958]
00793337 fild     dword ptr [ebp + 0x1c]
0079333a fmulp    st(1)
0079333c fmul     qword ptr [0xafe898]
00793342 fld      qword ptr [ebp - 0x14]
00793345 jmp      0x7932dd
00793347 push     dword ptr [esi + 0x14]
0079334a push     ebx
0079334b call     0x416563
00793350 push     dword ptr [esi + 0x14]
00793353 mov      dword ptr [ebp - 0x10], eax
00793356 push     ebx
00793357 call     0x416563
0079335c fild     dword ptr [ebp + 0x10]
0079335f sub      edi, dword ptr [ebp + 0x1c]
00793362 mov      ecx, dword ptr [ebp + 8]
00793365 fmul     qword ptr [0xafe950]
0079336b mov      dword ptr [ebp + 0x1c], edi
0079336e fild     dword ptr [ebp - 0x10]
00793371 add      esp, 0x10
00793374 sub      eax, dword ptr [ecx]
00793376 fmul     qword ptr [0xafe948]
0079337c fadd     st(1)
0079337e fadd     qword ptr [0xafe958]
00793384 fild     dword ptr [ebp + 0x1c]
00793387 mov      dword ptr [ebp + 0x1c], eax
0079338a fmulp    st(1)
0079338c fmul     qword ptr [0xafe940]
00793392 fild     dword ptr [ebp + 0x1c]
00793395 fadd     qword ptr [0xafe940]
0079339b fdivp    st(1)
0079339d fld      st(1)
0079339f fadd     qword ptr [0xafe958]
007933a5 fimul    dword ptr [ebp + 0x18]
007933a8 faddp    st(1)
007933aa fsubr    qword ptr [ebp - 0x20]
007933ad fstp     qword ptr [ebp - 0x20]
007933b0 fstp     st(0)
007933b2 mov      esi, dword ptr [ebp + 0x14]
007933b5 push     dword ptr [esi + 0x29c]
007933bb lea      eax, [esi + 0x294]
007933c1 push     eax
007933c2 call     0x416563
007933c7 push     dword ptr [esi + 0x47c]
007933cd mov      dword ptr [ebp + 0x14], eax
007933d0 fild     dword ptr [ebp + 0x14]
007933d3 add      esi, 0x474
007933d9 push     esi
007933da fmul     qword ptr [ebp - 0x20]
007933dd fmul     qword ptr [0xaf14f0]
007933e3 fsubr    qword ptr [ebp - 0x20]
007933e6 fstp     qword ptr [ebp - 0x20]
007933e9 call     0x416563
007933ee add      esp, 0x10
007933f1 test     eax, eax
007933f3 je       0x793411
007933f5 mov      esi, dword ptr [ebp + 0x20]
007933f8 test     esi, esi
007933fa je       0x793411
007933fc fld      qword ptr [ebp - 0x20]
007933ff push     ecx
00793400 push     ecx
00793401 mov      ecx, dword ptr [ebp - 0x18]
00793404 fstp     qword ptr [esp]
00793407 push     dword ptr [ebp + 0xc]
0079340a call     0x792fa8
0079340f mov      dword ptr [esi], eax
00793411 mov      eax, dword ptr [ebp + 8]
00793414 mov      eax, dword ptr [eax + 0xd4]
0079341a test     eax, eax
0079341c mov      dword ptr [ebp + 8], eax
0079341f je       0x79342f
00793421 fild     dword ptr [ebp + 8]
00793424 fmul     qword ptr [ebp - 0x20]
00793427 fmul     qword ptr [0xaf14f0]
0079342d jmp      0x793432
0079342f fld      qword ptr [ebp - 0x20]
00793432 fld1     
00793434 fcomp    st(1)
00793436 fnstsw   ax
00793438 sahf     
00793439 jb       0x79343f
0079343b fstp     st(0)
0079343d fld1     
0079343f fcom     qword ptr [0xafe8a0]
00793445 fnstsw   ax
00793447 sahf     
00793448 jb       0x793452
0079344a fstp     st(0)
0079344c fld      qword ptr [0xafe8a0]
00793452 call     0xa62018
00793457 pop      edi
00793458 pop      esi
00793459 pop      ebx
0079345a leave    
0079345b ret      0x1c
