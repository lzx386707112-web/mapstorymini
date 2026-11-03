SHA256 1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2
# function 0x59e771: 327 instructions, 0x59e771..0x59eb3e
0059e771 mov      eax, 0xa90654
0059e776 call     0xa60b98
0059e77b sub      esp, 0x5c
0059e77e push     ebx
0059e77f push     esi
0059e780 mov      esi, ecx
0059e782 mov      ecx, dword ptr [0xbe7918]
0059e788 push     edi
0059e789 call     0xa04920
0059e78e lea      edx, [esi + 0x1c]
0059e791 xor      ecx, ecx
0059e793 call     0x4165b1
0059e798 lea      edx, [esi + 0x28]
0059e79b xor      ecx, ecx
0059e79d mov      dword ptr [esi + 0x24], eax
0059e7a0 call     0x4165b1
0059e7a5 xor      edi, edi
0059e7a7 push     1
0059e7a9 pop      ebx
0059e7aa mov      dword ptr [esi + 0x30], eax
0059e7ad mov      dword ptr [esi + 0x68], ebx
0059e7b0 mov      dword ptr [esi + 0x6c], edi
0059e7b3 mov      dword ptr [esi + 0x70], edi
0059e7b6 mov      dword ptr [esi + 0x58], edi
0059e7b9 mov      dword ptr [esi + 0x5c], edi
0059e7bc mov      dword ptr [esi + 0x1e80], edi
0059e7c2 mov      dword ptr [esi + 0x1e84], edi
0059e7c8 mov      dword ptr [esi + 0x1e88], edi
0059e7ce mov      dword ptr [esi + 0x1e8c], edi
0059e7d4 mov      eax, dword ptr [esi + 0x1e90]
0059e7da cmp      eax, edi
0059e7dc je       0x59e7ee
0059e7de add      eax, -0xc
0059e7e1 push     eax
0059e7e2 call     0x428d13
0059e7e7 pop      ecx
0059e7e8 mov      dword ptr [esi + 0x1e90], edi
0059e7ee push     0xbf6300
0059e7f3 lea      ecx, [ebp - 0x48]
0059e7f6 mov      dword ptr [esi + 0x1e94], ebx
0059e7fc mov      dword ptr [esi + 0x60], ebx
0059e7ff mov      dword ptr [esi + 0x64], edi
0059e802 call     0x402f85
0059e807 push     0xbf6300
0059e80c lea      ecx, [ebp - 0x38]
0059e80f mov      dword ptr [ebp - 4], edi
0059e812 call     0x402f85
0059e817 push     edi
0059e818 push     edi
0059e819 lea      eax, [ebp - 0x48]
0059e81c push     eax
0059e81d lea      eax, [ebp - 0x38]
0059e820 push     eax
0059e821 push     ecx
0059e822 mov      eax, esp
0059e824 mov      dword ptr [ebp - 0x18], esp
0059e827 push     0x126d
0059e82c push     eax
0059e82d mov      byte ptr [ebp - 4], bl
0059e830 call     0x79e805
0059e835 mov      ecx, eax
0059e837 call     0x406292
0059e83c lea      eax, [ebp - 0x68]
0059e83f push     eax
0059e840 mov      byte ptr [ebp - 4], bl
0059e843 call     0x406472
0059e848 mov      ecx, eax
0059e84a call     0x402680
0059e84f mov      ecx, eax
0059e851 call     0x403a93
0059e856 mov      ecx, eax
0059e858 mov      byte ptr [ebp - 4], 3
0059e85c call     0x4032b2
0059e861 cmp      eax, edi
0059e863 mov      dword ptr [ebp - 0x14], eax
0059e866 mov      dword ptr [ebp - 0x10], edi
0059e869 je       0x59e888
0059e86b lea      eax, [ebp - 0x14]
0059e86e push     eax
0059e86f lea      ecx, [ebp - 0x10]
0059e872 call     0x41e527
0059e877 cmp      eax, edi
0059e879 jge      0x59e888
0059e87b cmp      eax, 0x80004002
0059e880 je       0x59e888
0059e882 push     eax
0059e883 call     0xa5fde4
0059e888 lea      eax, [ebp - 0x68]
0059e88b push     eax
0059e88c mov      byte ptr [ebp - 4], 7
0059e890 call     0x40291d
0059e895 cmp      eax, edi
0059e897 pop      ecx
0059e898 jge      0x59e8a0
0059e89a push     eax
0059e89b call     0xa5fde4
0059e8a0 lea      eax, [ebp - 0x38]
0059e8a3 push     eax
0059e8a4 mov      byte ptr [ebp - 4], 6
0059e8a8 call     0x40291d
0059e8ad cmp      eax, edi
0059e8af pop      ecx
0059e8b0 jge      0x59e8b8
0059e8b2 push     eax
0059e8b3 call     0xa5fde4
0059e8b8 lea      eax, [ebp - 0x48]
0059e8bb push     eax
0059e8bc mov      byte ptr [ebp - 4], 5
0059e8c0 call     0x40291d
0059e8c5 cmp      eax, edi
0059e8c7 pop      ecx
0059e8c8 jge      0x59e8d0
0059e8ca push     eax
0059e8cb call     0xa5fde4
0059e8d0 lea      eax, [ebp - 0x28]
0059e8d3 push     eax
0059e8d4 call     dword ptr [0xaf0268]
0059e8da lea      eax, [ebp - 0x28]
0059e8dd push     0xbf6300
0059e8e2 push     eax
0059e8e3 call     0x402b8c
0059e8e8 cmp      eax, edi
0059e8ea pop      ecx
0059e8eb pop      ecx
0059e8ec jge      0x59e8f4
0059e8ee push     eax
0059e8ef call     0xa5fde4
0059e8f4 push     ebx
0059e8f5 push     dword ptr [ebp - 0x10]
0059e8f8 lea      ecx, [ebp - 0x58]
0059e8fb call     0x410fdf
0059e900 cmp      dword ptr [0xbf14ec], edi
0059e906 mov      byte ptr [ebp - 4], 9
0059e90a jne      0x59e916
0059e90c push     0x80004003
0059e911 call     0xa5fde4
0059e916 mov      ecx, dword ptr [0xbf14ec]
0059e91c lea      eax, [ebp - 0x28]
0059e91f push     eax
0059e920 lea      eax, [ebp - 0x58]
0059e923 push     eax
0059e924 push     edi
0059e925 push     edi
0059e926 push     edi
0059e927 push     edi
0059e928 push     edi
0059e929 lea      eax, [ebp - 0x18]
0059e92c push     eax
0059e92d call     0x426c7e
0059e932 push     dword ptr [eax]
0059e934 lea      ecx, [esi + 0x74]
0059e937 call     0x428712
0059e93c mov      eax, dword ptr [ebp - 0x18]
0059e93f cmp      eax, edi
0059e941 je       0x59e949
0059e943 mov      ecx, dword ptr [eax]
0059e945 push     eax
0059e946 call     dword ptr [ecx + 8]
0059e949 lea      eax, [ebp - 0x58]
0059e94c push     eax
0059e94d mov      byte ptr [ebp - 4], 8
0059e951 call     0x40291d
0059e956 cmp      eax, edi
0059e958 pop      ecx
0059e959 jge      0x59e961
0059e95b push     eax
0059e95c call     0xa5fde4
0059e961 lea      eax, [ebp - 0x28]
0059e964 push     eax
0059e965 mov      byte ptr [ebp - 4], 5
0059e969 call     0x40291d
0059e96e cmp      eax, edi
0059e970 pop      ecx
0059e971 jge      0x59e979
0059e973 push     eax
0059e974 call     0xa5fde4
0059e979 cmp      dword ptr [esi + 0x74], edi
0059e97c jne      0x59e988
0059e97e push     0x80004003
0059e983 call     0xa5fde4
0059e988 mov      eax, dword ptr [esi + 0x74]
0059e98b mov      ecx, dword ptr [eax]
0059e98d push     -1
0059e98f push     eax
0059e990 mov      dword ptr [ebp - 0x18], eax
0059e993 call     dword ptr [ecx + 0xb4]
0059e999 cmp      eax, edi
0059e99b jge      0x59e9ab
0059e99d push     0xbd8358
0059e9a2 push     dword ptr [ebp - 0x18]
0059e9a5 push     eax
0059e9a6 call     0xa5fdf2
0059e9ab cmp      dword ptr [esi + 0x74], edi
0059e9ae jne      0x59e9ba
0059e9b0 push     0x80004003
0059e9b5 call     0xa5fde4
0059e9ba mov      eax, dword ptr [esi + 0x74]
0059e9bd mov      ecx, dword ptr [eax]
0059e9bf push     -1
0059e9c1 push     eax
0059e9c2 mov      dword ptr [ebp - 0x18], eax
0059e9c5 call     dword ptr [ecx + 0xe0]
0059e9cb cmp      eax, edi
0059e9cd jge      0x59e9dd
0059e9cf push     0xbd8358
0059e9d4 push     dword ptr [ebp - 0x18]
0059e9d7 push     eax
0059e9d8 call     0xa5fdf2
0059e9dd push     ebx
0059e9de push     esi
0059e9df push     ebx
0059e9e0 push     edi
0059e9e1 push     0xc8
0059e9e6 push     0x100
0059e9eb push     edi
0059e9ec push     edi
0059e9ed lea      ecx, [esi + 0x78]
0059e9f0 call     0x9de4d2
0059e9f5 push     ebx
0059e9f6 push     esi
0059e9f7 push     ebx
0059e9f8 push     edi
0059e9f9 push     0x6e
0059e9fb push     0x100
0059ea00 push     0xc8
0059ea05 push     edi
0059ea06 lea      ecx, [esi + 0x46c]
0059ea0c call     0x9de4d2
0059ea11 push     ebx
0059ea12 push     esi
0059ea13 push     ebx
0059ea14 push     edi
0059ea15 push     0x6c
0059ea17 push     0x100
0059ea1c push     0x136
0059ea21 push     edi
0059ea22 lea      ecx, [esi + 0xa08]
0059ea28 call     0x9de4d2
0059ea2d push     ebx
0059ea2e push     esi
0059ea2f push     ebx
0059ea30 push     edi
0059ea31 push     0xb4
0059ea36 push     0x100
0059ea3b push     0x1a2
0059ea40 push     edi
0059ea41 lea      ecx, [esi + 0xf9c]
0059ea47 call     0x9de4d2
0059ea4c push     ebx
0059ea4d push     esi
0059ea4e push     ebx
0059ea4f push     0xa
0059ea51 push     0x4e
0059ea53 push     0x1fd
0059ea58 push     0x11
0059ea5a push     0x110
0059ea5f lea      ecx, [esi + 0x1548]
0059ea65 call     0x9de4d2
0059ea6a push     ebx
0059ea6b push     esi
0059ea6c push     ebx
0059ea6d push     edi
0059ea6e push     0x30
0059ea70 push     0x1fd
0059ea75 push     0x62
0059ea77 push     0x111
0059ea7c lea      ecx, [esi + 0x1608]
0059ea82 call     0x9de4d2
0059ea87 push     ebx
0059ea88 push     esi
0059ea89 push     ebx
0059ea8a push     edi
0059ea8b push     0x16d
0059ea90 push     0x1fd
0059ea95 push     0x91
0059ea9a push     0x111
0059ea9f lea      ecx, [esi + 0x16e8]
0059eaa5 call     0x9de4d2
0059eaaa push     ebx
0059eaab push     esi
0059eaac push     ebx
0059eaad push     edi
0059eaae push     0x38
0059eab0 push     0x221
0059eab5 push     0x213
0059eaba push     0xff
0059eabf lea      ecx, [esi + 0x1dd0]
0059eac5 call     0x9de4d2
0059eaca mov      ecx, dword ptr [0xbe78d8]
0059ead0 call     0x5d55d4
0059ead5 mov      ecx, dword ptr [0xbec33c]
0059eadb push     ebx
0059eadc call     0x59a338
0059eae1 lea      eax, [ebp - 0x18]
0059eae4 push     0x4e1
0059eae9 push     eax
0059eaea call     0x79e805
0059eaef mov      ecx, eax
0059eaf1 call     0x406276
0059eaf6 mov      eax, dword ptr [eax]
0059eaf8 mov      ecx, dword ptr [0xbebf94]
0059eafe push     edi
0059eaff push     edi
0059eb00 push     edi
0059eb01 push     ebx
0059eb02 push     eax
0059eb03 mov      byte ptr [ebp - 4], 0xa
0059eb07 call     0x43f301
0059eb0c lea      ecx, [ebp - 0x18]
0059eb0f mov      byte ptr [ebp - 4], 5
0059eb13 call     0x40265e
0059eb18 mov      ecx, esi
0059eb1a call     0x776b64
0059eb1f mov      eax, dword ptr [ebp - 0x10]
0059eb22 or       dword ptr [ebp - 4], 0xffffffff
0059eb26 cmp      eax, edi
0059eb28 je       0x59eb30
0059eb2a mov      ecx, dword ptr [eax]
0059eb2c push     eax
0059eb2d call     dword ptr [ecx + 8]
0059eb30 mov      ecx, dword ptr [ebp - 0xc]
0059eb33 pop      edi
0059eb34 pop      esi
0059eb35 mov      dword ptr fs:[0], ecx
0059eb3c pop      ebx
0059eb3d leave    
0059eb3e ret      4
