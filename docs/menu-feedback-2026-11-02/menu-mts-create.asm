SHA256 1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2
# function 0x5b0b8c: 560 instructions, 0x5b0b8c..0x5b121f
005b0b8c mov      eax, 0xa9190a
005b0b91 call     0xa60b98
005b0b96 sub      esp, 0x10c
005b0b9c mov      eax, dword ptr [ebp + 8]
005b0b9f push     ebx
005b0ba0 push     esi
005b0ba1 push     edi
005b0ba2 mov      edi, ecx
005b0ba4 mov      ebx, 0xbf6300
005b0ba9 push     ebx
005b0baa lea      ecx, [ebp - 0x88]
005b0bb0 mov      dword ptr [ebp - 0x20], edi
005b0bb3 mov      dword ptr [edi + 0x3f0], eax
005b0bb9 call     0x402f85
005b0bbe xor      esi, esi
005b0bc0 push     ebx
005b0bc1 lea      ecx, [ebp - 0x68]
005b0bc4 mov      dword ptr [ebp - 4], esi
005b0bc7 call     0x402f85
005b0bcc push     esi
005b0bcd push     esi
005b0bce lea      eax, [ebp - 0x88]
005b0bd4 push     eax
005b0bd5 lea      eax, [ebp - 0x68]
005b0bd8 push     eax
005b0bd9 push     ecx
005b0bda mov      eax, esp
005b0bdc mov      dword ptr [ebp - 0x14], esp
005b0bdf push     0x1274
005b0be4 push     eax
005b0be5 mov      byte ptr [ebp - 4], 1
005b0be9 call     0x79e805
005b0bee mov      ecx, eax
005b0bf0 call     0x406292
005b0bf5 lea      eax, [ebp - 0xd8]
005b0bfb push     eax
005b0bfc mov      byte ptr [ebp - 4], 1
005b0c00 call     0x406472
005b0c05 mov      ecx, eax
005b0c07 call     0x402680
005b0c0c mov      ecx, eax
005b0c0e call     0x403a93
005b0c13 mov      ecx, eax
005b0c15 mov      byte ptr [ebp - 4], 3
005b0c19 call     0x4032b2
005b0c1e cmp      eax, esi
005b0c20 mov      dword ptr [ebp - 0x54], eax
005b0c23 mov      dword ptr [ebp - 0x1c], esi
005b0c26 je       0x5b0c45
005b0c28 lea      eax, [ebp - 0x54]
005b0c2b push     eax
005b0c2c lea      ecx, [ebp - 0x1c]
005b0c2f call     0x41e527
005b0c34 cmp      eax, esi
005b0c36 jge      0x5b0c45
005b0c38 cmp      eax, 0x80004002
005b0c3d je       0x5b0c45
005b0c3f push     eax
005b0c40 call     0xa5fde4
005b0c45 lea      eax, [ebp - 0xd8]
005b0c4b push     eax
005b0c4c mov      byte ptr [ebp - 4], 7
005b0c50 call     0x40291d
005b0c55 cmp      eax, esi
005b0c57 pop      ecx
005b0c58 jge      0x5b0c60
005b0c5a push     eax
005b0c5b call     0xa5fde4
005b0c60 lea      eax, [ebp - 0x68]
005b0c63 push     eax
005b0c64 mov      byte ptr [ebp - 4], 6
005b0c68 call     0x40291d
005b0c6d cmp      eax, esi
005b0c6f pop      ecx
005b0c70 jge      0x5b0c78
005b0c72 push     eax
005b0c73 call     0xa5fde4
005b0c78 lea      eax, [ebp - 0x88]
005b0c7e push     eax
005b0c7f mov      byte ptr [ebp - 4], 5
005b0c83 call     0x40291d
005b0c88 cmp      eax, esi
005b0c8a pop      ecx
005b0c8b jge      0x5b0c93
005b0c8d push     eax
005b0c8e call     0xa5fde4
005b0c93 push     ebx
005b0c94 lea      ecx, [ebp - 0x3c]
005b0c97 call     0x402f85
005b0c9c push     1
005b0c9e push     dword ptr [ebp - 0x1c]
005b0ca1 lea      ecx, [ebp - 0x50]
005b0ca4 call     0x410fdf
005b0ca9 cmp      dword ptr [0xbf14ec], esi
005b0caf mov      byte ptr [ebp - 4], 9
005b0cb3 jne      0x5b0cbf
005b0cb5 push     0x80004003
005b0cba call     0xa5fde4
005b0cbf mov      ecx, dword ptr [0xbf14ec]
005b0cc5 lea      eax, [ebp - 0x3c]
005b0cc8 push     eax
005b0cc9 lea      eax, [ebp - 0x50]
005b0ccc push     eax
005b0ccd push     0xc0000000
005b0cd2 push     0x6d
005b0cd4 push     0xd5
005b0cd9 push     0x22
005b0cdb push     0x17
005b0cdd lea      eax, [ebp - 0x10]
005b0ce0 push     eax
005b0ce1 call     0x426c7e
005b0ce6 push     dword ptr [eax]
005b0ce8 lea      esi, [edi + 0x3ec]
005b0cee mov      ecx, esi
005b0cf0 call     0x428712
005b0cf5 lea      ecx, [ebp - 0x10]
005b0cf8 call     0x4156c5
005b0cfd lea      eax, [ebp - 0x50]
005b0d00 push     eax
005b0d01 mov      byte ptr [ebp - 4], 8
005b0d05 call     0x40291d
005b0d0a test     eax, eax
005b0d0c pop      ecx
005b0d0d jge      0x5b0d15
005b0d0f push     eax
005b0d10 call     0xa5fde4
005b0d15 lea      eax, [ebp - 0x3c]
005b0d18 push     eax
005b0d19 mov      byte ptr [ebp - 4], 5
005b0d1d call     0x40291d
005b0d22 test     eax, eax
005b0d24 pop      ecx
005b0d25 jge      0x5b0d2d
005b0d27 push     eax
005b0d28 call     0xa5fde4
005b0d2d lea      eax, [ebp - 0x24]
005b0d30 push     eax
005b0d31 mov      ecx, edi
005b0d33 call     0x426604
005b0d38 mov      eax, dword ptr [eax]
005b0d3a push     1
005b0d3c push     eax
005b0d3d lea      ecx, [ebp - 0x3c]
005b0d40 call     0x410fdf
005b0d45 cmp      dword ptr [esi], 0
005b0d48 mov      byte ptr [ebp - 4], 0xb
005b0d4c jne      0x5b0d58
005b0d4e push     0x80004003
005b0d53 call     0xa5fde4
005b0d58 mov      eax, dword ptr [esi]
005b0d5a mov      ecx, dword ptr [eax]
005b0d5c sub      esp, 0x10
005b0d5f mov      edi, esp
005b0d61 lea      esi, [ebp - 0x3c]
005b0d64 movsd    dword ptr es:[edi], dword ptr [esi]
005b0d65 movsd    dword ptr es:[edi], dword ptr [esi]
005b0d66 movsd    dword ptr es:[edi], dword ptr [esi]
005b0d67 push     eax
005b0d68 mov      dword ptr [ebp - 0x10], eax
005b0d6b movsd    dword ptr es:[edi], dword ptr [esi]
005b0d6c call     dword ptr [ecx + 0x64]
005b0d6f xor      edi, edi
005b0d71 cmp      eax, edi
005b0d73 jge      0x5b0d83
005b0d75 push     0xbd8348
005b0d7a push     dword ptr [ebp - 0x10]
005b0d7d push     eax
005b0d7e call     0xa5fdf2
005b0d83 lea      eax, [ebp - 0x3c]
005b0d86 push     eax
005b0d87 mov      byte ptr [ebp - 4], 0xa
005b0d8b call     0x40291d
005b0d90 cmp      eax, edi
005b0d92 pop      ecx
005b0d93 jge      0x5b0d9b
005b0d95 push     eax
005b0d96 call     0xa5fde4
005b0d9b lea      ecx, [ebp - 0x24]
005b0d9e mov      byte ptr [ebp - 4], 5
005b0da2 call     0x4156c5
005b0da7 mov      eax, dword ptr [ebp - 0x20]
005b0daa cmp      dword ptr [eax + 0x3ec], edi
005b0db0 lea      esi, [eax + 0x3ec]
005b0db6 jne      0x5b0dc2
005b0db8 push     0x80004003
005b0dbd call     0xa5fde4
005b0dc2 mov      esi, dword ptr [esi]
005b0dc4 mov      eax, dword ptr [esi]
005b0dc6 push     -1
005b0dc8 push     esi
005b0dc9 call     dword ptr [eax + 0xe0]
005b0dcf cmp      eax, edi
005b0dd1 jge      0x5b0ddf
005b0dd3 push     0xbd8358
005b0dd8 push     esi
005b0dd9 push     eax
005b0dda call     0xa5fdf2
005b0ddf push     ebx
005b0de0 lea      ecx, [ebp - 0xc8]
005b0de6 call     0x402f85
005b0deb push     ebx
005b0dec lea      ecx, [ebp - 0x3c]
005b0def mov      byte ptr [ebp - 4], 0xc
005b0df3 call     0x402f85
005b0df8 push     edi
005b0df9 push     edi
005b0dfa lea      eax, [ebp - 0xc8]
005b0e00 push     eax
005b0e01 lea      eax, [ebp - 0x3c]
005b0e04 push     eax
005b0e05 push     ecx
005b0e06 mov      eax, esp
005b0e08 mov      dword ptr [ebp - 0x14], esp
005b0e0b push     0x1272
005b0e10 push     eax
005b0e11 mov      byte ptr [ebp - 4], 0xd
005b0e15 call     0x79e805
005b0e1a mov      ecx, eax
005b0e1c call     0x406292
005b0e21 lea      eax, [ebp - 0x108]
005b0e27 push     eax
005b0e28 mov      byte ptr [ebp - 4], 0xd
005b0e2c call     0x406472
005b0e31 mov      ecx, eax
005b0e33 call     0x402680
005b0e38 mov      ecx, eax
005b0e3a call     0x403a93
005b0e3f mov      ecx, eax
005b0e41 mov      byte ptr [ebp - 4], 0xf
005b0e45 call     0x4032b2
005b0e4a cmp      eax, edi
005b0e4c mov      dword ptr [ebp - 0x24], eax
005b0e4f mov      dword ptr [ebp - 0x28], edi
005b0e52 je       0x5b0e75
005b0e54 lea      eax, [ebp - 0x24]
005b0e57 push     eax
005b0e58 lea      ecx, [ebp - 0x28]
005b0e5b call     0x4052ad
005b0e60 cmp      eax, edi
005b0e62 jge      0x5b0e75
005b0e64 mov      esi, 0x80004002
005b0e69 cmp      eax, esi
005b0e6b je       0x5b0e7a
005b0e6d push     eax
005b0e6e call     0xa5fde4
005b0e73 jmp      0x5b0e7a
005b0e75 mov      esi, 0x80004002
005b0e7a lea      eax, [ebp - 0x108]
005b0e80 push     eax
005b0e81 mov      byte ptr [ebp - 4], 0x13
005b0e85 call     0x40291d
005b0e8a cmp      eax, edi
005b0e8c pop      ecx
005b0e8d jge      0x5b0e95
005b0e8f push     eax
005b0e90 call     0xa5fde4
005b0e95 lea      eax, [ebp - 0x3c]
005b0e98 push     eax
005b0e99 mov      byte ptr [ebp - 4], 0x12
005b0e9d call     0x40291d
005b0ea2 cmp      eax, edi
005b0ea4 pop      ecx
005b0ea5 jge      0x5b0ead
005b0ea7 push     eax
005b0ea8 call     0xa5fde4
005b0ead lea      eax, [ebp - 0xc8]
005b0eb3 push     eax
005b0eb4 mov      byte ptr [ebp - 4], 0x11
005b0eb8 call     0x40291d
005b0ebd cmp      eax, edi
005b0ebf pop      ecx
005b0ec0 jge      0x5b0ec8
005b0ec2 push     eax
005b0ec3 call     0xa5fde4
005b0ec8 push     ebx
005b0ec9 lea      ecx, [ebp - 0x98]
005b0ecf call     0x402f85
005b0ed4 push     ebx
005b0ed5 lea      ecx, [ebp - 0xb8]
005b0edb mov      byte ptr [ebp - 4], 0x14
005b0edf call     0x402f85
005b0ee4 push     edi
005b0ee5 push     edi
005b0ee6 lea      eax, [ebp - 0x98]
005b0eec push     eax
005b0eed lea      eax, [ebp - 0xb8]
005b0ef3 push     eax
005b0ef4 push     ecx
005b0ef5 mov      eax, esp
005b0ef7 mov      dword ptr [ebp - 0x14], esp
005b0efa push     0x1273
005b0eff push     eax
005b0f00 mov      byte ptr [ebp - 4], 0x15
005b0f04 call     0x79e805
005b0f09 mov      ecx, eax
005b0f0b call     0x406292
005b0f10 lea      eax, [ebp - 0xe8]
005b0f16 push     eax
005b0f17 mov      byte ptr [ebp - 4], 0x15
005b0f1b call     0x406472
005b0f20 mov      ecx, eax
005b0f22 call     0x402680
005b0f27 mov      ecx, eax
005b0f29 call     0x403a93
005b0f2e mov      ecx, eax
005b0f30 mov      byte ptr [ebp - 4], 0x17
005b0f34 call     0x4032b2
005b0f39 cmp      eax, edi
005b0f3b mov      dword ptr [ebp - 0x10], eax
005b0f3e mov      dword ptr [ebp - 0x2c], edi
005b0f41 je       0x5b0f5d
005b0f43 lea      eax, [ebp - 0x10]
005b0f46 push     eax
005b0f47 lea      ecx, [ebp - 0x2c]
005b0f4a call     0x4052ad
005b0f4f cmp      eax, edi
005b0f51 jge      0x5b0f5d
005b0f53 cmp      eax, esi
005b0f55 je       0x5b0f5d
005b0f57 push     eax
005b0f58 call     0xa5fde4
005b0f5d lea      eax, [ebp - 0xe8]
005b0f63 push     eax
005b0f64 mov      byte ptr [ebp - 4], 0x1b
005b0f68 call     0x40291d
005b0f6d cmp      eax, edi
005b0f6f pop      ecx
005b0f70 jge      0x5b0f78
005b0f72 push     eax
005b0f73 call     0xa5fde4
005b0f78 lea      eax, [ebp - 0xb8]
005b0f7e push     eax
005b0f7f mov      byte ptr [ebp - 4], 0x1a
005b0f83 call     0x40291d
005b0f88 cmp      eax, edi
005b0f8a pop      ecx
005b0f8b jge      0x5b0f93
005b0f8d push     eax
005b0f8e call     0xa5fde4
005b0f93 lea      eax, [ebp - 0x98]
005b0f99 push     eax
005b0f9a mov      byte ptr [ebp - 4], 0x19
005b0f9e call     0x40291d
005b0fa3 cmp      eax, edi
005b0fa5 pop      ecx
005b0fa6 jge      0x5b0fae
005b0fa8 push     eax
005b0fa9 call     0xa5fde4
005b0fae push     ebx
005b0faf lea      ecx, [ebp - 0x78]
005b0fb2 call     0x402f85
005b0fb7 push     ebx
005b0fb8 lea      ecx, [ebp - 0xa8]
005b0fbe mov      byte ptr [ebp - 4], 0x1c
005b0fc2 call     0x402f85
005b0fc7 push     edi
005b0fc8 push     edi
005b0fc9 lea      eax, [ebp - 0x78]
005b0fcc push     eax
005b0fcd lea      eax, [ebp - 0xa8]
005b0fd3 push     eax
005b0fd4 push     ecx
005b0fd5 mov      eax, esp
005b0fd7 mov      dword ptr [ebp - 0x14], esp
005b0fda push     0x1282
005b0fdf push     eax
005b0fe0 mov      byte ptr [ebp - 4], 0x1d
005b0fe4 call     0x79e805
005b0fe9 mov      ecx, eax
005b0feb call     0x406292
005b0ff0 lea      eax, [ebp - 0xf8]
005b0ff6 push     eax
005b0ff7 mov      byte ptr [ebp - 4], 0x1d
005b0ffb call     0x406472
005b1000 mov      ecx, eax
005b1002 call     0x402680
005b1007 mov      ecx, eax
005b1009 call     0x403a93
005b100e mov      ecx, eax
005b1010 mov      byte ptr [ebp - 4], 0x1f
005b1014 call     0x4032b2
005b1019 cmp      eax, edi
005b101b mov      dword ptr [ebp - 0x58], eax
005b101e mov      dword ptr [ebp - 0x18], edi
005b1021 je       0x5b103d
005b1023 lea      eax, [ebp - 0x58]
005b1026 push     eax
005b1027 lea      ecx, [ebp - 0x18]
005b102a call     0x4052ad
005b102f cmp      eax, edi
005b1031 jge      0x5b103d
005b1033 cmp      eax, esi
005b1035 je       0x5b103d
005b1037 push     eax
005b1038 call     0xa5fde4
005b103d lea      eax, [ebp - 0xf8]
005b1043 push     eax
005b1044 mov      byte ptr [ebp - 4], 0x23
005b1048 call     0x40291d
005b104d cmp      eax, edi
005b104f pop      ecx
005b1050 jge      0x5b1058
005b1052 push     eax
005b1053 call     0xa5fde4
005b1058 lea      eax, [ebp - 0xa8]
005b105e push     eax
005b105f mov      byte ptr [ebp - 4], 0x22
005b1063 call     0x40291d
005b1068 cmp      eax, edi
005b106a pop      ecx
005b106b jge      0x5b1073
005b106d push     eax
005b106e call     0xa5fde4
005b1073 lea      eax, [ebp - 0x78]
005b1076 push     eax
005b1077 mov      byte ptr [ebp - 4], 0x21
005b107b call     0x40291d
005b1080 cmp      eax, edi
005b1082 pop      ecx
005b1083 jge      0x5b108b
005b1085 push     eax
005b1086 call     0xa5fde4
005b108b push     edi
005b108c push     edi
005b108d push     ecx
005b108e mov      eax, esp
005b1090 mov      dword ptr [ebp - 0x40], esp
005b1093 push     0x5ce
005b1098 push     eax
005b1099 call     0x79e805
005b109e mov      ecx, eax
005b10a0 call     0x406292
005b10a5 lea      eax, [ebp - 0x118]
005b10ab push     eax
005b10ac lea      ecx, [ebp - 0x18]
005b10af mov      byte ptr [ebp - 4], 0x21
005b10b3 call     0x40263b
005b10b8 mov      ecx, eax
005b10ba call     0x403935
005b10bf mov      ecx, eax
005b10c1 mov      byte ptr [ebp - 4], 0x25
005b10c5 call     0x4032b2
005b10ca cmp      eax, edi
005b10cc mov      dword ptr [ebp - 0x14], eax
005b10cf mov      dword ptr [ebp + 8], edi
005b10d2 je       0x5b10ee
005b10d4 lea      eax, [ebp - 0x14]
005b10d7 push     eax
005b10d8 lea      ecx, [ebp + 8]
005b10db call     0x4052ad
005b10e0 cmp      eax, edi
005b10e2 jge      0x5b10ee
005b10e4 cmp      eax, esi
005b10e6 je       0x5b10ee
005b10e8 push     eax
005b10e9 call     0xa5fde4
005b10ee lea      eax, [ebp - 0x118]
005b10f4 push     eax
005b10f5 mov      byte ptr [ebp - 4], 0x27
005b10f9 call     0x40291d
005b10fe cmp      eax, edi
005b1100 pop      ecx
005b1101 jge      0x5b1109
005b1103 push     eax
005b1104 call     0xa5fde4
005b1109 mov      esi, dword ptr [ebp - 0x20]
005b110c lea      ebx, [esi + 0x3dc]
005b1112 mov      ecx, ebx
005b1114 call     0x4b1f77
005b1119 push     ecx
005b111a mov      eax, esp
005b111c mov      dword ptr [ebp - 0x40], esp
005b111f mov      dword ptr [eax], edi
005b1121 push     ecx
005b1122 mov      ecx, esp
005b1124 mov      dword ptr [ebp - 0x20], esp
005b1127 push     edi
005b1128 mov      byte ptr [ebp - 4], 0x28
005b112c call     0x5b3c96
005b1131 push     ecx
005b1132 lea      eax, [ebp + 8]
005b1135 mov      ecx, esp
005b1137 mov      dword ptr [ebp - 0x44], esp
005b113a push     eax
005b113b call     0x4281c2
005b1140 mov      ecx, ebx
005b1142 mov      byte ptr [ebp - 4], 0x27
005b1146 call     0x4b1f66
005b114b mov      ecx, eax
005b114d call     0xa43e7b
005b1152 mov      ecx, dword ptr [0xbebfa0]
005b1158 call     0xa45b8c
005b115d mov      ecx, dword ptr [0xbe7918]
005b1163 lea      eax, [ebp - 0x48]
005b1166 push     eax
005b1167 call     0x425d0b
005b116c mov      eax, dword ptr [eax + 4]
005b116f lea      ecx, [eax + 0x287]
005b1175 push     ecx
005b1176 lea      ecx, [eax + 0xe7]
005b117c push     ecx
005b117d push     eax
005b117e lea      ecx, [esi + 0x6c]
005b1181 mov      byte ptr [ebp - 4], 0x2b
005b1185 call     0x4e72ad
005b118a cmp      dword ptr [ebp - 0x44], edi
005b118d mov      byte ptr [ebp - 4], 0x27
005b1191 je       0x5b119f
005b1193 push     edi
005b1194 lea      ecx, [ebp - 0x48]
005b1197 call     0x428c44
005b119c mov      dword ptr [ebp - 0x44], edi
005b119f lea      ecx, [esi + 0x3e4]
005b11a5 call     0x4b1faa
005b11aa mov      ecx, dword ptr [esi + 0x3e8]
005b11b0 call     0x9785bd
005b11b5 mov      ecx, esi
005b11b7 call     0x5b17af
005b11bc mov      eax, dword ptr [ebp + 8]
005b11bf cmp      eax, edi
005b11c1 mov      byte ptr [ebp - 4], 0x21
005b11c5 je       0x5b11cd
005b11c7 mov      ecx, dword ptr [eax]
005b11c9 push     eax
005b11ca call     dword ptr [ecx + 8]
005b11cd mov      eax, dword ptr [ebp - 0x18]
005b11d0 cmp      eax, edi
005b11d2 mov      byte ptr [ebp - 4], 0x19
005b11d6 je       0x5b11de
005b11d8 mov      ecx, dword ptr [eax]
005b11da push     eax
005b11db call     dword ptr [ecx + 8]
005b11de mov      eax, dword ptr [ebp - 0x2c]
005b11e1 cmp      eax, edi
005b11e3 mov      byte ptr [ebp - 4], 0x11
005b11e7 je       0x5b11ef
005b11e9 mov      ecx, dword ptr [eax]
005b11eb push     eax
005b11ec call     dword ptr [ecx + 8]
005b11ef mov      eax, dword ptr [ebp - 0x28]
005b11f2 cmp      eax, edi
005b11f4 mov      byte ptr [ebp - 4], 5
005b11f8 je       0x5b1200
005b11fa mov      ecx, dword ptr [eax]
005b11fc push     eax
005b11fd call     dword ptr [ecx + 8]
005b1200 mov      eax, dword ptr [ebp - 0x1c]
005b1203 or       dword ptr [ebp - 4], 0xffffffff
005b1207 cmp      eax, edi
005b1209 je       0x5b1211
005b120b mov      ecx, dword ptr [eax]
005b120d push     eax
005b120e call     dword ptr [ecx + 8]
005b1211 mov      ecx, dword ptr [ebp - 0xc]
005b1214 pop      edi
005b1215 pop      esi
005b1216 mov      dword ptr fs:[0], ecx
005b121d pop      ebx
005b121e leave    
005b121f ret      4
