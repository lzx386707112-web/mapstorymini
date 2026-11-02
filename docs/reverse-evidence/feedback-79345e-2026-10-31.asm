0079345e push     ebp
0079345f mov      ebp, esp
00793461 sub      esp, 0x1c
00793464 push     ebx
00793465 push     esi
00793466 mov      esi, dword ptr [ebp + 0x10]
00793469 push     edi
0079346a push     dword ptr [ebp + 0x18]
0079346d mov      edi, dword ptr [ebp + 8]
00793470 push     dword ptr [ebp + 0x14]
00793473 mov      ebx, ecx
00793475 push     esi
00793476 push     dword ptr [ebp + 0xc]
00793479 mov      dword ptr [ebp - 4], ebx
0079347c push     edi
0079347d call     0x7929ca
00793482 test     eax, eax
00793484 je       0x79348d
00793486 xor      eax, eax
00793488 jmp      0x793680
0079348d mov      eax, dword ptr [edi + 0x48]
00793490 add      eax, dword ptr [edi + 0x44]
00793493 test     eax, eax
00793495 jg       0x793499
00793497 xor      eax, eax
00793499 mov      ecx, 0x7cf
0079349e cmp      eax, ecx
007934a0 mov      dword ptr [ebp + 0x18], eax
007934a3 jl       0x7934a8
007934a5 mov      dword ptr [ebp + 0x18], ecx
007934a8 lea      ecx, [ebx + 0xa0]
007934ae call     0x4165ec
007934b3 fild     dword ptr [ebp + 0x18]
007934b6 mov      ebx, eax
007934b8 fst      qword ptr [ebp - 0x14]
007934bb fmul     qword ptr [0xaf0d40]
007934c1 fst      qword ptr [ebp - 0x1c]
007934c4 fld      qword ptr [ebp - 0x14]
007934c7 fmul     qword ptr [0xafe918]
007934cd fstp     qword ptr [ebp - 0xc]
007934d0 fcom     qword ptr [ebp - 0xc]
007934d3 fnstsw   ax
007934d5 sahf     
007934d6 jbe      0x7934ee
007934d8 lea      eax, [ebp - 0xc]
007934db fstp     st(0)
007934dd push     eax
007934de lea      eax, [ebp - 0x1c]
007934e1 push     eax
007934e2 call     0x79460a
007934e7 fld      qword ptr [ebp - 0x1c]
007934ea pop      ecx
007934eb pop      ecx
007934ec jmp      0x7934f6
007934ee fcom     qword ptr [ebp - 0xc]
007934f1 fnstsw   ax
007934f3 sahf     
007934f4 je       0x79351a
007934f6 xor      edx, edx
007934f8 fld      qword ptr [ebp - 0xc]
007934fb mov      eax, ebx
007934fd fsub     st(1)
007934ff mov      ecx, 0x989680
00793504 div      ecx
00793506 and      dword ptr [ebp - 0x18], 0
0079350a mov      dword ptr [ebp - 0x1c], edx
0079350d fild     qword ptr [ebp - 0x1c]
00793510 fmulp    st(1)
00793512 fmul     qword ptr [0xafe8a8]
00793518 faddp    st(1)
0079351a mov      ebx, dword ptr [ebp + 0x14]
0079351d push     dword ptr [ebp + 0xc]
00793520 fmul     qword ptr [ebp - 0x14]
00793523 mov      ecx, ebx
00793525 fmul     qword ptr [0xaf14f0]
0079352b fstp     qword ptr [ebp - 0xc]
0079352e call     0x77e141
00793533 push     dword ptr [esi + 0x20]
00793536 mov      dword ptr [ebp + 0x14], eax
00793539 lea      eax, [esi + 0x18]
0079353c push     eax
0079353d call     0x416563
00793542 pop      ecx
00793543 cdq      
00793544 pop      ecx
00793545 mov      ecx, 0x3e8
0079354a idiv     ecx
0079354c push     0x64
0079354e pop      ecx
0079354f push     dword ptr [esi + 0x2c]
00793552 mov      eax, edx
00793554 cdq      
00793555 idiv     ecx
00793557 dec      eax
00793558 dec      eax
00793559 lea      eax, [esi + 0x24]
0079355c push     eax
0079355d je       0x7935b2
0079355f call     0x416563
00793564 push     dword ptr [esi + 0x38]
00793567 mov      dword ptr [ebp + 0x10], eax
0079356a lea      eax, [esi + 0x30]
0079356d push     eax
0079356e call     0x416563
00793573 push     dword ptr [esi + 0x50]
00793576 add      esi, 0x48
00793579 push     esi
0079357a mov      dword ptr [ebp + 8], eax
0079357d call     0x416563
00793582 mov      dword ptr [ebp + 0x18], eax
00793585 fild     dword ptr [ebp + 0x18]
00793588 add      esp, 0x18
0079358b fmul     qword ptr [0xaf1628]
00793591 fild     dword ptr [ebp + 0x10]
00793594 fmul     qword ptr [0xafe998]
0079359a faddp    st(1)
0079359c fild     dword ptr [ebp + 8]
0079359f fmul     qword ptr [0xafe990]
007935a5 faddp    st(1)
007935a7 fiadd    dword ptr [ebp + 0x14]
007935aa fmul     qword ptr [0xafe988]
007935b0 jmp      0x793603
007935b2 call     0x416563
007935b7 push     dword ptr [esi + 0x38]
007935ba mov      dword ptr [ebp + 0x10], eax
007935bd lea      eax, [esi + 0x30]
007935c0 push     eax
007935c1 call     0x416563
007935c6 push     dword ptr [esi + 0x50]
007935c9 add      esi, 0x48
007935cc push     esi
007935cd mov      dword ptr [ebp + 8], eax
007935d0 call     0x416563
007935d5 mov      dword ptr [ebp + 0x18], eax
007935d8 fild     dword ptr [ebp + 0x18]
007935db add      esp, 0x18
007935de fmul     qword ptr [0xaf1628]
007935e4 fild     dword ptr [ebp + 0x10]
007935e7 fmul     qword ptr [0xafe998]
007935ed faddp    st(1)
007935ef fild     dword ptr [ebp + 8]
007935f2 fmul     qword ptr [0xafe990]
007935f8 faddp    st(1)
007935fa fiadd    dword ptr [ebp + 0x14]
007935fd fmul     qword ptr [0xafe800]
00793603 push     dword ptr [ebx + 0x47c]
00793609 fsubr    qword ptr [ebp - 0xc]
0079360c add      ebx, 0x474
00793612 push     ebx
00793613 fstp     qword ptr [ebp - 0xc]
00793616 call     0x416563
0079361b test     eax, eax
0079361d pop      ecx
0079361e pop      ecx
0079361f je       0x79363d
00793621 mov      esi, dword ptr [ebp + 0x1c]
00793624 test     esi, esi
00793626 je       0x79363d
00793628 fld      qword ptr [ebp - 0xc]
0079362b push     ecx
0079362c push     ecx
0079362d mov      ecx, dword ptr [ebp - 4]
00793630 fstp     qword ptr [esp]
00793633 push     dword ptr [ebp + 0xc]
00793636 call     0x792fa8
0079363b mov      dword ptr [esi], eax
0079363d mov      eax, dword ptr [edi + 0xe0]
00793643 test     eax, eax
00793645 mov      dword ptr [ebp + 0xc], eax
00793648 je       0x793658
0079364a fild     dword ptr [ebp + 0xc]
0079364d fmul     qword ptr [ebp - 0xc]
00793650 fmul     qword ptr [0xaf14f0]
00793656 jmp      0x79365b
00793658 fld      qword ptr [ebp - 0xc]
0079365b fld1     
0079365d fcomp    st(1)
0079365f fnstsw   ax
00793661 sahf     
00793662 jb       0x793668
00793664 fstp     st(0)
00793666 fld1     
00793668 fcom     qword ptr [0xafe8a0]
0079366e fnstsw   ax
00793670 sahf     
00793671 jb       0x79367b
00793673 fstp     st(0)
00793675 fld      qword ptr [0xafe8a0]
0079367b call     0xa62018
00793680 pop      edi
00793681 pop      esi
00793682 pop      ebx
00793683 leave    
00793684 ret      0x18
