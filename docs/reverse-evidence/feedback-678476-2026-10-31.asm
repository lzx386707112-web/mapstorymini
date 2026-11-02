00678476 push     ebp
00678477 mov      ebp, esp
00678479 sub      esp, 0x28
0067847c push     ebx
0067847d mov      ebx, dword ptr [ecx + 0x28]
00678480 push     esi
00678481 push     edi
00678482 xor      edi, edi
00678484 cmp      ebx, edi
00678486 mov      dword ptr [ebp - 4], edi
00678489 mov      dword ptr [ebp - 8], edi
0067848c je       0x6785f6
00678492 mov      eax, dword ptr [ebp + 0xc]
00678495 mov      dword ptr [ebp + 0xc], eax
00678498 mov      eax, dword ptr [ebp - 4]
0067849b cmp      eax, dword ptr [ebp + 0x10]
0067849e jge      0x6785f6
006784a4 mov      ecx, ebx
006784a6 mov      esi, dword ptr [ecx + 4]
006784a9 push     dword ptr [esi + 0x12c]
006784af mov      eax, ebx
006784b1 add      ebx, -0x10
006784b4 neg      eax
006784b6 sbb      eax, eax
006784b8 and      eax, ebx
006784ba mov      eax, dword ptr [eax + 4]
006784bd mov      edx, eax
006784bf add      eax, 0x10
006784c2 neg      edx
006784c4 sbb      edx, edx
006784c6 and      edx, eax
006784c8 lea      eax, [esi + 0x124]
006784ce push     eax
006784cf mov      ebx, edx
006784d1 call     0x508a95
006784d6 test     eax, eax
006784d8 pop      ecx
006784d9 pop      ecx
006784da je       0x6785ee
006784e0 push     dword ptr [esi + 0x458]
006784e6 lea      eax, [esi + 0x450]
006784ec push     eax
006784ed call     0x508a95
006784f2 test     eax, eax
006784f4 pop      ecx
006784f5 pop      ecx
006784f6 jne      0x6785ee
006784fc cmp      dword ptr [ebp + 0x14], edi
006784ff je       0x678536
00678501 push     dword ptr [esi + 0x184]
00678507 lea      eax, [esi + 0x17c]
0067850d push     eax
0067850e call     0x4e8152
00678513 mov      edi, eax
00678515 mov      eax, dword ptr [ebp + 0x14]
00678518 push     dword ptr [eax + 0x184]
0067851e add      eax, 0x17c
00678523 push     eax
00678524 call     0x4e8152
00678529 add      esp, 0x10
0067852c cmp      edi, eax
0067852e je       0x6785ec
00678534 xor      edi, edi
00678536 cmp      dword ptr [ebp + 0x18], edi
00678539 je       0x678558
0067853b push     dword ptr [esi + 0x184]
00678541 lea      eax, [esi + 0x17c]
00678547 push     eax
00678548 call     0x4e8152
0067854d cmp      eax, dword ptr [ebp + 0x18]
00678550 pop      ecx
00678551 pop      ecx
00678552 jne      0x6785ee
00678558 cmp      dword ptr [ebp + 0x20], edi
0067855b je       0x678576
0067855d mov      eax, dword ptr [esi + 0x188]
00678563 push     dword ptr [eax + 0x14]
00678566 add      eax, 0xc
00678569 push     eax
0067856a call     0x4e8152
0067856f cmp      dword ptr [ebp + 0x20], eax
00678572 pop      ecx
00678573 pop      ecx
00678574 jne      0x6785ee
00678576 mov      eax, dword ptr [esi + 0x188]
0067857c cmp      dword ptr [eax + 0x208], edi
00678582 jne      0x6785ee
00678584 mov      ecx, esi
00678586 call     0x670c06
0067858b test     eax, eax
0067858d jne      0x6785ee
0067858f cmp      dword ptr [ebp + 0x24], edi
00678592 jne      0x67859c
00678594 cmp      dword ptr [esi + 0x328], edi
0067859a jne      0x6785ee
0067859c push     1
0067859e lea      eax, [ebp - 0x18]
006785a1 push     eax
006785a2 mov      ecx, esi
006785a4 call     0x664559
006785a9 lea      eax, [ebp - 0x18]
006785ac push     eax
006785ad call     dword ptr [0xbf04ac]
006785b3 test     eax, eax
006785b5 jne      0x6785ee
006785b7 push     dword ptr [ebp + 8]
006785ba lea      eax, [ebp - 0x18]
006785bd push     eax
006785be lea      eax, [ebp - 0x28]
006785c1 push     eax
006785c2 call     dword ptr [0xbf04a8]
006785c8 test     eax, eax
006785ca je       0x6785ee
006785cc mov      eax, dword ptr [ebp + 0xc]
006785cf inc      dword ptr [ebp - 4]
006785d2 add      dword ptr [ebp + 0xc], 4
006785d6 mov      dword ptr [eax], esi
006785d8 mov      eax, dword ptr [ebp + 0x1c]
006785db cmp      eax, edi
006785dd je       0x6785ee
006785df cmp      dword ptr [esi + 0x250], eax
006785e5 jne      0x6785ee
006785e7 inc      dword ptr [ebp - 8]
006785ea jmp      0x6785ee
006785ec xor      edi, edi
006785ee cmp      ebx, edi
006785f0 jne      0x678498
006785f6 mov      eax, dword ptr [ebp - 8]
006785f9 pop      edi
006785fa shl      eax, 0x10
006785fd or       eax, dword ptr [ebp - 4]
00678600 pop      esi
00678601 pop      ebx
00678602 leave    
00678603 ret      0x20
