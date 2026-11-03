SHA256 1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2
# function 0x7c618e: 865 instructions, 0x7c618e..0x7c6bf0
007c618e mov      eax, 0xab8fb0
007c6193 call     0xa60b98
007c6198 sub      esp, 0xc4
007c619e push     ebx
007c619f push     esi
007c61a0 push     edi
007c61a1 mov      esi, ecx
007c61a3 mov      edi, 0xbf6300
007c61a8 push     edi
007c61a9 lea      ecx, [ebp - 0x44]
007c61ac call     0x402f85
007c61b1 xor      ebx, ebx
007c61b3 push     edi
007c61b4 lea      ecx, [ebp - 0x54]
007c61b7 mov      dword ptr [ebp - 4], ebx
007c61ba call     0x402f85
007c61bf push     ebx
007c61c0 push     ebx
007c61c1 lea      eax, [ebp - 0x44]
007c61c4 push     eax
007c61c5 lea      eax, [ebp - 0x54]
007c61c8 push     eax
007c61c9 push     ecx
007c61ca mov      eax, esp
007c61cc mov      dword ptr [ebp - 0x14], esp
007c61cf push     0xb26
007c61d4 push     eax
007c61d5 mov      byte ptr [ebp - 4], 1
007c61d9 call     0x79e805
007c61de mov      ecx, eax
007c61e0 call     0x406292
007c61e5 lea      eax, [ebp - 0x70]
007c61e8 push     eax
007c61e9 mov      byte ptr [ebp - 4], 1
007c61ed call     0x406472
007c61f2 mov      ecx, eax
007c61f4 call     0x402680
007c61f9 mov      ecx, eax
007c61fb call     0x403a93
007c6200 mov      ecx, eax
007c6202 mov      byte ptr [ebp - 4], 3
007c6206 call     0x4032b2
007c620b mov      dword ptr [ebp - 0x10], eax
007c620e lea      eax, [ebp - 0x10]
007c6211 lea      ecx, [esi + 0xe8]
007c6217 push     eax
007c6218 call     0x41e527
007c621d cmp      eax, ebx
007c621f jge      0x7c622e
007c6221 cmp      eax, 0x80004002
007c6226 je       0x7c622e
007c6228 push     eax
007c6229 call     0xa5fde4
007c622e lea      eax, [ebp - 0x70]
007c6231 push     eax
007c6232 mov      byte ptr [ebp - 4], 1
007c6236 call     0x40291d
007c623b cmp      eax, ebx
007c623d pop      ecx
007c623e jge      0x7c6246
007c6240 push     eax
007c6241 call     0xa5fde4
007c6246 and      byte ptr [ebp - 4], 0
007c624a lea      eax, [ebp - 0x54]
007c624d push     eax
007c624e call     0x40291d
007c6253 cmp      eax, ebx
007c6255 pop      ecx
007c6256 jge      0x7c625e
007c6258 push     eax
007c6259 call     0xa5fde4
007c625e or       dword ptr [ebp - 4], 0xffffffff
007c6262 lea      eax, [ebp - 0x44]
007c6265 push     eax
007c6266 call     0x40291d
007c626b cmp      eax, ebx
007c626d pop      ecx
007c626e jge      0x7c6276
007c6270 push     eax
007c6271 call     0xa5fde4
007c6276 push     edi
007c6277 lea      ecx, [ebp - 0x44]
007c627a call     0x402f85
007c627f push     edi
007c6280 lea      ecx, [ebp - 0x54]
007c6283 mov      dword ptr [ebp - 4], 4
007c628a call     0x402f85
007c628f push     ebx
007c6290 push     ebx
007c6291 lea      eax, [ebp - 0x44]
007c6294 push     eax
007c6295 lea      eax, [ebp - 0x54]
007c6298 push     eax
007c6299 push     ecx
007c629a mov      eax, esp
007c629c mov      dword ptr [ebp - 0x14], esp
007c629f push     0x71e
007c62a4 push     eax
007c62a5 mov      byte ptr [ebp - 4], 5
007c62a9 call     0x79e805
007c62ae mov      ecx, eax
007c62b0 call     0x406292
007c62b5 lea      eax, [ebp - 0x70]
007c62b8 push     eax
007c62b9 mov      byte ptr [ebp - 4], 5
007c62bd call     0x406472
007c62c2 mov      ecx, eax
007c62c4 call     0x402680
007c62c9 mov      ecx, eax
007c62cb call     0x403a93
007c62d0 mov      ecx, eax
007c62d2 mov      byte ptr [ebp - 4], 7
007c62d6 call     0x4032b2
007c62db mov      dword ptr [ebp - 0x10], eax
007c62de lea      eax, [ebp - 0x10]
007c62e1 lea      ecx, [esi + 0xec]
007c62e7 push     eax
007c62e8 call     0x41e527
007c62ed cmp      eax, ebx
007c62ef jge      0x7c62fe
007c62f1 cmp      eax, 0x80004002
007c62f6 je       0x7c62fe
007c62f8 push     eax
007c62f9 call     0xa5fde4
007c62fe lea      eax, [ebp - 0x70]
007c6301 push     eax
007c6302 mov      byte ptr [ebp - 4], 5
007c6306 call     0x40291d
007c630b cmp      eax, ebx
007c630d pop      ecx
007c630e jge      0x7c6316
007c6310 push     eax
007c6311 call     0xa5fde4
007c6316 lea      eax, [ebp - 0x54]
007c6319 push     eax
007c631a mov      byte ptr [ebp - 4], 4
007c631e call     0x40291d
007c6323 cmp      eax, ebx
007c6325 pop      ecx
007c6326 jge      0x7c632e
007c6328 push     eax
007c6329 call     0xa5fde4
007c632e or       dword ptr [ebp - 4], 0xffffffff
007c6332 lea      eax, [ebp - 0x44]
007c6335 push     eax
007c6336 call     0x40291d
007c633b cmp      eax, ebx
007c633d pop      ecx
007c633e jge      0x7c6346
007c6340 push     eax
007c6341 call     0xa5fde4
007c6346 mov      dword ptr [ebp - 0x18], ebx
007c6349 mov      dword ptr [ebp - 0x24], 1
007c6350 mov      dword ptr [ebp - 0x20], ebx
007c6353 mov      dword ptr [ebp - 0x1c], ebx
007c6356 lea      eax, [ebp - 0x10]
007c6359 push     0xb27
007c635e push     eax
007c635f mov      dword ptr [ebp - 4], 8
007c6366 call     0x79e805
007c636b mov      ecx, eax
007c636d call     0x406276
007c6372 push     eax
007c6373 lea      ecx, [ebp - 0x18]
007c6376 mov      byte ptr [ebp - 4], 9
007c637a call     0x41cf93
007c637f lea      ecx, [ebp - 0x10]
007c6382 mov      byte ptr [ebp - 4], 8
007c6386 call     0x40265e
007c638b mov      ebx, 0x5a4
007c6390 mov      edi, 0xbf0b00
007c6395 push     ebx
007c6396 mov      ecx, edi
007c6398 call     0x403065
007c639d mov      dword ptr [ebp - 0x14], eax
007c63a0 test     eax, eax
007c63a2 mov      byte ptr [ebp - 4], 0xa
007c63a6 je       0x7c63b1
007c63a8 mov      ecx, eax
007c63aa call     0x4258e4
007c63af jmp      0x7c63b3
007c63b1 xor      eax, eax
007c63b3 push     eax
007c63b4 lea      ecx, [esi + 0x80]
007c63ba mov      byte ptr [ebp - 4], 8
007c63be call     0x4284ff
007c63c3 mov      ecx, dword ptr [esi + 0x84]
007c63c9 mov      eax, dword ptr [ecx]
007c63cb lea      edx, [ebp - 0x24]
007c63ce push     edx
007c63cf push     0
007c63d1 push     0xf
007c63d3 push     0x9a
007c63d8 push     2
007c63da push     esi
007c63db call     dword ptr [eax + 0x20]
007c63de lea      eax, [ebp - 0x10]
007c63e1 push     0x71f
007c63e6 push     eax
007c63e7 call     0x79e805
007c63ec mov      ecx, eax
007c63ee call     0x406276
007c63f3 push     eax
007c63f4 lea      ecx, [ebp - 0x18]
007c63f7 mov      byte ptr [ebp - 4], 0xb
007c63fb call     0x41cf93
007c6400 lea      ecx, [ebp - 0x10]
007c6403 mov      byte ptr [ebp - 4], 8
007c6407 call     0x40265e
007c640c push     ebx
007c640d mov      ecx, edi
007c640f call     0x403065
007c6414 mov      dword ptr [ebp - 0x14], eax
007c6417 test     eax, eax
007c6419 mov      byte ptr [ebp - 4], 0xc
007c641d je       0x7c6428
007c641f mov      ecx, eax
007c6421 call     0x4258e4
007c6426 jmp      0x7c642a
007c6428 xor      eax, eax
007c642a lea      ecx, [esi + 0x88]
007c6430 push     eax
007c6431 mov      byte ptr [ebp - 4], 8
007c6435 call     0x4284ff
007c643a mov      ecx, dword ptr [esi + 0x8c]
007c6440 mov      eax, dword ptr [ecx]
007c6442 lea      edx, [ebp - 0x24]
007c6445 push     edx
007c6446 push     0
007c6448 push     0x23
007c644a push     0x9a
007c644f push     0x3e8
007c6454 push     esi
007c6455 call     dword ptr [eax + 0x20]
007c6458 lea      eax, [ebp - 0x10]
007c645b push     0x720
007c6460 push     eax
007c6461 call     0x79e805
007c6466 mov      ecx, eax
007c6468 call     0x406276
007c646d push     eax
007c646e lea      ecx, [ebp - 0x18]
007c6471 mov      byte ptr [ebp - 4], 0xd
007c6475 call     0x41cf93
007c647a lea      ecx, [ebp - 0x10]
007c647d mov      byte ptr [ebp - 4], 8
007c6481 call     0x40265e
007c6486 push     ebx
007c6487 mov      ecx, edi
007c6489 call     0x403065
007c648e mov      dword ptr [ebp - 0x14], eax
007c6491 test     eax, eax
007c6493 mov      byte ptr [ebp - 4], 0xe
007c6497 je       0x7c64a2
007c6499 mov      ecx, eax
007c649b call     0x4258e4
007c64a0 jmp      0x7c64a4
007c64a2 xor      eax, eax
007c64a4 push     eax
007c64a5 lea      ecx, [esi + 0x90]
007c64ab mov      byte ptr [ebp - 4], 8
007c64af call     0x4284ff
007c64b4 mov      ecx, dword ptr [esi + 0x94]
007c64ba mov      eax, dword ptr [ecx]
007c64bc lea      edx, [ebp - 0x24]
007c64bf push     edx
007c64c0 push     0
007c64c2 push     0xf
007c64c4 push     0x180
007c64c9 push     0x3e9
007c64ce push     esi
007c64cf call     dword ptr [eax + 0x20]
007c64d2 lea      eax, [ebp - 0x10]
007c64d5 push     0x721
007c64da push     eax
007c64db call     0x79e805
007c64e0 mov      ecx, eax
007c64e2 call     0x406276
007c64e7 push     eax
007c64e8 lea      ecx, [ebp - 0x18]
007c64eb mov      byte ptr [ebp - 4], 0xf
007c64ef call     0x41cf93
007c64f4 lea      ecx, [ebp - 0x10]
007c64f7 mov      byte ptr [ebp - 4], 8
007c64fb call     0x40265e
007c6500 push     ebx
007c6501 mov      ecx, edi
007c6503 call     0x403065
007c6508 mov      dword ptr [ebp - 0x14], eax
007c650b test     eax, eax
007c650d mov      byte ptr [ebp - 4], 0x10
007c6511 je       0x7c651c
007c6513 mov      ecx, eax
007c6515 call     0x4258e4
007c651a jmp      0x7c651e
007c651c xor      eax, eax
007c651e push     eax
007c651f lea      ecx, [esi + 0x88]
007c6525 mov      byte ptr [ebp - 4], 8
007c6529 call     0x4284ff
007c652e mov      ecx, dword ptr [esi + 0x8c]
007c6534 mov      eax, dword ptr [ecx]
007c6536 lea      edx, [ebp - 0x24]
007c6539 push     edx
007c653a push     0
007c653c push     0x37
007c653e push     0x9a
007c6543 push     0x3ea
007c6548 push     esi
007c6549 call     dword ptr [eax + 0x20]
007c654c lea      eax, [ebp - 0x10]
007c654f push     0xb28
007c6554 push     eax
007c6555 call     0x79e805
007c655a mov      ecx, eax
007c655c call     0x406276
007c6561 push     eax
007c6562 lea      ecx, [ebp - 0x18]
007c6565 mov      byte ptr [ebp - 4], 0x11
007c6569 call     0x41cf93
007c656e lea      ecx, [ebp - 0x10]
007c6571 mov      byte ptr [ebp - 4], 8
007c6575 call     0x40265e
007c657a push     ebx
007c657b mov      ecx, edi
007c657d call     0x403065
007c6582 mov      dword ptr [ebp - 0x14], eax
007c6585 test     eax, eax
007c6587 mov      byte ptr [ebp - 4], 0x12
007c658b je       0x7c6596
007c658d mov      ecx, eax
007c658f call     0x4258e4
007c6594 jmp      0x7c6598
007c6596 xor      eax, eax
007c6598 push     eax
007c6599 lea      ecx, [esi + 0xa0]
007c659f mov      byte ptr [ebp - 4], 8
007c65a3 call     0x4284ff
007c65a8 mov      ecx, dword ptr [esi + 0xa4]
007c65ae mov      eax, dword ptr [ecx]
007c65b0 lea      edx, [ebp - 0x24]
007c65b3 push     edx
007c65b4 push     0
007c65b6 push     0x126
007c65bb push     5
007c65bd push     0x3eb
007c65c2 push     esi
007c65c3 call     dword ptr [eax + 0x20]
007c65c6 lea      eax, [ebp - 0x10]
007c65c9 push     0xb29
007c65ce push     eax
007c65cf call     0x79e805
007c65d4 mov      ecx, eax
007c65d6 call     0x406276
007c65db push     eax
007c65dc lea      ecx, [ebp - 0x18]
007c65df mov      byte ptr [ebp - 4], 0x13
007c65e3 call     0x41cf93
007c65e8 lea      ecx, [ebp - 0x10]
007c65eb mov      byte ptr [ebp - 4], 8
007c65ef call     0x40265e
007c65f4 push     ebx
007c65f5 mov      ecx, edi
007c65f7 call     0x403065
007c65fc mov      dword ptr [ebp - 0x14], eax
007c65ff xor      ebx, ebx
007c6601 cmp      eax, ebx
007c6603 mov      byte ptr [ebp - 4], 0x14
007c6607 je       0x7c6612
007c6609 mov      ecx, eax
007c660b call     0x4258e4
007c6610 jmp      0x7c6614
007c6612 xor      eax, eax
007c6614 push     eax
007c6615 lea      ecx, [esi + 0xa8]
007c661b mov      byte ptr [ebp - 4], 8
007c661f call     0x4284ff
007c6624 mov      ecx, dword ptr [esi + 0xac]
007c662a mov      eax, dword ptr [ecx]
007c662c lea      edx, [ebp - 0x24]
007c662f push     edx
007c6630 push     ebx
007c6631 push     0x126
007c6636 push     0xec
007c663b push     0x3ec
007c6640 push     esi
007c6641 call     dword ptr [eax + 0x20]
007c6644 push     0x68
007c6646 mov      ecx, edi
007c6648 call     0x403065
007c664d mov      dword ptr [ebp - 0x14], eax
007c6650 cmp      eax, ebx
007c6652 mov      byte ptr [ebp - 4], 0x15
007c6656 je       0x7c6661
007c6658 mov      ecx, eax
007c665a call     0x4dd4ff
007c665f jmp      0x7c6663
007c6661 xor      eax, eax
007c6663 lea      ecx, [esi + 0xb0]
007c6669 push     eax
007c666a mov      byte ptr [ebp - 4], 8
007c666e call     0x42854d
007c6673 mov      ecx, dword ptr [esi + 0xb4]
007c6679 mov      eax, dword ptr [ecx]
007c667b push     ebx
007c667c push     0xde
007c6681 push     0x5c
007c6683 push     0xeb
007c6688 push     2
007c668a push     0x3ed
007c668f push     esi
007c6690 call     dword ptr [eax + 8]
007c6693 push     0xbf6300
007c6698 lea      ecx, [ebp - 0x70]
007c669b call     0x402f85
007c66a0 push     0xbf6300
007c66a5 lea      ecx, [ebp - 0xa0]
007c66ab mov      byte ptr [ebp - 4], 0x16
007c66af call     0x402f85
007c66b4 push     ebx
007c66b5 push     ebx
007c66b6 lea      eax, [ebp - 0x70]
007c66b9 push     eax
007c66ba lea      eax, [ebp - 0xa0]
007c66c0 push     eax
007c66c1 push     ecx
007c66c2 mov      eax, esp
007c66c4 mov      dword ptr [ebp - 0x14], esp
007c66c7 push     0xb2b
007c66cc push     eax
007c66cd mov      byte ptr [ebp - 4], 0x17
007c66d1 call     0x79e805
007c66d6 mov      ecx, eax
007c66d8 call     0x406292
007c66dd lea      eax, [ebp - 0xb0]
007c66e3 push     eax
007c66e4 mov      byte ptr [ebp - 4], 0x17
007c66e8 call     0x406472
007c66ed mov      ecx, eax
007c66ef call     0x402680
007c66f4 mov      ecx, eax
007c66f6 call     0x403a93
007c66fb mov      ecx, eax
007c66fd mov      byte ptr [ebp - 4], 0x19
007c6701 call     0x4032b2
007c6706 cmp      eax, ebx
007c6708 mov      dword ptr [ebp - 0x58], eax
007c670b mov      dword ptr [ebp - 0x30], ebx
007c670e je       0x7c672d
007c6710 lea      eax, [ebp - 0x58]
007c6713 push     eax
007c6714 lea      ecx, [ebp - 0x30]
007c6717 call     0x4052ad
007c671c cmp      eax, ebx
007c671e jge      0x7c672d
007c6720 cmp      eax, 0x80004002
007c6725 je       0x7c672d
007c6727 push     eax
007c6728 call     0xa5fde4
007c672d lea      eax, [ebp - 0xb0]
007c6733 push     eax
007c6734 mov      byte ptr [ebp - 4], 0x1d
007c6738 call     0x40291d
007c673d cmp      eax, ebx
007c673f pop      ecx
007c6740 jge      0x7c6748
007c6742 push     eax
007c6743 call     0xa5fde4
007c6748 lea      eax, [ebp - 0xa0]
007c674e push     eax
007c674f mov      byte ptr [ebp - 4], 0x1c
007c6753 call     0x40291d
007c6758 cmp      eax, ebx
007c675a pop      ecx
007c675b jge      0x7c6763
007c675d push     eax
007c675e call     0xa5fde4
007c6763 lea      eax, [ebp - 0x70]
007c6766 push     eax
007c6767 mov      byte ptr [ebp - 4], 0x1b
007c676b call     0x40291d
007c6770 cmp      eax, ebx
007c6772 pop      ecx
007c6773 jge      0x7c677b
007c6775 push     eax
007c6776 call     0xa5fde4
007c677b lea      eax, [ebp - 0x54]
007c677e push     eax
007c677f call     dword ptr [0xaf0268]
007c6785 lea      eax, [ebp - 0x54]
007c6788 push     0xbf6300
007c678d push     eax
007c678e call     0x402b8c
007c6793 cmp      eax, ebx
007c6795 pop      ecx
007c6796 pop      ecx
007c6797 jge      0x7c679f
007c6799 push     eax
007c679a call     0xa5fde4
007c679f lea      eax, [ebp - 0x44]
007c67a2 push     eax
007c67a3 mov      byte ptr [ebp - 4], 0x1e
007c67a7 call     dword ptr [0xaf0268]
007c67ad lea      eax, [ebp - 0x44]
007c67b0 push     0xbf6300
007c67b5 push     eax
007c67b6 call     0x402b8c
007c67bb cmp      eax, ebx
007c67bd pop      ecx
007c67be pop      ecx
007c67bf jge      0x7c67c7
007c67c1 push     eax
007c67c2 call     0xa5fde4
007c67c7 push     ebx
007c67c8 push     ebx
007c67c9 lea      eax, [ebp - 0x54]
007c67cc push     eax
007c67cd lea      eax, [ebp - 0x44]
007c67d0 push     eax
007c67d1 push     ecx
007c67d2 mov      eax, esp
007c67d4 mov      dword ptr [ebp - 0x14], esp
007c67d7 push     0xb2c
007c67dc push     eax
007c67dd mov      byte ptr [ebp - 4], 0x1f
007c67e1 call     0x79e805
007c67e6 mov      ecx, eax
007c67e8 call     0x406292
007c67ed lea      eax, [ebp - 0xc0]
007c67f3 push     eax
007c67f4 mov      byte ptr [ebp - 4], 0x1f
007c67f8 call     0x406472
007c67fd mov      ecx, eax
007c67ff call     0x402680
007c6804 mov      ecx, eax
007c6806 call     0x403a93
007c680b mov      ecx, eax
007c680d mov      byte ptr [ebp - 4], 0x21
007c6811 call     0x4032b2
007c6816 cmp      eax, ebx
007c6818 mov      dword ptr [ebp - 0x60], eax
007c681b mov      dword ptr [ebp - 0x28], ebx
007c681e je       0x7c683d
007c6820 lea      eax, [ebp - 0x60]
007c6823 push     eax
007c6824 lea      ecx, [ebp - 0x28]
007c6827 call     0x4052ad
007c682c cmp      eax, ebx
007c682e jge      0x7c683d
007c6830 cmp      eax, 0x80004002
007c6835 je       0x7c683d
007c6837 push     eax
007c6838 call     0xa5fde4
007c683d lea      eax, [ebp - 0xc0]
007c6843 push     eax
007c6844 mov      byte ptr [ebp - 4], 0x25
007c6848 call     0x40291d
007c684d cmp      eax, ebx
007c684f pop      ecx
007c6850 jge      0x7c6858
007c6852 push     eax
007c6853 call     0xa5fde4
007c6858 lea      eax, [ebp - 0x44]
007c685b push     eax
007c685c mov      byte ptr [ebp - 4], 0x24
007c6860 call     0x40291d
007c6865 cmp      eax, ebx
007c6867 pop      ecx
007c6868 jge      0x7c6870
007c686a push     eax
007c686b call     0xa5fde4
007c6870 lea      eax, [ebp - 0x54]
007c6873 push     eax
007c6874 mov      byte ptr [ebp - 4], 0x23
007c6878 call     0x40291d
007c687d cmp      eax, ebx
007c687f pop      ecx
007c6880 jge      0x7c6888
007c6882 push     eax
007c6883 call     0xa5fde4
007c6888 mov      dword ptr [ebp - 0x10], ebx
007c688b push     0x20
007c688d pop      eax
007c688e call     0xa61190
007c6893 mov      eax, esp
007c6895 push     ebx
007c6896 push     ebx
007c6897 push     eax
007c6898 push     dword ptr [ebp - 0x10]
007c689b call     0x4286a2
007c68a0 pop      ecx
007c68a1 mov      ecx, esp
007c68a3 mov      dword ptr [ebp - 0x34], esp
007c68a6 push     eax
007c68a7 call     0x425add
007c68ac lea      eax, [ebp - 0x90]
007c68b2 push     eax
007c68b3 lea      ecx, [ebp - 0x28]
007c68b6 mov      byte ptr [ebp - 4], 0x23
007c68ba call     0x40263b
007c68bf mov      ecx, eax
007c68c1 call     0x403935
007c68c6 mov      ecx, eax
007c68c8 mov      byte ptr [ebp - 4], 0x27
007c68cc call     0x4032b2
007c68d1 push     0x20
007c68d3 mov      dword ptr [ebp - 0x5c], eax
007c68d6 pop      eax
007c68d7 call     0xa61190
007c68dc mov      eax, esp
007c68de push     ebx
007c68df push     ebx
007c68e0 push     eax
007c68e1 push     dword ptr [ebp - 0x10]
007c68e4 call     0x4286a2
007c68e9 pop      ecx
007c68ea mov      ecx, esp
007c68ec mov      dword ptr [ebp - 0x34], esp
007c68ef push     eax
007c68f0 call     0x425add
007c68f5 lea      eax, [ebp - 0x80]
007c68f8 push     eax
007c68f9 lea      ecx, [ebp - 0x30]
007c68fc mov      byte ptr [ebp - 4], 0x27
007c6900 call     0x40263b
007c6905 mov      ecx, eax
007c6907 call     0x403935
007c690c mov      ecx, eax
007c690e mov      byte ptr [ebp - 4], 0x29
007c6912 call     0x4032b2
007c6917 push     1
007c6919 push     ecx
007c691a mov      dword ptr [ebp - 0x14], eax
007c691d lea      eax, [ebp - 0x5c]
007c6920 mov      ecx, esp
007c6922 mov      dword ptr [ebp - 0x34], esp
007c6925 push     eax
007c6926 call     0x428dcc
007c692b push     ecx
007c692c lea      eax, [ebp - 0x14]
007c692f mov      ecx, esp
007c6931 mov      dword ptr [ebp - 0x2c], esp
007c6934 push     eax
007c6935 mov      byte ptr [ebp - 4], 0x2a
007c6939 call     0x428dcc
007c693e lea      ecx, [esi + 0xb0]
007c6944 mov      byte ptr [ebp - 4], 0x29
007c6948 call     0x428549
007c694d mov      ecx, eax
007c694f call     0x4de0e2
007c6954 lea      eax, [ebp - 0x80]
007c6957 push     eax
007c6958 mov      byte ptr [ebp - 4], 0x27
007c695c call     0x40291d
007c6961 pop      ecx
007c6962 cmp      eax, ebx
007c6964 jge      0x7c696c
007c6966 push     eax
007c6967 call     0xa5fde4
007c696c lea      eax, [ebp - 0x90]
007c6972 push     eax
007c6973 mov      byte ptr [ebp - 4], 0x23
007c6977 call     0x40291d
007c697c cmp      eax, ebx
007c697e pop      ecx
007c697f jge      0x7c6987
007c6981 push     eax
007c6982 call     0xa5fde4
007c6987 inc      dword ptr [ebp - 0x10]
007c698a cmp      dword ptr [ebp - 0x10], 5
007c698e jl       0x7c688b
007c6994 mov      eax, dword ptr [esi + 0xb4]
007c699a push     0x74
007c699c mov      ecx, edi
007c699e mov      dword ptr [eax + 0x34], 1
007c69a5 mov      dword ptr [eax + 0x38], ebx
007c69a8 call     0x403065
007c69ad mov      dword ptr [ebp - 0x2c], eax
007c69b0 cmp      eax, ebx
007c69b2 mov      byte ptr [ebp - 4], 0x2c
007c69b6 je       0x7c69c1
007c69b8 mov      ecx, eax
007c69ba call     0x4d6cd3
007c69bf jmp      0x7c69c3
007c69c1 xor      eax, eax
007c69c3 push     eax
007c69c4 lea      ecx, [esi + 0xb8]
007c69ca mov      byte ptr [ebp - 4], 0x23
007c69ce call     0x428597
007c69d3 mov      ecx, dword ptr [esi + 0xbc]
007c69d9 mov      eax, dword ptr [ecx]
007c69db push     ebx
007c69dc push     0xc5
007c69e1 push     0x58
007c69e3 push     0xd2
007c69e8 push     3
007c69ea push     1
007c69ec push     0x3ee
007c69f1 push     esi
007c69f2 call     dword ptr [eax + 0x20]
007c69f5 mov      eax, dword ptr [esi + 0xbc]
007c69fb push     0x74
007c69fd mov      ecx, edi
007c69ff mov      dword ptr [eax + 0x34], 0xb4
007c6a06 call     0x403065
007c6a0b mov      dword ptr [ebp - 0x2c], eax
007c6a0e cmp      eax, ebx
007c6a10 mov      byte ptr [ebp - 4], 0x2d
007c6a14 je       0x7c6a1f
007c6a16 mov      ecx, eax
007c6a18 call     0x4d6cd3
007c6a1d jmp      0x7c6a21
007c6a1f xor      eax, eax
007c6a21 push     eax
007c6a22 lea      ecx, [esi + 0xc0]
007c6a28 mov      byte ptr [ebp - 4], 0x23
007c6a2c call     0x428597
007c6a31 mov      ecx, dword ptr [esi + 0xc4]
007c6a37 mov      eax, dword ptr [ecx]
007c6a39 push     ebx
007c6a3a push     0x9e
007c6a3f push     0x7f
007c6a41 push     0x1b9
007c6a46 push     3
007c6a48 push     1
007c6a4a push     0x3ef
007c6a4f push     esi
007c6a50 call     dword ptr [eax + 0x20]
007c6a53 mov      eax, dword ptr [esi + 0xc4]
007c6a59 mov      dword ptr [eax + 0x34], 0x92
007c6a60 lea      eax, [ebp - 0x14]
007c6a63 push     0x12
007c6a65 push     eax
007c6a66 call     0x98a707
007c6a6b pop      ecx
007c6a6c pop      ecx
007c6a6d push     dword ptr [eax]
007c6a6f lea      ecx, [esi + 0xe0]
007c6a75 call     0x4288d7
007c6a7a mov      eax, dword ptr [ebp - 0x14]
007c6a7d cmp      eax, ebx
007c6a7f je       0x7c6a87
007c6a81 mov      ecx, dword ptr [eax]
007c6a83 push     eax
007c6a84 call     dword ptr [ecx + 8]
007c6a87 mov      edi, dword ptr [0xaf0268]
007c6a8d lea      eax, [ebp - 0x80]
007c6a90 push     eax
007c6a91 call     edi
007c6a93 lea      eax, [ebp - 0x80]
007c6a96 push     0xbf6300
007c6a9b push     eax
007c6a9c call     0x402b8c
007c6aa1 cmp      eax, ebx
007c6aa3 pop      ecx
007c6aa4 pop      ecx
007c6aa5 jge      0x7c6aad
007c6aa7 push     eax
007c6aa8 call     0xa5fde4
007c6aad lea      eax, [ebp - 0x90]
007c6ab3 push     eax
007c6ab4 mov      byte ptr [ebp - 4], 0x2e
007c6ab8 call     edi
007c6aba lea      eax, [ebp - 0x90]
007c6ac0 push     0xbf6300
007c6ac5 push     eax
007c6ac6 call     0x402b8c
007c6acb cmp      eax, ebx
007c6acd pop      ecx
007c6ace pop      ecx
007c6acf jge      0x7c6ad7
007c6ad1 push     eax
007c6ad2 call     0xa5fde4
007c6ad7 push     ebx
007c6ad8 push     ebx
007c6ad9 lea      eax, [ebp - 0x80]
007c6adc push     eax
007c6add lea      eax, [ebp - 0x90]
007c6ae3 push     eax
007c6ae4 push     ecx
007c6ae5 mov      eax, esp
007c6ae7 mov      dword ptr [ebp - 0x2c], esp
007c6aea push     0x4e5
007c6aef push     eax
007c6af0 mov      byte ptr [ebp - 4], 0x2f
007c6af4 call     0x79e805
007c6af9 mov      ecx, eax
007c6afb call     0x406292
007c6b00 lea      eax, [ebp - 0xd0]
007c6b06 push     eax
007c6b07 mov      byte ptr [ebp - 4], 0x2f
007c6b0b call     0x406472
007c6b10 mov      ecx, eax
007c6b12 call     0x402680
007c6b17 mov      ecx, eax
007c6b19 call     0x403a93
007c6b1e mov      ecx, eax
007c6b20 mov      byte ptr [ebp - 4], 0x31
007c6b24 call     0x4032b2
007c6b29 mov      dword ptr [ebp - 0x14], eax
007c6b2c lea      eax, [ebp - 0x14]
007c6b2f lea      ecx, [esi + 0xe4]
007c6b35 push     eax
007c6b36 call     0x4052ad
007c6b3b cmp      eax, ebx
007c6b3d jge      0x7c6b4c
007c6b3f cmp      eax, 0x80004002
007c6b44 je       0x7c6b4c
007c6b46 push     eax
007c6b47 call     0xa5fde4
007c6b4c lea      eax, [ebp - 0xd0]
007c6b52 push     eax
007c6b53 mov      byte ptr [ebp - 4], 0x2f
007c6b57 call     0x40291d
007c6b5c cmp      eax, ebx
007c6b5e pop      ecx
007c6b5f jge      0x7c6b67
007c6b61 push     eax
007c6b62 call     0xa5fde4
007c6b67 lea      eax, [ebp - 0x90]
007c6b6d push     eax
007c6b6e mov      byte ptr [ebp - 4], 0x2e
007c6b72 call     0x40291d
007c6b77 cmp      eax, ebx
007c6b79 pop      ecx
007c6b7a jge      0x7c6b82
007c6b7c push     eax
007c6b7d call     0xa5fde4
007c6b82 lea      eax, [ebp - 0x80]
007c6b85 push     eax
007c6b86 mov      byte ptr [ebp - 4], 0x23
007c6b8a call     0x40291d
007c6b8f cmp      eax, ebx
007c6b91 pop      ecx
007c6b92 jge      0x7c6b9a
007c6b94 push     eax
007c6b95 call     0xa5fde4
007c6b9a mov      eax, dword ptr [ebp - 0x28]
007c6b9d or       dword ptr [esi + 0xf4], 0xffffffff
007c6ba4 or       dword ptr [esi + 0xf0], 0xffffffff
007c6bab cmp      eax, ebx
007c6bad mov      dword ptr [esi + 0x104], ebx
007c6bb3 mov      byte ptr [ebp - 4], 0x1b
007c6bb7 je       0x7c6bbf
007c6bb9 mov      ecx, dword ptr [eax]
007c6bbb push     eax
007c6bbc call     dword ptr [ecx + 8]
007c6bbf mov      eax, dword ptr [ebp - 0x30]
007c6bc2 cmp      eax, ebx
007c6bc4 mov      byte ptr [ebp - 4], 8
007c6bc8 je       0x7c6bd0
007c6bca mov      ecx, dword ptr [eax]
007c6bcc push     eax
007c6bcd call     dword ptr [ecx + 8]
007c6bd0 or       dword ptr [ebp - 4], 0xffffffff
007c6bd4 lea      ecx, [ebp - 0x18]
007c6bd7 call     0x40265e
007c6bdc mov      ecx, dword ptr [ebp - 0xc]
007c6bdf lea      esp, [ebp - 0xdc]
007c6be5 pop      edi
007c6be6 pop      esi
007c6be7 mov      dword ptr fs:[0], ecx
007c6bee pop      ebx
007c6bef leave    
007c6bf0 ret      4
