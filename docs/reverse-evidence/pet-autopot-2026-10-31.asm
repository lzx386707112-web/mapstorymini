SHA256 1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2
# function 0x7ff0d6: 246 instructions, 0x7ff0d6..0x7ff3b2
007ff0d6 mov      eax, 0xabf87a
007ff0db call     0xa60b98
007ff0e0 sub      esp, 0x48
007ff0e3 push     ebx
007ff0e4 push     esi
007ff0e5 push     edi
007ff0e6 mov      edi, dword ptr [ebp + 8]
007ff0e9 xor      esi, esi
007ff0eb cmp      edi, esi
007ff0ed mov      dword ptr [ebp - 0x14], ecx
007ff0f0 jne      0x7ff10c
007ff0f2 mov      ecx, dword ptr [0xbed5a0]
007ff0f8 push     esi
007ff0f9 call     0x58e03c
007ff0fe mov      ecx, dword ptr [ebp - 0x14]
007ff101 push     esi
007ff102 call     0x9e04c9
007ff107 jmp      0x7ff3a4
007ff10c mov      eax, dword ptr [0xbebf98]
007ff111 cmp      eax, esi
007ff113 je       0x7ff3a4
007ff119 mov      eax, dword ptr [eax + 0x1edc]
007ff11f mov      eax, dword ptr [eax + 4]
007ff122 cmp      eax, esi
007ff124 je       0x7ff3a4
007ff12a push     dword ptr [eax + 0x16c]
007ff130 add      eax, 0x164
007ff135 push     eax
007ff136 call     0x508a95
007ff13b cmp      eax, esi
007ff13d pop      ecx
007ff13e pop      ecx
007ff13f je       0x7ff3a4
007ff145 mov      ecx, dword ptr [0xbe78d8]
007ff14b push     edi
007ff14c lea      eax, [ebp - 0x10]
007ff14f push     eax
007ff150 call     0x5d3322
007ff155 cmp      dword ptr [ebp - 0x10], esi
007ff158 mov      dword ptr [ebp - 4], esi
007ff15b jne      0x7ff16a
007ff15d lea      ecx, [ebp - 0x10]
007ff160 call     0x4027e9
007ff165 jmp      0x7ff3a4
007ff16a push     esi
007ff16b push     esi
007ff16c push     ecx
007ff16d mov      eax, esp
007ff16f mov      dword ptr [ebp - 0x1c], esp
007ff172 push     0x902
007ff177 push     eax
007ff178 call     0x79e805
007ff17d mov      ecx, eax
007ff17f call     0x406292
007ff184 and      byte ptr [ebp - 4], 0
007ff188 lea      eax, [ebp - 0x34]
007ff18b push     eax
007ff18c lea      ecx, [ebp - 0x10]
007ff18f call     0x40263b
007ff194 mov      ecx, eax
007ff196 call     0x403935
007ff19b mov      ecx, eax
007ff19d mov      byte ptr [ebp - 4], 2
007ff1a1 call     0x4032b2
007ff1a6 cmp      eax, esi
007ff1a8 mov      dword ptr [ebp - 0x18], eax
007ff1ab mov      dword ptr [ebp + 8], esi
007ff1ae je       0x7ff1cd
007ff1b0 lea      eax, [ebp - 0x18]
007ff1b3 push     eax
007ff1b4 lea      ecx, [ebp + 8]
007ff1b7 call     0x4052ad
007ff1bc cmp      eax, esi
007ff1be jge      0x7ff1cd
007ff1c0 cmp      eax, 0x80004002
007ff1c5 je       0x7ff1cd
007ff1c7 push     eax
007ff1c8 call     0xa5fde4
007ff1cd lea      eax, [ebp - 0x34]
007ff1d0 push     eax
007ff1d1 mov      byte ptr [ebp - 4], 4
007ff1d5 call     0x40291d
007ff1da cmp      eax, esi
007ff1dc pop      ecx
007ff1dd jge      0x7ff1e5
007ff1df push     eax
007ff1e0 call     0xa5fde4
007ff1e5 cmp      dword ptr [ebp + 8], esi
007ff1e8 jne      0x7ff1f7
007ff1ea lea      ecx, [ebp + 8]
007ff1ed call     0x4027e9
007ff1f2 jmp      0x7ff15d
007ff1f7 push     esi
007ff1f8 push     ecx
007ff1f9 mov      ecx, esp
007ff1fb mov      dword ptr [ebp - 0x1c], esp
007ff1fe push     0xaf8488
007ff203 call     0x403382
007ff208 lea      eax, [ebp - 0x54]
007ff20b push     eax
007ff20c lea      ecx, [ebp + 8]
007ff20f mov      byte ptr [ebp - 4], 4
007ff213 call     0x40263b
007ff218 mov      ecx, eax
007ff21a call     0x403935
007ff21f push     eax
007ff220 mov      byte ptr [ebp - 4], 6
007ff224 call     0x414d40
007ff229 test     eax, eax
007ff22b pop      ecx
007ff22c pop      ecx
007ff22d jne      0x7ff28b
007ff22f push     esi
007ff230 push     ecx
007ff231 mov      ecx, esp
007ff233 mov      dword ptr [ebp - 0x1c], esp
007ff236 push     0xb38aa4
007ff23b call     0x403382
007ff240 lea      eax, [ebp - 0x44]
007ff243 push     eax
007ff244 lea      ecx, [ebp + 8]
007ff247 mov      byte ptr [ebp - 4], 6
007ff24b call     0x40263b
007ff250 mov      ecx, eax
007ff252 call     0x403935
007ff257 push     eax
007ff258 mov      byte ptr [ebp - 4], 8
007ff25c call     0x414d40
007ff261 mov      ebx, eax
007ff263 neg      ebx
007ff265 lea      eax, [ebp - 0x44]
007ff268 sbb      bl, bl
007ff26a push     eax
007ff26b inc      bl
007ff26d mov      byte ptr [ebp - 4], 6
007ff271 call     0x40291d
007ff276 add      esp, 0xc
007ff279 cmp      eax, esi
007ff27b jge      0x7ff283
007ff27d push     eax
007ff27e call     0xa5fde4
007ff283 test     bl, bl
007ff285 je       0x7ff28b
007ff287 mov      bl, 1
007ff289 jmp      0x7ff28d
007ff28b xor      bl, bl
007ff28d lea      eax, [ebp - 0x54]
007ff290 push     eax
007ff291 mov      byte ptr [ebp - 4], 4
007ff295 call     0x40291d
007ff29a cmp      eax, esi
007ff29c pop      ecx
007ff29d jge      0x7ff2a5
007ff29f push     eax
007ff2a0 call     0xa5fde4
007ff2a5 test     bl, bl
007ff2a7 je       0x7ff2d2
007ff2a9 push     esi
007ff2aa push     esi
007ff2ab push     esi
007ff2ac push     esi
007ff2ad push     ecx
007ff2ae mov      eax, esp
007ff2b0 mov      dword ptr [ebp - 0x1c], esp
007ff2b3 push     0xb1f
007ff2b8 push     eax
007ff2b9 call     0x79e805
007ff2be mov      ecx, eax
007ff2c0 call     0x406455
007ff2c5 call     0x9929dd
007ff2ca add      esp, 0x14
007ff2cd jmp      0x7ff382
007ff2d2 mov      ecx, dword ptr [0xbed5a0]
007ff2d8 cmp      dword ptr [ecx + 0x3c0], edi
007ff2de je       0x7ff382
007ff2e4 push     edi
007ff2e5 call     0x58e03c
007ff2ea mov      ecx, dword ptr [ebp - 0x14]
007ff2ed push     esi
007ff2ee call     0x9e04c9
007ff2f3 lea      eax, [ebp - 0x14]
007ff2f6 push     0xd6b
007ff2fb push     eax
007ff2fc call     0x79e805
007ff301 mov      ecx, eax
007ff303 call     0x406455
007ff308 push     0xc
007ff30a push     eax
007ff30b mov      byte ptr [ebp - 4], 9
007ff30f call     0x4906b5
007ff314 pop      ecx
007ff315 pop      ecx
007ff316 lea      ecx, [ebp - 0x14]
007ff319 mov      byte ptr [ebp - 4], 4
007ff31d call     0x4062df
007ff322 push     0x154
007ff327 mov      ecx, 0xbf0b00
007ff32c call     0x403065
007ff331 mov      dword ptr [ebp - 0x1c], eax
007ff334 cmp      eax, esi
007ff336 mov      byte ptr [ebp - 4], 0xa
007ff33a je       0x7ff345
007ff33c mov      ecx, eax
007ff33e call     0x7ff3b5
007ff343 jmp      0x7ff347
007ff345 xor      eax, eax
007ff347 lea      ecx, [eax + 8]
007ff34a neg      eax
007ff34c sbb      eax, eax
007ff34e and      eax, ecx
007ff350 mov      ecx, eax
007ff352 add      eax, -8
007ff355 neg      ecx
007ff357 sbb      ecx, ecx
007ff359 and      ecx, eax
007ff35b mov      dword ptr [ebp - 0x20], ecx
007ff35e lea      ecx, [ebp - 0x24]
007ff361 mov      byte ptr [ebp - 4], 4
007ff365 call     0x801b94
007ff36a mov      ecx, dword ptr [ebp - 0x20]
007ff36d mov      byte ptr [ebp - 4], 0xb
007ff371 call     0x4edba1
007ff376 lea      ecx, [ebp - 0x24]
007ff379 mov      byte ptr [ebp - 4], 4
007ff37d call     0x801bb1
007ff382 mov      eax, dword ptr [ebp + 8]
007ff385 and      byte ptr [ebp - 4], 0
007ff389 cmp      eax, esi
007ff38b je       0x7ff393
007ff38d mov      ecx, dword ptr [eax]
007ff38f push     eax
007ff390 call     dword ptr [ecx + 8]
007ff393 mov      eax, dword ptr [ebp - 0x10]
007ff396 or       dword ptr [ebp - 4], 0xffffffff
007ff39a cmp      eax, esi
007ff39c je       0x7ff3a4
007ff39e mov      ecx, dword ptr [eax]
007ff3a0 push     eax
007ff3a1 call     dword ptr [ecx + 8]
007ff3a4 mov      ecx, dword ptr [ebp - 0xc]
007ff3a7 pop      edi
007ff3a8 pop      esi
007ff3a9 mov      dword ptr fs:[0], ecx
007ff3b0 pop      ebx
007ff3b1 leave    
007ff3b2 ret      0xc


SHA256 1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2
# function 0x801474: 471 instructions, 0x801474..0x8019ee
00801474 mov      eax, 0xabfdf0
00801479 call     0xa60b98
0080147e sub      esp, 0x2c
00801481 push     ebx
00801482 push     esi
00801483 push     edi
00801484 push     dword ptr [ebp + 8]
00801487 mov      dword ptr [ebp - 0x10], ecx
0080148a call     0x9e0502
0080148f mov      esi, dword ptr [0xbe7918]
00801495 lea      eax, [ebp - 0x30]
00801498 push     eax
00801499 mov      ecx, esi
0080149b call     0x425d0b
008014a0 mov      eax, dword ptr [eax + 4]
008014a3 xor      ebx, ebx
008014a5 cmp      dword ptr [ebp - 0x2c], ebx
008014a8 mov      dword ptr [ebp - 0x20], eax
008014ab je       0x8014b9
008014ad push     ebx
008014ae lea      ecx, [ebp - 0x30]
008014b1 call     0x428c44
008014b6 mov      dword ptr [ebp - 0x2c], ebx
008014b9 mov      ecx, dword ptr [ebp - 0x10]
008014bc lea      eax, [ebp + 8]
008014bf push     eax
008014c0 call     0x425c4c
008014c5 lea      eax, [esi + 0x30d0]
008014cb mov      dword ptr [ebp - 0x1c], eax
008014ce mov      eax, dword ptr [0xbebf98]
008014d3 add      esi, 0x3270
008014d9 cmp      eax, ebx
008014db mov      dword ptr [ebp - 4], ebx
008014de mov      dword ptr [ebp - 0x14], esi
008014e1 je       0x8016af
008014e7 mov      ecx, dword ptr [eax + 0x1edc]
008014ed cmp      dword ptr [ecx + 4], ebx
008014f0 jne      0x8015c8
008014f6 mov      eax, dword ptr [ebp + 8]
008014f9 cmp      eax, ebx
008014fb jne      0x80150a
008014fd push     0x80004003
00801502 call     0xa5fde4
00801507 mov      eax, dword ptr [ebp + 8]
0080150a mov      ecx, dword ptr [eax]
0080150c mov      esi, 0x40ff0000
00801511 push     esi
00801512 push     0x20
00801514 pop      edi
00801515 push     edi
00801516 push     edi
00801517 push     dword ptr [0xbe22cc]
0080151d mov      dword ptr [ebp - 0x24], eax
00801520 push     dword ptr [0xbe22c8]
00801526 push     eax
00801527 call     dword ptr [ecx + 0x8c]
0080152d cmp      eax, ebx
0080152f jge      0x80153f
00801531 push     0xbd82f8
00801536 push     dword ptr [ebp - 0x24]
00801539 push     eax
0080153a call     0xa5fdf2
0080153f mov      eax, dword ptr [ebp + 8]
00801542 cmp      eax, ebx
00801544 jne      0x801553
00801546 push     0x80004003
0080154b call     0xa5fde4
00801550 mov      eax, dword ptr [ebp + 8]
00801553 mov      ecx, dword ptr [eax]
00801555 push     esi
00801556 push     edi
00801557 push     edi
00801558 push     dword ptr [0xbe2304]
0080155e mov      dword ptr [ebp - 0x24], eax
00801561 push     dword ptr [0xbe2300]
00801567 push     eax
00801568 call     dword ptr [ecx + 0x8c]
0080156e cmp      eax, ebx
00801570 jge      0x801580
00801572 push     0xbd82f8
00801577 push     dword ptr [ebp - 0x24]
0080157a push     eax
0080157b call     0xa5fdf2
00801580 mov      eax, dword ptr [ebp + 8]
00801583 cmp      eax, ebx
00801585 jne      0x801594
00801587 push     0x80004003
0080158c call     0xa5fde4
00801591 mov      eax, dword ptr [ebp + 8]
00801594 mov      ecx, dword ptr [eax]
00801596 push     esi
00801597 push     edi
00801598 push     edi
00801599 push     dword ptr [0xbe2344]
0080159f mov      dword ptr [ebp - 0x24], eax
008015a2 push     dword ptr [0xbe2340]
008015a8 push     eax
008015a9 call     dword ptr [ecx + 0x8c]
008015af cmp      eax, ebx
008015b1 jge      0x8015c1
008015b3 push     0xbd82f8
008015b8 push     dword ptr [ebp - 0x24]
008015bb push     eax
008015bc call     0xa5fdf2
008015c1 mov      eax, dword ptr [0xbebf98]
008015c6 jmp      0x8015d0
008015c8 push     0x20
008015ca pop      edi
008015cb mov      esi, 0x40ff0000
008015d0 cmp      eax, ebx
008015d2 je       0x8016af
008015d8 mov      eax, dword ptr [eax + 0x1edc]
008015de mov      eax, dword ptr [eax + 4]
008015e1 cmp      eax, ebx
008015e3 je       0x8015fc
008015e5 push     dword ptr [eax + 0x16c]
008015eb add      eax, 0x164
008015f0 push     eax
008015f1 call     0x508a95
008015f6 cmp      eax, ebx
008015f8 pop      ecx
008015f9 pop      ecx
008015fa jne      0x801641
008015fc mov      eax, dword ptr [ebp + 8]
008015ff cmp      eax, ebx
00801601 jne      0x801610
00801603 push     0x80004003
00801608 call     0xa5fde4
0080160d mov      eax, dword ptr [ebp + 8]
00801610 mov      ecx, dword ptr [eax]
00801612 push     esi
00801613 push     edi
00801614 push     edi
00801615 push     0xb
00801617 push     0x2e
00801619 push     eax
0080161a mov      dword ptr [ebp - 0x24], eax
0080161d call     dword ptr [ecx + 0x8c]
00801623 cmp      eax, ebx
00801625 jge      0x801635
00801627 push     0xbd82f8
0080162c push     dword ptr [ebp - 0x24]
0080162f push     eax
00801630 call     0xa5fdf2
00801635 mov      ecx, dword ptr [0xbed5a0]
0080163b push     ebx
0080163c call     0x58e03c
00801641 mov      eax, dword ptr [0xbebf98]
00801646 mov      eax, dword ptr [eax + 0x1edc]
0080164c mov      eax, dword ptr [eax + 4]
0080164f cmp      eax, ebx
00801651 je       0x80166a
00801653 push     dword ptr [eax + 0x178]
00801659 add      eax, 0x170
0080165e push     eax
0080165f call     0x508a95
00801664 cmp      eax, ebx
00801666 pop      ecx
00801667 pop      ecx
00801668 jne      0x8016af
0080166a mov      eax, dword ptr [ebp + 8]
0080166d cmp      eax, ebx
0080166f jne      0x80167e
00801671 push     0x80004003
00801676 call     0xa5fde4
0080167b mov      eax, dword ptr [ebp + 8]
0080167e mov      ecx, dword ptr [eax]
00801680 push     esi
00801681 push     edi
00801682 push     edi
00801683 push     0xb
00801685 push     0x70
00801687 push     eax
00801688 mov      dword ptr [ebp - 0x24], eax
0080168b call     dword ptr [ecx + 0x8c]
00801691 cmp      eax, ebx
00801693 jge      0x8016a3
00801695 push     0xbd82f8
0080169a push     dword ptr [ebp - 0x24]
0080169d push     eax
0080169e call     0xa5fdf2
008016a3 mov      ecx, dword ptr [0xbed5a0]
008016a9 push     ebx
008016aa call     0x58e0a8
008016af mov      eax, dword ptr [ebp - 0x1c]
008016b2 mov      ecx, dword ptr [ebp - 0x20]
008016b5 mov      edi, dword ptr [ebp - 0x14]
008016b8 add      ecx, 0x293
008016be lea      edx, [eax + 0xc]
008016c1 sub      edi, eax
008016c3 mov      esi, 0xbe2260
008016c8 mov      dword ptr [ebp - 0x18], edx
008016cb mov      dword ptr [ebp - 0x1c], ecx
008016ce mov      dword ptr [ebp - 0x14], edi
008016d1 mov      eax, 0xbe22c8
008016d6 cmp      esi, eax
008016d8 je       0x8016f2
008016da cmp      esi, 0xbe22f8
008016e0 jle      0x80182c
008016e6 cmp      esi, 0xbe23d8
008016ec jg       0x80182c
008016f2 cmp      esi, 0xbe2318
008016f8 je       0x801780
008016fe cmp      esi, 0xbe2320
00801704 je       0x801780
00801706 cmp      esi, eax
00801708 je       0x801722
0080170a cmp      esi, 0xbe2300
00801710 jl       0x80171a
00801712 cmp      esi, 0xbe2340
00801718 jle      0x801722
0080171a cmp      esi, 0xbe23c8
00801720 jne      0x801733
00801722 mov      eax, dword ptr [ebp - 0x10]
00801725 cmp      dword ptr [eax + 0x5a0], ebx
0080172b jne      0x80182c
00801731 jmp      0x801736
00801733 mov      eax, dword ptr [ebp - 0x10]
00801736 cmp      esi, 0xbe2348
0080173c jl       0x801746
0080173e cmp      esi, 0xbe2380
00801744 jle      0x80174e
00801746 cmp      esi, 0xbe23d0
0080174c jne      0x80175b
0080174e cmp      dword ptr [eax + 0x5a0], 1
00801755 jne      0x80182c
0080175b cmp      esi, 0xbe2388
00801761 jl       0x80176b
00801763 cmp      esi, 0xbe23c0
00801769 jle      0x801773
0080176b cmp      esi, 0xbe23d8
00801771 jne      0x801780
00801773 cmp      dword ptr [eax + 0x5a0], 2
0080177a jne      0x80182c
00801780 mov      eax, dword ptr [ecx]
00801782 cmp      eax, ebx
00801784 mov      edi, dword ptr [ecx - 0x1a0]
0080178a je       0x801798
0080178c mov      ecx, dword ptr [ebp - 0x14]
0080178f mov      edi, eax
00801791 xor      eax, eax
00801793 cmp      dword ptr [ecx + edx], ebx
00801796 jmp      0x80179c
00801798 xor      eax, eax
0080179a cmp      dword ptr [edx], ebx
0080179c setne    al
0080179f cmp      edi, ebx
008017a1 mov      dword ptr [ebp - 0x24], eax
008017a4 je       0x80182c
008017aa lea      ecx, [edi + 0xc]
008017ad call     0x42873d
008017b2 push     ebx
008017b3 push     ebx
008017b4 push     ebx
008017b5 push     ebx
008017b6 mov      dword ptr [ebp - 0x28], eax
008017b9 mov      eax, dword ptr [edi]
008017bb push     ebx
008017bc mov      ecx, edi
008017be call     dword ptr [eax + 4]
008017c1 push     eax
008017c2 mov      eax, dword ptr [esi + 4]
008017c5 add      eax, 0x20
008017c8 push     eax
008017c9 push     dword ptr [esi]
008017cb lea      eax, [ebp + 8]
008017ce push     dword ptr [ebp - 0x28]
008017d1 push     ecx
008017d2 mov      ecx, esp
008017d4 mov      dword ptr [ebp - 0x28], esp
008017d7 push     eax
008017d8 call     0x4281dd
008017dd mov      byte ptr [ebp - 4], bl
008017e0 call     0x4283f8
008017e5 mov      ecx, eax
008017e7 call     0x5d6458
008017ec cmp      dword ptr [ebp - 0x24], ebx
008017ef jne      0x80182c
008017f1 cmp      dword ptr [ebp + 8], ebx
008017f4 jne      0x801800
008017f6 push     0x80004003
008017fb call     0xa5fde4
00801800 mov      eax, dword ptr [esi + 4]
00801803 mov      ecx, dword ptr [esi]
00801805 mov      edi, dword ptr [ebp + 8]
00801808 mov      edx, dword ptr [edi]
0080180a push     0x40ff0000
0080180f push     0x20
00801811 push     0x20
00801813 push     eax
00801814 push     ecx
00801815 push     edi
00801816 call     dword ptr [edx + 0x8c]
0080181c cmp      eax, ebx
0080181e jge      0x80182c
00801820 push     0xbd82f8
00801825 push     edi
00801826 push     eax
00801827 call     0xa5fdf2
0080182c mov      ecx, dword ptr [ebp - 0x1c]
0080182f mov      edx, dword ptr [ebp - 0x18]
00801832 add      ecx, 8
00801835 add      edx, 8
00801838 add      esi, 8
0080183b cmp      esi, 0xbe23f0
00801841 mov      dword ptr [ebp - 0x1c], ecx
00801844 mov      dword ptr [ebp - 0x18], edx
00801847 jle      0x8016d1
0080184d mov      eax, dword ptr [ebp - 0x20]
00801850 mov      eax, dword ptr [eax + 0x44f]
00801856 cmp      eax, ebx
00801858 jne      0x80185e
0080185a xor      eax, eax
0080185c jmp      0x801861
0080185e mov      eax, dword ptr [eax - 4]
00801861 lea      ecx, [eax - 1]
00801864 mov      eax, dword ptr [0xbed5a0]
00801869 mov      edi, dword ptr [eax + 0x3c0]
0080186f mov      eax, dword ptr [eax + 0x3c4]
00801875 push     1
00801877 mov      dword ptr [ebp - 0x24], eax
0080187a pop      eax
0080187b cmp      ecx, eax
0080187d mov      dword ptr [ebp - 0x28], ecx
00801880 mov      dword ptr [ebp - 0x18], ebx
00801883 mov      dword ptr [ebp - 0x1c], ebx
00801886 mov      dword ptr [ebp - 0x14], eax
00801889 jl       0x801902
0080188b push     dword ptr [ebp - 0x14]
0080188e mov      ecx, dword ptr [ebp - 0x20]
00801891 push     2
00801893 lea      eax, [ebp - 0x38]
00801896 push     eax
00801897 call     0x4282f7
0080189c cmp      dword ptr [ebp - 0x34], ebx
0080189f mov      esi, dword ptr [eax + 4]
008018a2 je       0x8018b0
008018a4 push     ebx
008018a5 lea      ecx, [ebp - 0x38]
008018a8 call     0x428a50
008018ad mov      dword ptr [ebp - 0x34], ebx
008018b0 cmp      esi, ebx
008018b2 je       0x8018f7
008018b4 lea      ecx, [esi + 0xc]
008018b7 call     0x42873d
008018bc cmp      eax, edi
008018be jne      0x8018d5
008018c0 mov      eax, dword ptr [esi]
008018c2 mov      ecx, esi
008018c4 call     dword ptr [eax + 4]
008018c7 test     eax, eax
008018c9 jne      0x8018d5
008018cb mov      eax, dword ptr [esi]
008018cd mov      ecx, esi
008018cf call     dword ptr [eax + 0x20]
008018d2 add      dword ptr [ebp - 0x18], eax
008018d5 lea      ecx, [esi + 0xc]
008018d8 call     0x42873d
008018dd cmp      eax, dword ptr [ebp - 0x24]
008018e0 jne      0x8018f7
008018e2 mov      eax, dword ptr [esi]
008018e4 mov      ecx, esi
008018e6 call     dword ptr [eax + 4]
008018e9 test     eax, eax
008018eb jne      0x8018f7
008018ed mov      eax, dword ptr [esi]
008018ef mov      ecx, esi
008018f1 call     dword ptr [eax + 0x20]
008018f4 add      dword ptr [ebp - 0x1c], eax
008018f7 inc      dword ptr [ebp - 0x14]
008018fa mov      eax, dword ptr [ebp - 0x14]
008018fd cmp      eax, dword ptr [ebp - 0x28]
00801900 jle      0x80188b
00801902 cmp      edi, ebx
00801904 je       0x801967
00801906 push     ebx
00801907 push     ebx
00801908 push     ebx
00801909 push     ebx
0080190a push     ebx
0080190b push     ebx
0080190c push     0x2b
0080190e push     0x2e
00801910 push     edi
00801911 push     ecx
00801912 lea      eax, [ebp + 8]
00801915 mov      ecx, esp
00801917 mov      dword ptr [ebp - 0x28], esp
0080191a push     eax
0080191b call     0x4281dd
00801920 mov      byte ptr [ebp - 4], bl
00801923 call     0x4283f8
00801928 mov      ecx, eax
0080192a call     0x5d6458
0080192f cmp      dword ptr [ebp - 0x18], ebx
00801932 jl       0x801967
00801934 mov      eax, dword ptr [ebp - 0x10]
00801937 push     ebx
00801938 push     ecx
00801939 add      eax, 0x5a4
0080193e mov      ecx, esp
00801940 mov      dword ptr [ebp - 0x28], esp
00801943 push     eax
00801944 call     0x4281c2
00801949 push     dword ptr [ebp - 0x18]
0080194c lea      eax, [ebp + 8]
0080194f push     0x1f
00801951 push     0x2e
00801953 push     ecx
00801954 mov      ecx, esp
00801956 mov      dword ptr [ebp - 0x28], esp
00801959 push     eax
0080195a call     0x4281dd
0080195f call     0x988345
00801964 add      esp, 0x18
00801967 cmp      dword ptr [ebp - 0x24], ebx
0080196a je       0x8019cf
0080196c push     ebx
0080196d push     ebx
0080196e push     ebx
0080196f push     ebx
00801970 push     ebx
00801971 push     ebx
00801972 push     0x2b
00801974 push     0x70
00801976 push     dword ptr [ebp - 0x24]
00801979 lea      eax, [ebp + 8]
0080197c push     ecx
0080197d mov      ecx, esp
0080197f mov      dword ptr [ebp - 0x28], esp
00801982 push     eax
00801983 call     0x4281dd
00801988 mov      byte ptr [ebp - 4], bl
0080198b call     0x4283f8
00801990 mov      ecx, eax
00801992 call     0x5d6458
00801997 cmp      dword ptr [ebp - 0x1c], ebx
0080199a jl       0x8019cf
0080199c mov      eax, dword ptr [ebp - 0x10]
0080199f push     ebx
008019a0 push     ecx
008019a1 add      eax, 0x5a4
008019a6 mov      ecx, esp
008019a8 mov      dword ptr [ebp - 0x28], esp
008019ab push     eax
008019ac call     0x4281c2
008019b1 push     dword ptr [ebp - 0x1c]
008019b4 lea      eax, [ebp + 8]
008019b7 push     0x1f
008019b9 push     0x70
008019bb push     ecx
008019bc mov      ecx, esp
008019be mov      dword ptr [ebp - 0x28], esp
008019c1 push     eax
008019c2 call     0x4281dd
008019c7 call     0x988345
008019cc add      esp, 0x18
008019cf mov      eax, dword ptr [ebp + 8]
008019d2 or       dword ptr [ebp - 4], 0xffffffff
008019d6 cmp      eax, ebx
008019d8 je       0x8019e0
008019da mov      ecx, dword ptr [eax]
008019dc push     eax
008019dd call     dword ptr [ecx + 8]
008019e0 mov      ecx, dword ptr [ebp - 0xc]
008019e3 pop      edi
008019e4 pop      esi
008019e5 mov      dword ptr fs:[0], ecx
008019ec pop      ebx
008019ed leave    
008019ee ret      4


SHA256 1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2
# function 0x800214: 1186 instructions, 0x800214..0x800f78
00800214 mov      eax, 0xabfd99
00800219 call     0xa60b98
0080021e sub      esp, 0x88
00800224 cmp      dword ptr [ebp + 8], 0x201
0080022b push     ebx
0080022c push     esi
0080022d mov      esi, ecx
0080022f push     edi
00800230 mov      dword ptr [ebp - 0x1c], esi
00800233 jne      0x800f6a
00800239 push     dword ptr [ebp + 0x14]
0080023c lea      ecx, [esi - 4]
0080023f push     dword ptr [ebp + 0x10]
00800242 mov      dword ptr [ebp - 0x18], ecx
00800245 call     0x8011fa
0080024a mov      ebx, eax
0080024c xor      edi, edi
0080024e cmp      ebx, edi
00800250 jne      0x80025f
00800252 lea      ecx, [esi + 0x68]
00800255 call     0x8e6e23
0080025a jmp      0x800f6a
0080025f mov      esi, dword ptr [0xbe7918]
00800265 push     edi
00800266 push     0x1f4
0080026b mov      ecx, esi
0080026d call     0x485bf7
00800272 test     eax, eax
00800274 je       0x800f6a
0080027a lea      eax, [ebp - 0x68]
0080027d push     eax
0080027e mov      ecx, esi
00800280 call     0x425d0b
00800285 mov      esi, dword ptr [eax + 4]
00800288 lea      ecx, [ebp - 0x68]
0080028b mov      dword ptr [ebp - 0x14], esi
0080028e call     0x4289b7
00800293 push     -0x64
00800295 pop      eax
00800296 sub      eax, ebx
00800298 push     eax
00800299 mov      dword ptr [ebp - 0x40], eax
0080029c push     1
0080029e lea      eax, [ebp - 0x68]
008002a1 push     eax
008002a2 mov      ecx, esi
008002a4 call     0x4282f7
008002a9 mov      esi, dword ptr [eax + 4]
008002ac lea      ecx, [ebp - 0x68]
008002af call     0x4288c1
008002b4 cmp      esi, edi
008002b6 jne      0x8002d7
008002b8 mov      ecx, dword ptr [ebp - 0x14]
008002bb neg      ebx
008002bd push     ebx
008002be push     1
008002c0 lea      eax, [ebp - 0x68]
008002c3 push     eax
008002c4 mov      dword ptr [ebp - 0x40], ebx
008002c7 call     0x4282f7
008002cc mov      esi, dword ptr [eax + 4]
008002cf lea      ecx, [ebp - 0x68]
008002d2 call     0x4288c1
008002d7 mov      ebx, 0xbf6300
008002dc push     ebx
008002dd lea      ecx, [ebp - 0x50]
008002e0 call     0x402f85
008002e5 push     3
008002e7 push     edi
008002e8 lea      ecx, [ebp - 0x70]
008002eb mov      dword ptr [ebp - 4], edi
008002ee call     0x402fab
008002f3 cmp      dword ptr [0xbf14ec], edi
008002f9 mov      byte ptr [ebp - 4], 1
008002fd jne      0x800309
008002ff push     0x80004003
00800304 call     0xa5fde4
00800309 mov      ecx, dword ptr [0xbf14ec]
0080030f lea      eax, [ebp - 0x50]
00800312 push     eax
00800313 lea      eax, [ebp - 0x70]
00800316 push     eax
00800317 push     edi
00800318 push     edi
00800319 push     edi
0080031a push     edi
0080031b push     edi
0080031c lea      eax, [ebp - 0x84]
00800322 push     eax
00800323 call     0x426c7e
00800328 lea      eax, [ebp - 0x70]
0080032b push     eax
0080032c mov      byte ptr [ebp - 4], 4
00800330 call     0x40291d
00800335 cmp      eax, edi
00800337 pop      ecx
00800338 jge      0x800340
0080033a push     eax
0080033b call     0xa5fde4
00800340 lea      eax, [ebp - 0x50]
00800343 push     eax
00800344 mov      byte ptr [ebp - 4], 3
00800348 call     0x40291d
0080034d cmp      eax, edi
0080034f pop      ecx
00800350 jge      0x800358
00800352 push     eax
00800353 call     0xa5fde4
00800358 mov      dword ptr [ebp - 0x64], edi
0080035b cmp      esi, edi
0080035d mov      byte ptr [ebp - 4], 5
00800361 je       0x800746
00800367 push     ebx
00800368 lea      ecx, [ebp - 0x60]
0080036b call     0x402f85
00800370 push     3
00800372 push     edi
00800373 lea      ecx, [ebp - 0x50]
00800376 mov      byte ptr [ebp - 4], 6
0080037a call     0x402fab
0080037f cmp      dword ptr [0xbf14ec], edi
00800385 mov      byte ptr [ebp - 4], 7
00800389 jne      0x800395
0080038b push     0x80004003
00800390 call     0xa5fde4
00800395 mov      ecx, dword ptr [0xbf14ec]
0080039b lea      eax, [ebp - 0x60]
0080039e push     eax
0080039f lea      eax, [ebp - 0x50]
008003a2 push     eax
008003a3 push     edi
008003a4 push     edi
008003a5 push     edi
008003a6 push     edi
008003a7 push     edi
008003a8 lea      eax, [ebp + 8]
008003ab push     eax
008003ac call     0x426c7e
008003b1 lea      eax, [ebp - 0x50]
008003b4 push     eax
008003b5 mov      byte ptr [ebp - 4], 0xa
008003b9 call     0x40291d
008003be cmp      eax, edi
008003c0 pop      ecx
008003c1 jge      0x8003c9
008003c3 push     eax
008003c4 call     0xa5fde4
008003c9 lea      eax, [ebp - 0x60]
008003cc push     eax
008003cd mov      byte ptr [ebp - 4], 9
008003d1 call     0x40291d
008003d6 cmp      eax, edi
008003d8 pop      ecx
008003d9 jge      0x8003e1
008003db push     eax
008003dc call     0xa5fde4
008003e1 mov      eax, dword ptr [0xbe78d8]
008003e6 push     edi
008003e7 push     edi
008003e8 lea      ecx, [esi + 0xc]
008003eb mov      dword ptr [ebp - 0x14], eax
008003ee call     0x42873d
008003f3 mov      ecx, dword ptr [ebp - 0x14]
008003f6 push     eax
008003f7 lea      eax, [ebp - 0x10]
008003fa push     eax
008003fb call     0x5d3bd8
00800400 push     ebx
00800401 lea      ecx, [ebp - 0x2c]
00800404 mov      byte ptr [ebp - 4], 0xb
00800408 call     0x402f85
0080040d push     ebx
0080040e lea      ecx, [ebp - 0x3c]
00800411 mov      byte ptr [ebp - 4], 0xc
00800415 call     0x402f85
0080041a push     ebx
0080041b lea      ecx, [ebp - 0x80]
0080041e mov      byte ptr [ebp - 4], 0xd
00800422 call     0x402f85
00800427 push     ebx
00800428 lea      ecx, [ebp - 0x50]
0080042b mov      byte ptr [ebp - 4], 0xe
0080042f call     0x402f85
00800434 push     ebx
00800435 lea      ecx, [ebp - 0x60]
00800438 mov      byte ptr [ebp - 4], 0xf
0080043c call     0x402f85
00800441 cmp      dword ptr [ebp + 8], edi
00800444 mov      byte ptr [ebp - 4], 0x10
00800448 jne      0x800454
0080044a push     0x80004003
0080044f call     0xa5fde4
00800454 mov      ecx, dword ptr [ebp + 8]
00800457 lea      eax, [ebp - 0x2c]
0080045a push     eax
0080045b lea      eax, [ebp - 0x3c]
0080045e push     eax
0080045f lea      eax, [ebp - 0x80]
00800462 push     eax
00800463 lea      eax, [ebp - 0x50]
00800466 push     eax
00800467 lea      eax, [ebp - 0x60]
0080046a push     eax
0080046b push     dword ptr [ebp - 0x10]
0080046e lea      eax, [ebp - 0x94]
00800474 push     eax
00800475 call     0x426bab
0080047a lea      eax, [ebp - 0x94]
00800480 push     eax
00800481 call     0x40291d
00800486 cmp      eax, edi
00800488 pop      ecx
00800489 jge      0x800491
0080048b push     eax
0080048c call     0xa5fde4
00800491 lea      eax, [ebp - 0x60]
00800494 push     eax
00800495 mov      byte ptr [ebp - 4], 0xf
00800499 call     0x40291d
0080049e cmp      eax, edi
008004a0 pop      ecx
008004a1 jge      0x8004a9
008004a3 push     eax
008004a4 call     0xa5fde4
008004a9 lea      eax, [ebp - 0x50]
008004ac push     eax
008004ad mov      byte ptr [ebp - 4], 0xe
008004b1 call     0x40291d
008004b6 cmp      eax, edi
008004b8 pop      ecx
008004b9 jge      0x8004c1
008004bb push     eax
008004bc call     0xa5fde4
008004c1 lea      eax, [ebp - 0x80]
008004c4 push     eax
008004c5 mov      byte ptr [ebp - 4], 0xd
008004c9 call     0x40291d
008004ce cmp      eax, edi
008004d0 pop      ecx
008004d1 jge      0x8004d9
008004d3 push     eax
008004d4 call     0xa5fde4
008004d9 lea      eax, [ebp - 0x3c]
008004dc push     eax
008004dd mov      byte ptr [ebp - 4], 0xc
008004e1 call     0x40291d
008004e6 cmp      eax, edi
008004e8 pop      ecx
008004e9 jge      0x8004f1
008004eb push     eax
008004ec call     0xa5fde4
008004f1 lea      eax, [ebp - 0x2c]
008004f4 push     eax
008004f5 mov      byte ptr [ebp - 4], 0xb
008004f9 call     0x40291d
008004fe cmp      eax, edi
00800500 pop      ecx
00800501 jge      0x800509
00800503 push     eax
00800504 call     0xa5fde4
00800509 cmp      dword ptr [ebp + 8], edi
0080050c jne      0x800518
0080050e push     0x80004003
00800513 call     0xa5fde4
00800518 mov      esi, dword ptr [ebp + 8]
0080051b mov      eax, dword ptr [esi]
0080051d push     0x80ffffff
00800522 push     esi
00800523 call     dword ptr [eax + 0xe0]
00800529 cmp      eax, edi
0080052b jge      0x800539
0080052d push     0xbd8358
00800532 push     esi
00800533 push     eax
00800534 call     0xa5fdf2
00800539 mov      ecx, dword ptr [ebp - 0x18]
0080053c lea      eax, [ebp - 0x18]
0080053f push     eax
00800540 call     0x426604
00800545 mov      eax, dword ptr [eax]
00800547 push     1
00800549 push     eax
0080054a lea      ecx, [ebp - 0x2c]
0080054d call     0x410fdf
00800552 mov      eax, dword ptr [ebp + 8]
00800555 cmp      eax, edi
00800557 mov      byte ptr [ebp - 4], 0x12
0080055b jne      0x80056a
0080055d push     0x80004003
00800562 call     0xa5fde4
00800567 mov      eax, dword ptr [ebp + 8]
0080056a mov      ecx, dword ptr [eax]
0080056c sub      esp, 0x10
0080056f mov      edi, esp
00800571 lea      esi, [ebp - 0x2c]
00800574 movsd    dword ptr es:[edi], dword ptr [esi]
00800575 movsd    dword ptr es:[edi], dword ptr [esi]
00800576 movsd    dword ptr es:[edi], dword ptr [esi]
00800577 push     eax
00800578 mov      dword ptr [ebp - 0x14], eax
0080057b movsd    dword ptr es:[edi], dword ptr [esi]
0080057c call     dword ptr [ecx + 0x64]
0080057f test     eax, eax
00800581 jge      0x800591
00800583 push     0xbd8348
00800588 push     dword ptr [ebp - 0x14]
0080058b push     eax
0080058c call     0xa5fdf2
00800591 lea      eax, [ebp - 0x2c]
00800594 push     eax
00800595 mov      byte ptr [ebp - 4], 0x11
00800599 call     0x40291d
0080059e test     eax, eax
008005a0 pop      ecx
008005a1 jge      0x8005a9
008005a3 push     eax
008005a4 call     0xa5fde4
008005a9 lea      ecx, [ebp - 0x18]
008005ac mov      byte ptr [ebp - 4], 0xb
008005b0 call     0x4156c5
008005b5 push     ebx
008005b6 lea      ecx, [ebp - 0x3c]
008005b9 call     0x402f85
008005be push     ebx
008005bf lea      ecx, [ebp - 0x2c]
008005c2 mov      byte ptr [ebp - 4], 0x13
008005c6 call     0x402f85
008005cb cmp      dword ptr [ebp - 0x10], 0
008005cf mov      byte ptr [ebp - 4], 0x14
008005d3 mov      esi, 0x80004003
008005d8 jne      0x8005ec
008005da push     esi
008005db call     0xa5fde4
008005e0 cmp      dword ptr [ebp - 0x10], 0
008005e4 jne      0x8005ec
008005e6 push     esi
008005e7 call     0xa5fde4
008005ec cmp      dword ptr [ebp + 8], 0
008005f0 mov      edi, dword ptr [ebp - 0x10]
008005f3 jne      0x8005fb
008005f5 push     esi
008005f6 call     0xa5fde4
008005fb mov      esi, dword ptr [ebp + 8]
008005fe mov      ecx, dword ptr [ebp - 0x10]
00800601 mov      dword ptr [ebp - 0x18], esi
00800604 call     0x40f0c2
00800609 mov      ecx, eax
0080060b mov      eax, dword ptr [ebp + 0x14]
0080060e sub      eax, ecx
00800610 add      eax, 0x10
00800613 mov      ecx, edi
00800615 mov      dword ptr [ebp - 0x14], eax
00800618 call     0x40f09b
0080061d mov      ecx, eax
0080061f mov      eax, dword ptr [ebp + 0x10]
00800622 sub      esp, 0x10
00800625 mov      edi, esp
00800627 sub      eax, ecx
00800629 mov      ecx, dword ptr [esi]
0080062b lea      esi, [ebp - 0x3c]
0080062e movsd    dword ptr es:[edi], dword ptr [esi]
0080062f movsd    dword ptr es:[edi], dword ptr [esi]
00800630 movsd    dword ptr es:[edi], dword ptr [esi]
00800631 movsd    dword ptr es:[edi], dword ptr [esi]
00800632 sub      esp, 0x10
00800635 mov      edi, esp
00800637 push     dword ptr [ebp - 0x14]
0080063a lea      esi, [ebp - 0x2c]
0080063d movsd    dword ptr es:[edi], dword ptr [esi]
0080063e movsd    dword ptr es:[edi], dword ptr [esi]
0080063f sub      eax, 0x10
00800642 movsd    dword ptr es:[edi], dword ptr [esi]
00800643 push     eax
00800644 push     dword ptr [ebp - 0x18]
00800647 movsd    dword ptr es:[edi], dword ptr [esi]
00800648 call     dword ptr [ecx + 0x90]
0080064e test     eax, eax
00800650 jge      0x800660
00800652 push     0xbd8348
00800657 push     dword ptr [ebp - 0x18]
0080065a push     eax
0080065b call     0xa5fdf2
00800660 lea      eax, [ebp - 0x2c]
00800663 push     eax
00800664 mov      byte ptr [ebp - 4], 0x13
00800668 call     0x40291d
0080066d test     eax, eax
0080066f pop      ecx
00800670 jge      0x800678
00800672 push     eax
00800673 call     0xa5fde4
00800678 lea      eax, [ebp - 0x3c]
0080067b push     eax
0080067c mov      byte ptr [ebp - 4], 0xb
00800680 call     0x40291d
00800685 test     eax, eax
00800687 pop      ecx
00800688 jge      0x800690
0080068a push     eax
0080068b call     0xa5fde4
00800690 push     0x28
00800692 mov      ecx, 0xbf0b00
00800697 call     0x403065
0080069c mov      esi, eax
0080069e mov      dword ptr [ebp - 0x14], esi
008006a1 test     esi, esi
008006a3 mov      byte ptr [ebp - 4], 0x15
008006a7 je       0x8006db
008006a9 push     dword ptr [ebp + 8]
008006ac mov      ecx, esi
008006ae call     0x6ffda3
008006b3 mov      eax, dword ptr [ebp - 0x40]
008006b6 and      dword ptr [esi + 0x20], 0
008006ba mov      dword ptr [esi + 0x1c], eax
008006bd mov      eax, dword ptr [ebp - 0x1c]
008006c0 lea      ecx, [eax - 4]
008006c3 neg      ecx
008006c5 sbb      ecx, ecx
008006c7 and      ecx, eax
008006c9 mov      dword ptr [esi + 0x18], 1
008006d0 mov      dword ptr [esi + 0x24], ecx
008006d3 mov      dword ptr [esi], 0xaf34d8
008006d9 jmp      0x8006e0
008006db mov      eax, dword ptr [ebp - 0x1c]
008006de xor      esi, esi
008006e0 lea      ecx, [eax - 4]
008006e3 neg      ecx
008006e5 sbb      ecx, ecx
008006e7 and      ecx, eax
008006e9 push     esi
008006ea push     ecx
008006eb mov      ecx, dword ptr [0xbec20c]
008006f1 mov      byte ptr [ebp - 4], 0xb
008006f5 call     0x9e353d
008006fa lea      eax, [ebp - 0x14]
008006fd push     0x737
00800702 push     eax
00800703 call     0x79e805
00800708 mov      ecx, eax
0080070a call     0x406276
0080070f push     dword ptr [eax]
00800711 mov      byte ptr [ebp - 4], 0x16
00800715 call     0x989588
0080071a mov      eax, dword ptr [ebp - 0x14]
0080071d test     eax, eax
0080071f pop      ecx
00800720 mov      byte ptr [ebp - 4], 0xb
00800724 je       0x800730
00800726 add      eax, -0xc
00800729 push     eax
0080072a call     0x4033a7
0080072f pop      ecx
00800730 lea      ecx, [ebp - 0x10]
00800733 call     0x4156ab
00800738 lea      ecx, [ebp + 8]
0080073b mov      byte ptr [ebp - 4], 5
0080073f call     0x4156c5
00800744 xor      edi, edi
00800746 push     dword ptr [ebp + 0x14]
00800749 mov      eax, dword ptr [ebp - 0x1c]
0080074c push     dword ptr [ebp + 0x10]
0080074f lea      ecx, [eax - 4]
00800752 mov      dword ptr [ebp - 0x18], ecx
00800755 call     0x8011fa
0080075a cmp      eax, 0xc8
0080075f jne      0x800b4c
00800765 mov      eax, dword ptr [0xbed5a0]
0080076a cmp      dword ptr [eax + 0x3c0], edi
00800770 je       0x800b4c
00800776 push     ebx
00800777 lea      ecx, [ebp - 0x3c]
0080077a call     0x402f85
0080077f push     3
00800781 push     edi
00800782 lea      ecx, [ebp - 0x2c]
00800785 mov      byte ptr [ebp - 4], 0x17
00800789 call     0x402fab
0080078e cmp      dword ptr [0xbf14ec], edi
00800794 mov      byte ptr [ebp - 4], 0x18
00800798 mov      esi, 0x80004003
0080079d jne      0x8007a5
0080079f push     esi
008007a0 call     0xa5fde4
008007a5 mov      ecx, dword ptr [0xbf14ec]
008007ab lea      eax, [ebp - 0x3c]
008007ae push     eax
008007af lea      eax, [ebp - 0x2c]
008007b2 push     eax
008007b3 push     edi
008007b4 push     edi
008007b5 push     edi
008007b6 push     edi
008007b7 push     edi
008007b8 lea      eax, [ebp + 8]
008007bb push     eax
008007bc call     0x426c7e
008007c1 lea      eax, [ebp - 0x2c]
008007c4 push     eax
008007c5 mov      byte ptr [ebp - 4], 0x1b
008007c9 call     0x40291d
008007ce cmp      eax, edi
008007d0 pop      ecx
008007d1 jge      0x8007d9
008007d3 push     eax
008007d4 call     0xa5fde4
008007d9 lea      eax, [ebp - 0x3c]
008007dc push     eax
008007dd mov      byte ptr [ebp - 4], 0x1a
008007e1 call     0x40291d
008007e6 cmp      eax, edi
008007e8 pop      ecx
008007e9 jge      0x8007f1
008007eb push     eax
008007ec call     0xa5fde4
008007f1 mov      eax, dword ptr [0xbed5a0]
008007f6 mov      eax, dword ptr [eax + 0x3c0]
008007fc mov      ecx, dword ptr [0xbe78d8]
00800802 push     edi
00800803 push     edi
00800804 push     eax
00800805 lea      eax, [ebp - 0x10]
00800808 push     eax
00800809 call     0x5d3bd8
0080080e push     ebx
0080080f lea      ecx, [ebp - 0x50]
00800812 mov      byte ptr [ebp - 4], 0x1c
00800816 call     0x402f85
0080081b push     ebx
0080081c lea      ecx, [ebp - 0x60]
0080081f mov      byte ptr [ebp - 4], 0x1d
00800823 call     0x402f85
00800828 push     ebx
00800829 lea      ecx, [ebp - 0x80]
0080082c mov      byte ptr [ebp - 4], 0x1e
00800830 call     0x402f85
00800835 push     ebx
00800836 lea      ecx, [ebp - 0x3c]
00800839 mov      byte ptr [ebp - 4], 0x1f
0080083d call     0x402f85
00800842 push     ebx
00800843 lea      ecx, [ebp - 0x2c]
00800846 mov      byte ptr [ebp - 4], 0x20
0080084a call     0x402f85
0080084f cmp      dword ptr [ebp + 8], edi
00800852 mov      byte ptr [ebp - 4], 0x21
00800856 jne      0x80085e
00800858 push     esi
00800859 call     0xa5fde4
0080085e mov      ecx, dword ptr [ebp + 8]
00800861 lea      eax, [ebp - 0x50]
00800864 push     eax
00800865 lea      eax, [ebp - 0x60]
00800868 push     eax
00800869 lea      eax, [ebp - 0x80]
0080086c push     eax
0080086d lea      eax, [ebp - 0x3c]
00800870 push     eax
00800871 lea      eax, [ebp - 0x2c]
00800874 push     eax
00800875 push     dword ptr [ebp - 0x10]
00800878 lea      eax, [ebp - 0x94]
0080087e push     eax
0080087f call     0x426bab
00800884 lea      eax, [ebp - 0x94]
0080088a push     eax
0080088b call     0x40291d
00800890 cmp      eax, edi
00800892 pop      ecx
00800893 jge      0x80089b
00800895 push     eax
00800896 call     0xa5fde4
0080089b lea      eax, [ebp - 0x2c]
0080089e push     eax
0080089f mov      byte ptr [ebp - 4], 0x20
008008a3 call     0x40291d
008008a8 cmp      eax, edi
008008aa pop      ecx
008008ab jge      0x8008b3
008008ad push     eax
008008ae call     0xa5fde4
008008b3 lea      eax, [ebp - 0x3c]
008008b6 push     eax
008008b7 mov      byte ptr [ebp - 4], 0x1f
008008bb call     0x40291d
008008c0 cmp      eax, edi
008008c2 pop      ecx
008008c3 jge      0x8008cb
008008c5 push     eax
008008c6 call     0xa5fde4
008008cb lea      eax, [ebp - 0x80]
008008ce push     eax
008008cf mov      byte ptr [ebp - 4], 0x1e
008008d3 call     0x40291d
008008d8 cmp      eax, edi
008008da pop      ecx
008008db jge      0x8008e3
008008dd push     eax
008008de call     0xa5fde4
008008e3 lea      eax, [ebp - 0x60]
008008e6 push     eax
008008e7 mov      byte ptr [ebp - 4], 0x1d
008008eb call     0x40291d
008008f0 cmp      eax, edi
008008f2 pop      ecx
008008f3 jge      0x8008fb
008008f5 push     eax
008008f6 call     0xa5fde4
008008fb lea      eax, [ebp - 0x50]
008008fe push     eax
008008ff mov      byte ptr [ebp - 4], 0x1c
00800903 call     0x40291d
00800908 cmp      eax, edi
0080090a pop      ecx
0080090b jge      0x800913
0080090d push     eax
0080090e call     0xa5fde4
00800913 cmp      dword ptr [ebp + 8], edi
00800916 jne      0x80091e
00800918 push     esi
00800919 call     0xa5fde4
0080091e mov      esi, dword ptr [ebp + 8]
00800921 mov      eax, dword ptr [esi]
00800923 push     0x80ffffff
00800928 push     esi
00800929 call     dword ptr [eax + 0xe0]
0080092f cmp      eax, edi
00800931 jge      0x80093f
00800933 push     0xbd8358
00800938 push     esi
00800939 push     eax
0080093a call     0xa5fdf2
0080093f mov      ecx, dword ptr [ebp - 0x18]
00800942 lea      eax, [ebp - 0x18]
00800945 push     eax
00800946 call     0x426604
0080094b mov      eax, dword ptr [eax]
0080094d push     1
0080094f push     eax
00800950 lea      ecx, [ebp - 0x2c]
00800953 call     0x410fdf
00800958 mov      eax, dword ptr [ebp + 8]
0080095b cmp      eax, edi
0080095d mov      byte ptr [ebp - 4], 0x23
00800961 jne      0x800970
00800963 push     0x80004003
00800968 call     0xa5fde4
0080096d mov      eax, dword ptr [ebp + 8]
00800970 mov      ecx, dword ptr [eax]
00800972 sub      esp, 0x10
00800975 mov      edi, esp
00800977 lea      esi, [ebp - 0x2c]
0080097a movsd    dword ptr es:[edi], dword ptr [esi]
0080097b movsd    dword ptr es:[edi], dword ptr [esi]
0080097c movsd    dword ptr es:[edi], dword ptr [esi]
0080097d push     eax
0080097e mov      dword ptr [ebp - 0x14], eax
00800981 movsd    dword ptr es:[edi], dword ptr [esi]
00800982 call     dword ptr [ecx + 0x64]
00800985 test     eax, eax
00800987 jge      0x800997
00800989 push     0xbd8348
0080098e push     dword ptr [ebp - 0x14]
00800991 push     eax
00800992 call     0xa5fdf2
00800997 lea      eax, [ebp - 0x2c]
0080099a push     eax
0080099b mov      byte ptr [ebp - 4], 0x22
0080099f call     0x40291d
008009a4 test     eax, eax
008009a6 pop      ecx
008009a7 jge      0x8009af
008009a9 push     eax
008009aa call     0xa5fde4
008009af lea      ecx, [ebp - 0x18]
008009b2 mov      byte ptr [ebp - 4], 0x1c
008009b6 call     0x4156c5
008009bb push     ebx
008009bc lea      ecx, [ebp - 0x3c]
008009bf call     0x402f85
008009c4 push     ebx
008009c5 lea      ecx, [ebp - 0x2c]
008009c8 mov      byte ptr [ebp - 4], 0x24
008009cc call     0x402f85
008009d1 cmp      dword ptr [ebp - 0x10], 0
008009d5 mov      byte ptr [ebp - 4], 0x25
008009d9 mov      esi, 0x80004003
008009de jne      0x8009f2
008009e0 push     esi
008009e1 call     0xa5fde4
008009e6 cmp      dword ptr [ebp - 0x10], 0
008009ea jne      0x8009f2
008009ec push     esi
008009ed call     0xa5fde4
008009f2 cmp      dword ptr [ebp + 8], 0
008009f6 mov      edi, dword ptr [ebp - 0x10]
008009f9 jne      0x800a01
008009fb push     esi
008009fc call     0xa5fde4
00800a01 mov      esi, dword ptr [ebp + 8]
00800a04 mov      ecx, dword ptr [ebp - 0x10]
00800a07 mov      dword ptr [ebp - 0x18], esi
00800a0a call     0x40f0c2
00800a0f mov      ecx, eax
00800a11 mov      eax, dword ptr [ebp + 0x14]
00800a14 sub      eax, ecx
00800a16 add      eax, 0x10
00800a19 mov      ecx, edi
00800a1b mov      dword ptr [ebp - 0x14], eax
00800a1e call     0x40f09b
00800a23 mov      ecx, eax
00800a25 mov      eax, dword ptr [ebp + 0x10]
00800a28 sub      esp, 0x10
00800a2b mov      edi, esp
00800a2d sub      eax, ecx
00800a2f mov      ecx, dword ptr [esi]
00800a31 lea      esi, [ebp - 0x3c]
00800a34 movsd    dword ptr es:[edi], dword ptr [esi]
00800a35 movsd    dword ptr es:[edi], dword ptr [esi]
00800a36 movsd    dword ptr es:[edi], dword ptr [esi]
00800a37 movsd    dword ptr es:[edi], dword ptr [esi]
00800a38 sub      esp, 0x10
00800a3b mov      edi, esp
00800a3d push     dword ptr [ebp - 0x14]
00800a40 lea      esi, [ebp - 0x2c]
00800a43 movsd    dword ptr es:[edi], dword ptr [esi]
00800a44 movsd    dword ptr es:[edi], dword ptr [esi]
00800a45 sub      eax, 0x10
00800a48 movsd    dword ptr es:[edi], dword ptr [esi]
00800a49 push     eax
00800a4a push     dword ptr [ebp - 0x18]
00800a4d movsd    dword ptr es:[edi], dword ptr [esi]
00800a4e call     dword ptr [ecx + 0x90]
00800a54 test     eax, eax
00800a56 jge      0x800a66
00800a58 push     0xbd8348
00800a5d push     dword ptr [ebp - 0x18]
00800a60 push     eax
00800a61 call     0xa5fdf2
00800a66 lea      eax, [ebp - 0x2c]
00800a69 push     eax
00800a6a mov      byte ptr [ebp - 4], 0x24
00800a6e call     0x40291d
00800a73 test     eax, eax
00800a75 pop      ecx
00800a76 jge      0x800a7e
00800a78 push     eax
00800a79 call     0xa5fde4
00800a7e lea      eax, [ebp - 0x3c]
00800a81 push     eax
00800a82 mov      byte ptr [ebp - 4], 0x1c
00800a86 call     0x40291d
00800a8b test     eax, eax
00800a8d pop      ecx
00800a8e jge      0x800a96
00800a90 push     eax
00800a91 call     0xa5fde4
00800a96 push     0x28
00800a98 mov      ecx, 0xbf0b00
00800a9d call     0x403065
00800aa2 mov      esi, eax
00800aa4 mov      dword ptr [ebp - 0x14], esi
00800aa7 test     esi, esi
00800aa9 mov      byte ptr [ebp - 4], 0x26
00800aad je       0x800ae1
00800aaf push     dword ptr [ebp + 8]
00800ab2 mov      ecx, esi
00800ab4 call     0x6ffda3
00800ab9 mov      eax, dword ptr [ebp - 0x40]
00800abc and      dword ptr [esi + 0x20], 0
00800ac0 mov      dword ptr [esi + 0x1c], eax
00800ac3 mov      eax, dword ptr [ebp - 0x1c]
00800ac6 lea      ecx, [eax - 4]
00800ac9 neg      ecx
00800acb sbb      ecx, ecx
00800acd and      ecx, eax
00800acf mov      dword ptr [esi + 0x18], 2
00800ad6 mov      dword ptr [esi + 0x24], ecx
00800ad9 mov      dword ptr [esi], 0xaf34d8
00800adf jmp      0x800ae6
00800ae1 mov      eax, dword ptr [ebp - 0x1c]
00800ae4 xor      esi, esi
00800ae6 lea      ecx, [eax - 4]
00800ae9 neg      ecx
00800aeb sbb      ecx, ecx
00800aed and      ecx, eax
00800aef push     esi
00800af0 push     ecx
00800af1 mov      ecx, dword ptr [0xbec20c]
00800af7 mov      byte ptr [ebp - 4], 0x1c
00800afb call     0x9e353d
00800b00 lea      eax, [ebp - 0x14]
00800b03 push     0x737
00800b08 push     eax
00800b09 call     0x79e805
00800b0e mov      ecx, eax
00800b10 call     0x406276
00800b15 push     dword ptr [eax]
00800b17 mov      byte ptr [ebp - 4], 0x27
00800b1b call     0x989588
00800b20 mov      eax, dword ptr [ebp - 0x14]
00800b23 test     eax, eax
00800b25 pop      ecx
00800b26 mov      byte ptr [ebp - 4], 0x1c
00800b2a je       0x800b36
00800b2c add      eax, -0xc
00800b2f push     eax
00800b30 call     0x4033a7
00800b35 pop      ecx
00800b36 lea      ecx, [ebp - 0x10]
00800b39 call     0x4156ab
00800b3e lea      ecx, [ebp + 8]
00800b41 mov      byte ptr [ebp - 4], 5
00800b45 call     0x4156c5
00800b4a xor      edi, edi
00800b4c push     dword ptr [ebp + 0x14]
00800b4f mov      eax, dword ptr [ebp - 0x1c]
00800b52 push     dword ptr [ebp + 0x10]
00800b55 lea      ecx, [eax - 4]
00800b58 mov      dword ptr [ebp - 0x18], ecx
00800b5b call     0x8011fa
00800b60 cmp      eax, 0xc9
00800b65 jne      0x800f4b
00800b6b mov      eax, dword ptr [0xbed5a0]
00800b70 cmp      dword ptr [eax + 0x3c4], edi
00800b76 je       0x800f4b
00800b7c push     ebx
00800b7d lea      ecx, [ebp - 0x3c]
00800b80 call     0x402f85
00800b85 push     3
00800b87 push     edi
00800b88 lea      ecx, [ebp - 0x2c]
00800b8b mov      byte ptr [ebp - 4], 0x28
00800b8f call     0x402fab
00800b94 cmp      dword ptr [0xbf14ec], edi
00800b9a mov      byte ptr [ebp - 4], 0x29
00800b9e mov      esi, 0x80004003
00800ba3 jne      0x800bab
00800ba5 push     esi
00800ba6 call     0xa5fde4
00800bab mov      ecx, dword ptr [0xbf14ec]
00800bb1 lea      eax, [ebp - 0x3c]
00800bb4 push     eax
00800bb5 lea      eax, [ebp - 0x2c]
00800bb8 push     eax
00800bb9 push     edi
00800bba push     edi
00800bbb push     edi
00800bbc push     edi
00800bbd push     edi
00800bbe lea      eax, [ebp + 8]
00800bc1 push     eax
00800bc2 call     0x426c7e
00800bc7 lea      eax, [ebp - 0x2c]
00800bca push     eax
00800bcb mov      byte ptr [ebp - 4], 0x2c
00800bcf call     0x40291d
00800bd4 cmp      eax, edi
00800bd6 pop      ecx
00800bd7 jge      0x800bdf
00800bd9 push     eax
00800bda call     0xa5fde4
00800bdf lea      eax, [ebp - 0x3c]
00800be2 push     eax
00800be3 mov      byte ptr [ebp - 4], 0x2b
00800be7 call     0x40291d
00800bec cmp      eax, edi
00800bee pop      ecx
00800bef jge      0x800bf7
00800bf1 push     eax
00800bf2 call     0xa5fde4
00800bf7 mov      eax, dword ptr [0xbed5a0]
00800bfc mov      eax, dword ptr [eax + 0x3c4]
00800c02 mov      ecx, dword ptr [0xbe78d8]
00800c08 push     edi
00800c09 push     edi
00800c0a push     eax
00800c0b lea      eax, [ebp - 0x10]
00800c0e push     eax
00800c0f call     0x5d3bd8
00800c14 push     ebx
00800c15 lea      ecx, [ebp - 0x50]
00800c18 mov      byte ptr [ebp - 4], 0x2d
00800c1c call     0x402f85
00800c21 push     ebx
00800c22 lea      ecx, [ebp - 0x60]
00800c25 mov      byte ptr [ebp - 4], 0x2e
00800c29 call     0x402f85
00800c2e push     ebx
00800c2f lea      ecx, [ebp - 0x80]
00800c32 mov      byte ptr [ebp - 4], 0x2f
00800c36 call     0x402f85
00800c3b push     ebx
00800c3c lea      ecx, [ebp - 0x3c]
00800c3f mov      byte ptr [ebp - 4], 0x30
00800c43 call     0x402f85
00800c48 push     ebx
00800c49 lea      ecx, [ebp - 0x2c]
00800c4c mov      byte ptr [ebp - 4], 0x31
00800c50 call     0x402f85
00800c55 cmp      dword ptr [ebp + 8], edi
00800c58 mov      byte ptr [ebp - 4], 0x32
00800c5c jne      0x800c64
00800c5e push     esi
00800c5f call     0xa5fde4
00800c64 mov      ecx, dword ptr [ebp + 8]
00800c67 lea      eax, [ebp - 0x50]
00800c6a push     eax
00800c6b lea      eax, [ebp - 0x60]
00800c6e push     eax
00800c6f lea      eax, [ebp - 0x80]
00800c72 push     eax
00800c73 lea      eax, [ebp - 0x3c]
00800c76 push     eax
00800c77 lea      eax, [ebp - 0x2c]
00800c7a push     eax
00800c7b push     dword ptr [ebp - 0x10]
00800c7e lea      eax, [ebp - 0x94]
00800c84 push     eax
00800c85 call     0x426bab
00800c8a lea      eax, [ebp - 0x94]
00800c90 push     eax
00800c91 call     0x40291d
00800c96 cmp      eax, edi
00800c98 pop      ecx
00800c99 jge      0x800ca1
00800c9b push     eax
00800c9c call     0xa5fde4
00800ca1 lea      eax, [ebp - 0x2c]
00800ca4 push     eax
00800ca5 mov      byte ptr [ebp - 4], 0x31
00800ca9 call     0x40291d
00800cae cmp      eax, edi
00800cb0 pop      ecx
00800cb1 jge      0x800cb9
00800cb3 push     eax
00800cb4 call     0xa5fde4
00800cb9 lea      eax, [ebp - 0x3c]
00800cbc push     eax
00800cbd mov      byte ptr [ebp - 4], 0x30
00800cc1 call     0x40291d
00800cc6 cmp      eax, edi
00800cc8 pop      ecx
00800cc9 jge      0x800cd1
00800ccb push     eax
00800ccc call     0xa5fde4
00800cd1 lea      eax, [ebp - 0x80]
00800cd4 push     eax
00800cd5 mov      byte ptr [ebp - 4], 0x2f
00800cd9 call     0x40291d
00800cde cmp      eax, edi
00800ce0 pop      ecx
00800ce1 jge      0x800ce9
00800ce3 push     eax
00800ce4 call     0xa5fde4
00800ce9 lea      eax, [ebp - 0x60]
00800cec push     eax
00800ced mov      byte ptr [ebp - 4], 0x2e
00800cf1 call     0x40291d
00800cf6 cmp      eax, edi
00800cf8 pop      ecx
00800cf9 jge      0x800d01
00800cfb push     eax
00800cfc call     0xa5fde4
00800d01 lea      eax, [ebp - 0x50]
00800d04 push     eax
00800d05 mov      byte ptr [ebp - 4], 0x2d
00800d09 call     0x40291d
00800d0e cmp      eax, edi
00800d10 pop      ecx
00800d11 jge      0x800d19
00800d13 push     eax
00800d14 call     0xa5fde4
00800d19 cmp      dword ptr [ebp + 8], edi
00800d1c jne      0x800d24
00800d1e push     esi
00800d1f call     0xa5fde4
00800d24 mov      esi, dword ptr [ebp + 8]
00800d27 mov      eax, dword ptr [esi]
00800d29 push     0x80ffffff
00800d2e push     esi
00800d2f call     dword ptr [eax + 0xe0]
00800d35 cmp      eax, edi
00800d37 jge      0x800d45
00800d39 push     0xbd8358
00800d3e push     esi
00800d3f push     eax
00800d40 call     0xa5fdf2
00800d45 mov      ecx, dword ptr [ebp - 0x18]
00800d48 lea      eax, [ebp - 0x18]
00800d4b push     eax
00800d4c call     0x426604
00800d51 mov      eax, dword ptr [eax]
00800d53 push     1
00800d55 push     eax
00800d56 lea      ecx, [ebp - 0x2c]
00800d59 call     0x410fdf
00800d5e mov      eax, dword ptr [ebp + 8]
00800d61 cmp      eax, edi
00800d63 mov      byte ptr [ebp - 4], 0x34
00800d67 jne      0x800d76
00800d69 push     0x80004003
00800d6e call     0xa5fde4
00800d73 mov      eax, dword ptr [ebp + 8]
00800d76 mov      ecx, dword ptr [eax]
00800d78 sub      esp, 0x10
00800d7b mov      edi, esp
00800d7d lea      esi, [ebp - 0x2c]
00800d80 movsd    dword ptr es:[edi], dword ptr [esi]
00800d81 movsd    dword ptr es:[edi], dword ptr [esi]
00800d82 movsd    dword ptr es:[edi], dword ptr [esi]
00800d83 push     eax
00800d84 mov      dword ptr [ebp - 0x14], eax
00800d87 movsd    dword ptr es:[edi], dword ptr [esi]
00800d88 call     dword ptr [ecx + 0x64]
00800d8b test     eax, eax
00800d8d jge      0x800d9d
00800d8f push     0xbd8348
00800d94 push     dword ptr [ebp - 0x14]
00800d97 push     eax
00800d98 call     0xa5fdf2
00800d9d lea      eax, [ebp - 0x2c]
00800da0 push     eax
00800da1 mov      byte ptr [ebp - 4], 0x33
00800da5 call     0x40291d
00800daa test     eax, eax
00800dac pop      ecx
00800dad jge      0x800db5
00800daf push     eax
00800db0 call     0xa5fde4
00800db5 lea      ecx, [ebp - 0x18]
00800db8 mov      byte ptr [ebp - 4], 0x2d
00800dbc call     0x4156c5
00800dc1 push     ebx
00800dc2 lea      ecx, [ebp - 0x3c]
00800dc5 call     0x402f85
00800dca push     ebx
00800dcb lea      ecx, [ebp - 0x2c]
00800dce mov      byte ptr [ebp - 4], 0x35
00800dd2 call     0x402f85
00800dd7 cmp      dword ptr [ebp - 0x10], 0
00800ddb mov      byte ptr [ebp - 4], 0x36
00800ddf mov      esi, 0x80004003
00800de4 jne      0x800df8
00800de6 push     esi
00800de7 call     0xa5fde4
00800dec cmp      dword ptr [ebp - 0x10], 0
00800df0 jne      0x800df8
00800df2 push     esi
00800df3 call     0xa5fde4
00800df8 cmp      dword ptr [ebp + 8], 0
00800dfc mov      edi, dword ptr [ebp - 0x10]
00800dff jne      0x800e07
00800e01 push     esi
00800e02 call     0xa5fde4
00800e07 mov      ecx, dword ptr [ebp - 0x10]
00800e0a mov      ebx, dword ptr [ebp + 8]
00800e0d call     0x40f0c2
00800e12 mov      ecx, eax
00800e14 mov      eax, dword ptr [ebp + 0x14]
00800e17 sub      eax, ecx
00800e19 add      eax, 0x10
00800e1c mov      ecx, edi
00800e1e mov      dword ptr [ebp + 0x14], eax
00800e21 call     0x40f09b
00800e26 sub      esp, 0x10
00800e29 mov      edi, esp
00800e2b lea      esi, [ebp - 0x3c]
00800e2e movsd    dword ptr es:[edi], dword ptr [esi]
00800e2f movsd    dword ptr es:[edi], dword ptr [esi]
00800e30 movsd    dword ptr es:[edi], dword ptr [esi]
00800e31 movsd    dword ptr es:[edi], dword ptr [esi]
00800e32 sub      esp, 0x10
00800e35 mov      edi, esp
00800e37 push     dword ptr [ebp + 0x14]
00800e3a lea      esi, [ebp - 0x2c]
00800e3d movsd    dword ptr es:[edi], dword ptr [esi]
00800e3e mov      ecx, eax
00800e40 mov      eax, dword ptr [ebp + 0x10]
00800e43 movsd    dword ptr es:[edi], dword ptr [esi]
00800e44 sub      eax, ecx
00800e46 mov      ecx, dword ptr [ebx]
00800e48 sub      eax, 0x10
00800e4b movsd    dword ptr es:[edi], dword ptr [esi]
00800e4c push     eax
00800e4d push     ebx
00800e4e movsd    dword ptr es:[edi], dword ptr [esi]
00800e4f call     dword ptr [ecx + 0x90]
00800e55 test     eax, eax
00800e57 jge      0x800e65
00800e59 push     0xbd8348
00800e5e push     ebx
00800e5f push     eax
00800e60 call     0xa5fdf2
00800e65 lea      eax, [ebp - 0x2c]
00800e68 push     eax
00800e69 mov      byte ptr [ebp - 4], 0x35
00800e6d call     0x40291d
00800e72 test     eax, eax
00800e74 pop      ecx
00800e75 jge      0x800e7d
00800e77 push     eax
00800e78 call     0xa5fde4
00800e7d lea      eax, [ebp - 0x3c]
00800e80 push     eax
00800e81 mov      byte ptr [ebp - 4], 0x2d
00800e85 call     0x40291d
00800e8a test     eax, eax
00800e8c pop      ecx
00800e8d jge      0x800e95
00800e8f push     eax
00800e90 call     0xa5fde4
00800e95 push     0x28
00800e97 mov      ecx, 0xbf0b00
00800e9c call     0x403065
00800ea1 mov      esi, eax
00800ea3 mov      dword ptr [ebp + 0x14], esi
00800ea6 test     esi, esi
00800ea8 mov      byte ptr [ebp - 4], 0x37
00800eac je       0x800ee0
00800eae push     dword ptr [ebp + 8]
00800eb1 mov      ecx, esi
00800eb3 call     0x6ffda3
00800eb8 mov      eax, dword ptr [ebp - 0x40]
00800ebb and      dword ptr [esi + 0x20], 0
00800ebf mov      dword ptr [esi + 0x1c], eax
00800ec2 mov      eax, dword ptr [ebp - 0x1c]
00800ec5 lea      ecx, [eax - 4]
00800ec8 neg      ecx
00800eca sbb      ecx, ecx
00800ecc and      ecx, eax
00800ece mov      dword ptr [esi + 0x18], 2
00800ed5 mov      dword ptr [esi + 0x24], ecx
00800ed8 mov      dword ptr [esi], 0xaf34d8
00800ede jmp      0x800ee5
00800ee0 mov      eax, dword ptr [ebp - 0x1c]
00800ee3 xor      esi, esi
00800ee5 lea      ecx, [eax - 4]
00800ee8 neg      ecx
00800eea sbb      ecx, ecx
00800eec and      ecx, eax
00800eee push     esi
00800eef push     ecx
00800ef0 mov      ecx, dword ptr [0xbec20c]
00800ef6 mov      byte ptr [ebp - 4], 0x2d
00800efa call     0x9e353d
00800eff lea      eax, [ebp + 0x14]
00800f02 push     0x737
00800f07 push     eax
00800f08 call     0x79e805
00800f0d mov      ecx, eax
00800f0f call     0x406276
00800f14 push     dword ptr [eax]
00800f16 mov      byte ptr [ebp - 4], 0x38
00800f1a call     0x989588
00800f1f mov      eax, dword ptr [ebp + 0x14]
00800f22 test     eax, eax
00800f24 pop      ecx
00800f25 mov      byte ptr [ebp - 4], 0x2d
00800f29 je       0x800f35
00800f2b add      eax, -0xc
00800f2e push     eax
00800f2f call     0x4033a7
00800f34 pop      ecx
00800f35 lea      ecx, [ebp - 0x10]
00800f38 call     0x4156ab
00800f3d lea      ecx, [ebp + 8]
00800f40 mov      byte ptr [ebp - 4], 5
00800f44 call     0x4156c5
00800f49 xor      edi, edi
00800f4b mov      eax, dword ptr [ebp - 0x1c]
00800f4e lea      ecx, [eax + 0x68]
00800f51 call     0x8e6e23
00800f56 mov      eax, dword ptr [ebp - 0x84]
00800f5c or       dword ptr [ebp - 4], 0xffffffff
00800f60 cmp      eax, edi
00800f62 je       0x800f6a
00800f64 mov      ecx, dword ptr [eax]
00800f66 push     eax
00800f67 call     dword ptr [ecx + 8]
00800f6a mov      ecx, dword ptr [ebp - 0xc]
00800f6d pop      edi
00800f6e pop      esi
00800f6f mov      dword ptr fs:[0], ecx
00800f76 pop      ebx
00800f77 leave    
00800f78 ret      0x10
