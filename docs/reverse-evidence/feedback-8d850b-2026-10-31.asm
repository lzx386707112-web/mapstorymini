008d850b mov      eax, 0xad29c9
008d8510 call     0xa60b98
008d8515 sub      esp, 0xa4
008d851b mov      eax, dword ptr [0xbebf9c]
008d8520 mov      eax, dword ptr [eax + 0x80]
008d8526 push     ebx
008d8527 push     esi
008d8528 mov      esi, ecx
008d852a lea      ecx, [eax + eax*4]
008d852d xor      ebx, ebx
008d852f cmp      ecx, ebx
008d8531 push     edi
008d8532 mov      dword ptr [ebp - 0x18], esi
008d8535 je       0x8d8564
008d8537 mov      eax, dword ptr [ebp + 8]
008d853a imul     eax, eax, 0x64
008d853d cdq      
008d853e idiv     dword ptr [ebp + 0xc]
008d8541 cmp      eax, ecx
008d8543 jge      0x8d8564
008d8545 mov      eax, dword ptr [esi + 0xd18]
008d854b cmp      eax, dword ptr [ebp + 8]
008d854e lea      edi, [esi + 0xd18]
008d8554 jle      0x8d855d
008d8556 mov      ecx, esi
008d8558 call     0x8dea76
008d855d mov      eax, dword ptr [ebp + 8]
008d8560 mov      dword ptr [edi], eax
008d8562 jmp      0x8d8576
008d8564 mov      eax, ecx
008d8566 imul     eax, dword ptr [ebp + 0xc]
008d856a push     0x64
008d856c cdq      
008d856d pop      ecx
008d856e idiv     ecx
008d8570 mov      dword ptr [esi + 0xd18], eax
008d8576 mov      eax, dword ptr [0xbebf9c]
008d857b mov      eax, dword ptr [eax + 0x84]
008d8581 lea      ecx, [eax + eax*4]
008d8584 cmp      ecx, ebx
008d8586 je       0x8d85b5
008d8588 mov      eax, dword ptr [ebp + 0x10]
008d858b imul     eax, eax, 0x64
008d858e cdq      
008d858f idiv     dword ptr [ebp + 0x14]
008d8592 cmp      eax, ecx
008d8594 jge      0x8d85b5
008d8596 mov      eax, dword ptr [esi + 0xd1c]
008d859c cmp      eax, dword ptr [ebp + 0x10]
008d859f lea      edi, [esi + 0xd1c]
008d85a5 jle      0x8d85ae
008d85a7 mov      ecx, esi
008d85a9 call     0x8ded12
008d85ae mov      eax, dword ptr [ebp + 0x10]
008d85b1 mov      dword ptr [edi], eax
008d85b3 jmp      0x8d85c7
008d85b5 mov      eax, ecx
008d85b7 imul     eax, dword ptr [ebp + 0x14]
008d85bb push     0x64
008d85bd cdq      
008d85be pop      ecx
008d85bf idiv     ecx
008d85c1 mov      dword ptr [esi + 0xd1c], eax
008d85c7 cmp      dword ptr [esi + 0xbb8], ebx
008d85cd jne      0x8d85d9
008d85cf push     0x80004003
008d85d4 call     0xa5fde4
008d85d9 mov      ecx, dword ptr [esi + 0xbb8]
008d85df lea      eax, [ebp - 0x30]
008d85e2 push     eax
008d85e3 call     0x440c54
008d85e8 mov      edi, eax
008d85ea cmp      dword ptr [edi], ebx
008d85ec mov      dword ptr [ebp - 4], ebx
008d85ef jne      0x8d85fb
008d85f1 push     0x80004003
008d85f6 call     0xa5fde4
008d85fb mov      ecx, dword ptr [edi]
008d85fd call     0x425aef
008d8602 or       dword ptr [ebp - 4], 0xffffffff
008d8606 lea      ecx, [ebp - 0x30]
008d8609 mov      dword ptr [ebp - 0x24], eax
008d860c call     0x41569e
008d8611 cmp      dword ptr [esi + 0xbb8], ebx
008d8617 jne      0x8d8623
008d8619 push     0x80004003
008d861e call     0xa5fde4
008d8623 mov      ecx, dword ptr [esi + 0xbb8]
008d8629 lea      eax, [ebp - 0x28]
008d862c push     eax
008d862d call     0x440c54
008d8632 mov      edi, eax
008d8634 cmp      dword ptr [edi], ebx
008d8636 mov      dword ptr [ebp - 4], 1
008d863d jne      0x8d8649
008d863f push     0x80004003
008d8644 call     0xa5fde4
008d8649 mov      ecx, dword ptr [edi]
008d864b call     0x425b16
008d8650 or       dword ptr [ebp - 4], 0xffffffff
008d8654 lea      ecx, [ebp - 0x28]
008d8657 mov      dword ptr [ebp - 0x30], eax
008d865a call     0x41569e
008d865f cmp      dword ptr [esi + 0xbb8], ebx
008d8665 jne      0x8d8671
008d8667 push     0x80004003
008d866c call     0xa5fde4
008d8671 mov      ecx, dword ptr [esi + 0xbb8]
008d8677 lea      eax, [ebp - 0x28]
008d867a push     eax
008d867b call     0x440c54
008d8680 mov      edi, eax
008d8682 cmp      dword ptr [edi], ebx
008d8684 mov      dword ptr [ebp - 4], 2
008d868b jne      0x8d8697
008d868d push     0x80004003
008d8692 call     0xa5fde4
008d8697 mov      ecx, dword ptr [edi]
008d8699 lea      eax, [ebp - 0x84]
008d869f push     eax
008d86a0 call     0x441aa1
008d86a5 lea      ecx, [ebp - 0x28]
008d86a8 mov      byte ptr [ebp - 4], 4
008d86ac call     0x41569e
008d86b1 cmp      dword ptr [esi + 0xbb8], ebx
008d86b7 jne      0x8d86c3
008d86b9 push     0x80004003
008d86be call     0xa5fde4
008d86c3 mov      esi, dword ptr [esi + 0xbb8]
008d86c9 lea      eax, [ebp - 0x10]
008d86cc push     eax
008d86cd mov      ecx, esi
008d86cf call     0x440c54
008d86d4 mov      esi, eax
008d86d6 cmp      dword ptr [esi], ebx
008d86d8 mov      byte ptr [ebp - 4], 5
008d86dc jne      0x8d86e8
008d86de push     0x80004003
008d86e3 call     0xa5fde4
008d86e8 mov      eax, dword ptr [esi]
008d86ea mov      esi, 0xbf0b70
008d86ef lea      edi, [ebp - 0x54]
008d86f2 movsd    dword ptr es:[edi], dword ptr [esi]
008d86f3 movsd    dword ptr es:[edi], dword ptr [esi]
008d86f4 mov      ecx, dword ptr [eax]
008d86f6 movsd    dword ptr es:[edi], dword ptr [esi]
008d86f7 movsd    dword ptr es:[edi], dword ptr [esi]
008d86f8 sub      esp, 0x10
008d86fb mov      edi, esp
008d86fd lea      esi, [ebp - 0x54]
008d8700 movsd    dword ptr es:[edi], dword ptr [esi]
008d8701 movsd    dword ptr es:[edi], dword ptr [esi]
008d8702 movsd    dword ptr es:[edi], dword ptr [esi]
008d8703 push     eax
008d8704 mov      dword ptr [ebp - 0x14], eax
008d8707 movsd    dword ptr es:[edi], dword ptr [esi]
008d8708 call     dword ptr [ecx + 0x64]
008d870b cmp      eax, ebx
008d870d jge      0x8d871d
008d870f push     0xbd8348
008d8714 push     dword ptr [ebp - 0x14]
008d8717 push     eax
008d8718 call     0xa5fdf2
008d871d lea      ecx, [ebp - 0x10]
008d8720 mov      byte ptr [ebp - 4], 4
008d8724 call     0x41569e
008d8729 mov      esi, dword ptr [ebp - 0x18]
008d872c cmp      dword ptr [esi + 0xbb8], ebx
008d8732 jne      0x8d873e
008d8734 push     0x80004003
008d8739 call     0xa5fde4
008d873e mov      ecx, dword ptr [esi + 0xbb8]
008d8744 lea      eax, [ebp - 0x14]
008d8747 push     eax
008d8748 call     0x440c54
008d874d mov      esi, eax
008d874f cmp      dword ptr [esi], ebx
008d8751 mov      byte ptr [ebp - 4], 6
008d8755 jne      0x8d8761
008d8757 push     0x80004003
008d875c call     0xa5fde4
008d8761 mov      eax, dword ptr [esi]
008d8763 mov      ecx, dword ptr [eax]
008d8765 sub      esp, 0x10
008d8768 mov      edi, esp
008d876a lea      esi, [ebp - 0x84]
008d8770 movsd    dword ptr es:[edi], dword ptr [esi]
008d8771 movsd    dword ptr es:[edi], dword ptr [esi]
008d8772 movsd    dword ptr es:[edi], dword ptr [esi]
008d8773 push     eax
008d8774 mov      dword ptr [ebp - 0x10], eax
008d8777 movsd    dword ptr es:[edi], dword ptr [esi]
008d8778 call     dword ptr [ecx + 0x64]
008d877b cmp      eax, ebx
008d877d jge      0x8d878d
008d877f push     0xbd8348
008d8784 push     dword ptr [ebp - 0x10]
008d8787 push     eax
008d8788 call     0xa5fdf2
008d878d lea      ecx, [ebp - 0x14]
008d8790 mov      byte ptr [ebp - 4], 4
008d8794 call     0x41569e
008d8799 mov      esi, 0xbf6300
008d879e push     esi
008d879f lea      ecx, [ebp - 0x74]
008d87a2 call     0x402f85
008d87a7 push     esi
008d87a8 lea      ecx, [ebp - 0x64]
008d87ab mov      byte ptr [ebp - 4], 7
008d87af call     0x402f85
008d87b4 mov      esi, dword ptr [ebp - 0x18]
008d87b7 cmp      dword ptr [esi + 0xbb8], ebx
008d87bd mov      byte ptr [ebp - 4], 8
008d87c1 jne      0x8d87cd
008d87c3 push     0x80004003
008d87c8 call     0xa5fde4
008d87cd mov      ecx, dword ptr [esi + 0xbb8]
008d87d3 lea      eax, [ebp - 0x14]
008d87d6 push     eax
008d87d7 call     0x440c54
008d87dc mov      esi, eax
008d87de cmp      dword ptr [esi], ebx
008d87e0 mov      byte ptr [ebp - 4], 9
008d87e4 jne      0x8d87f0
008d87e6 push     0x80004003
008d87eb call     0xa5fde4
008d87f0 mov      eax, dword ptr [esi]
008d87f2 sub      esp, 0x10
008d87f5 mov      edi, esp
008d87f7 lea      esi, [ebp - 0x74]
008d87fa movsd    dword ptr es:[edi], dword ptr [esi]
008d87fb movsd    dword ptr es:[edi], dword ptr [esi]
008d87fc mov      ecx, dword ptr [eax]
008d87fe movsd    dword ptr es:[edi], dword ptr [esi]
008d87ff movsd    dword ptr es:[edi], dword ptr [esi]
008d8800 sub      esp, 0x10
008d8803 mov      edi, esp
008d8805 push     dword ptr [ebp - 0x30]
008d8808 lea      esi, [ebp - 0x64]
008d880b push     dword ptr [ebp - 0x24]
008d880e movsd    dword ptr es:[edi], dword ptr [esi]
008d880f movsd    dword ptr es:[edi], dword ptr [esi]
008d8810 movsd    dword ptr es:[edi], dword ptr [esi]
008d8811 push     eax
008d8812 mov      dword ptr [ebp - 0x10], eax
008d8815 movsd    dword ptr es:[edi], dword ptr [esi]
008d8816 call     dword ptr [ecx + 0x90]
008d881c cmp      eax, ebx
008d881e jge      0x8d882e
008d8820 push     0xbd8348
008d8825 push     dword ptr [ebp - 0x10]
008d8828 push     eax
008d8829 call     0xa5fdf2
008d882e lea      ecx, [ebp - 0x14]
008d8831 call     0x41569e
008d8836 lea      eax, [ebp - 0x64]
008d8839 push     eax
008d883a mov      byte ptr [ebp - 4], 7
008d883e call     0x40291d
008d8843 cmp      eax, ebx
008d8845 pop      ecx
008d8846 jge      0x8d884e
008d8848 push     eax
008d8849 call     0xa5fde4
008d884e lea      eax, [ebp - 0x74]
008d8851 push     eax
008d8852 mov      byte ptr [ebp - 4], 4
008d8856 call     0x40291d
008d885b cmp      eax, ebx
008d885d pop      ecx
008d885e jge      0x8d8866
008d8860 push     eax
008d8861 call     0xa5fde4
008d8866 mov      esi, dword ptr [ebp - 0x18]
008d8869 cmp      dword ptr [esi + 0xbb8], ebx
008d886f jne      0x8d887b
008d8871 push     0x80004003
008d8876 call     0xa5fde4
008d887b mov      ecx, dword ptr [esi + 0xbb8]
008d8881 lea      eax, [ebp - 0x10]
008d8884 push     eax
008d8885 call     0x439dcb
008d888a mov      edi, eax
008d888c cmp      dword ptr [edi], ebx
008d888e mov      byte ptr [ebp - 4], 0xa
008d8892 jne      0x8d889e
008d8894 push     0x80004003
008d8899 call     0xa5fde4
008d889e mov      ecx, dword ptr [edi]
008d88a0 call     0x425aef
008d88a5 lea      ecx, [ebp - 0x10]
008d88a8 mov      dword ptr [ebp - 0x24], eax
008d88ab mov      byte ptr [ebp - 4], 4
008d88af call     0x41569e
008d88b4 cmp      dword ptr [esi + 0xbb8], ebx
008d88ba jne      0x8d88c6
008d88bc push     0x80004003
008d88c1 call     0xa5fde4
008d88c6 mov      ecx, dword ptr [esi + 0xbb8]
008d88cc lea      eax, [ebp - 0x10]
008d88cf push     eax
008d88d0 call     0x439dcb
008d88d5 mov      edi, eax
008d88d7 cmp      dword ptr [edi], ebx
008d88d9 mov      byte ptr [ebp - 4], 0xb
008d88dd jne      0x8d88e9
008d88df push     0x80004003
008d88e4 call     0xa5fde4
008d88e9 mov      ecx, dword ptr [edi]
008d88eb call     0x425b16
008d88f0 lea      ecx, [ebp - 0x10]
008d88f3 mov      dword ptr [ebp - 0x30], eax
008d88f6 mov      byte ptr [ebp - 4], 4
008d88fa call     0x41569e
008d88ff cmp      dword ptr [esi + 0xbb8], ebx
008d8905 jne      0x8d8911
008d8907 push     0x80004003
008d890c call     0xa5fde4
008d8911 mov      ecx, dword ptr [esi + 0xbb8]
008d8917 lea      eax, [ebp - 0x10]
008d891a push     eax
008d891b call     0x439dcb
008d8920 mov      edi, eax
008d8922 cmp      dword ptr [edi], ebx
008d8924 mov      byte ptr [ebp - 4], 0xc
008d8928 jne      0x8d8934
008d892a push     0x80004003
008d892f call     0xa5fde4
008d8934 mov      ecx, dword ptr [edi]
008d8936 lea      eax, [ebp - 0x54]
008d8939 push     eax
008d893a call     0x441aa1
008d893f push     eax
008d8940 lea      ecx, [ebp - 0x84]
008d8946 mov      byte ptr [ebp - 4], 0xd
008d894a call     0x51ffc3
008d894f lea      eax, [ebp - 0x54]
008d8952 push     eax
008d8953 mov      byte ptr [ebp - 4], 0xc
008d8957 call     0x40291d
008d895c cmp      eax, ebx
008d895e pop      ecx
008d895f jge      0x8d8967
008d8961 push     eax
008d8962 call     0xa5fde4
008d8967 lea      ecx, [ebp - 0x10]
008d896a mov      byte ptr [ebp - 4], 4
008d896e call     0x41569e
008d8973 cmp      dword ptr [esi + 0xbb8], ebx
008d8979 jne      0x8d8985
008d897b push     0x80004003
008d8980 call     0xa5fde4
008d8985 mov      esi, dword ptr [esi + 0xbb8]
008d898b lea      eax, [ebp - 0x14]
008d898e push     eax
008d898f mov      ecx, esi
008d8991 call     0x439dcb
008d8996 mov      esi, eax
008d8998 cmp      dword ptr [esi], ebx
008d899a mov      byte ptr [ebp - 4], 0xe
008d899e jne      0x8d89aa
008d89a0 push     0x80004003
008d89a5 call     0xa5fde4
008d89aa mov      eax, dword ptr [esi]
008d89ac mov      esi, 0xbf0b70
008d89b1 lea      edi, [ebp - 0x54]
008d89b4 movsd    dword ptr es:[edi], dword ptr [esi]
008d89b5 movsd    dword ptr es:[edi], dword ptr [esi]
008d89b6 mov      ecx, dword ptr [eax]
008d89b8 movsd    dword ptr es:[edi], dword ptr [esi]
008d89b9 movsd    dword ptr es:[edi], dword ptr [esi]
008d89ba sub      esp, 0x10
008d89bd mov      edi, esp
008d89bf lea      esi, [ebp - 0x54]
008d89c2 movsd    dword ptr es:[edi], dword ptr [esi]
008d89c3 movsd    dword ptr es:[edi], dword ptr [esi]
008d89c4 movsd    dword ptr es:[edi], dword ptr [esi]
008d89c5 push     eax
008d89c6 mov      dword ptr [ebp - 0x10], eax
008d89c9 movsd    dword ptr es:[edi], dword ptr [esi]
008d89ca call     dword ptr [ecx + 0x64]
008d89cd cmp      eax, ebx
008d89cf jge      0x8d89df
008d89d1 push     0xbd8348
008d89d6 push     dword ptr [ebp - 0x10]
008d89d9 push     eax
008d89da call     0xa5fdf2
008d89df lea      ecx, [ebp - 0x14]
008d89e2 mov      byte ptr [ebp - 4], 4
008d89e6 call     0x41569e
008d89eb mov      esi, dword ptr [ebp - 0x18]
008d89ee cmp      dword ptr [esi + 0xbb8], ebx
008d89f4 jne      0x8d8a00
008d89f6 push     0x80004003
008d89fb call     0xa5fde4
008d8a00 mov      ecx, dword ptr [esi + 0xbb8]
008d8a06 lea      eax, [ebp - 0x14]
008d8a09 push     eax
008d8a0a call     0x439dcb
008d8a0f mov      esi, eax
008d8a11 cmp      dword ptr [esi], ebx
008d8a13 mov      byte ptr [ebp - 4], 0xf
008d8a17 jne      0x8d8a23
008d8a19 push     0x80004003
008d8a1e call     0xa5fde4
008d8a23 mov      eax, dword ptr [esi]
008d8a25 mov      ecx, dword ptr [eax]
008d8a27 sub      esp, 0x10
008d8a2a mov      edi, esp
008d8a2c lea      esi, [ebp - 0x84]
008d8a32 movsd    dword ptr es:[edi], dword ptr [esi]
008d8a33 movsd    dword ptr es:[edi], dword ptr [esi]
008d8a34 movsd    dword ptr es:[edi], dword ptr [esi]
008d8a35 push     eax
008d8a36 mov      dword ptr [ebp - 0x10], eax
008d8a39 movsd    dword ptr es:[edi], dword ptr [esi]
008d8a3a call     dword ptr [ecx + 0x64]
008d8a3d cmp      eax, ebx
008d8a3f jge      0x8d8a4f
008d8a41 push     0xbd8348
008d8a46 push     dword ptr [ebp - 0x10]
008d8a49 push     eax
008d8a4a call     0xa5fdf2
008d8a4f lea      ecx, [ebp - 0x14]
008d8a52 mov      byte ptr [ebp - 4], 4
008d8a56 call     0x41569e
008d8a5b mov      esi, 0xbf6300
008d8a60 push     esi
008d8a61 lea      ecx, [ebp - 0x74]
008d8a64 call     0x402f85
008d8a69 push     esi
008d8a6a lea      ecx, [ebp - 0x64]
008d8a6d mov      byte ptr [ebp - 4], 0x10
008d8a71 call     0x402f85
008d8a76 mov      esi, dword ptr [ebp - 0x18]
008d8a79 cmp      dword ptr [esi + 0xbb8], ebx
008d8a7f mov      byte ptr [ebp - 4], 0x11
008d8a83 jne      0x8d8a8f
008d8a85 push     0x80004003
008d8a8a call     0xa5fde4
008d8a8f mov      ecx, dword ptr [esi + 0xbb8]
008d8a95 lea      eax, [ebp - 0x14]
008d8a98 push     eax
008d8a99 call     0x439dcb
008d8a9e mov      esi, eax
008d8aa0 cmp      dword ptr [esi], ebx
008d8aa2 mov      byte ptr [ebp - 4], 0x12
008d8aa6 jne      0x8d8ab2
008d8aa8 push     0x80004003
008d8aad call     0xa5fde4
008d8ab2 mov      eax, dword ptr [esi]
008d8ab4 sub      esp, 0x10
008d8ab7 mov      edi, esp
008d8ab9 lea      esi, [ebp - 0x74]
008d8abc movsd    dword ptr es:[edi], dword ptr [esi]
008d8abd movsd    dword ptr es:[edi], dword ptr [esi]
008d8abe mov      ecx, dword ptr [eax]
008d8ac0 movsd    dword ptr es:[edi], dword ptr [esi]
008d8ac1 movsd    dword ptr es:[edi], dword ptr [esi]
008d8ac2 sub      esp, 0x10
008d8ac5 mov      edi, esp
008d8ac7 push     dword ptr [ebp - 0x30]
008d8aca lea      esi, [ebp - 0x64]
008d8acd push     dword ptr [ebp - 0x24]
008d8ad0 movsd    dword ptr es:[edi], dword ptr [esi]
008d8ad1 movsd    dword ptr es:[edi], dword ptr [esi]
008d8ad2 movsd    dword ptr es:[edi], dword ptr [esi]
008d8ad3 push     eax
008d8ad4 mov      dword ptr [ebp - 0x10], eax
008d8ad7 movsd    dword ptr es:[edi], dword ptr [esi]
008d8ad8 call     dword ptr [ecx + 0x90]
008d8ade cmp      eax, ebx
008d8ae0 jge      0x8d8af0
008d8ae2 push     0xbd8348
008d8ae7 push     dword ptr [ebp - 0x10]
008d8aea push     eax
008d8aeb call     0xa5fdf2
008d8af0 lea      ecx, [ebp - 0x14]
008d8af3 call     0x41569e
008d8af8 lea      eax, [ebp - 0x64]
008d8afb push     eax
008d8afc mov      byte ptr [ebp - 4], 0x10
008d8b00 call     0x40291d
008d8b05 cmp      eax, ebx
008d8b07 pop      ecx
008d8b08 jge      0x8d8b10
008d8b0a push     eax
008d8b0b call     0xa5fde4
008d8b10 lea      eax, [ebp - 0x74]
008d8b13 push     eax
008d8b14 mov      byte ptr [ebp - 4], 4
008d8b18 call     0x40291d
008d8b1d cmp      eax, ebx
008d8b1f pop      ecx
008d8b20 jge      0x8d8b28
008d8b22 push     eax
008d8b23 call     0xa5fde4
008d8b28 or       dword ptr [ebp - 4], 0xffffffff
008d8b2c lea      eax, [ebp - 0x84]
008d8b32 push     eax
008d8b33 call     0x40291d
008d8b38 cmp      eax, ebx
008d8b3a pop      ecx
008d8b3b jge      0x8d8b43
008d8b3d push     eax
008d8b3e call     0xa5fde4
008d8b43 mov      eax, dword ptr [ebp + 8]
008d8b46 push     ebx
008d8b47 cdq      
008d8b48 push     0x69
008d8b4a push     edx
008d8b4b push     eax
008d8b4c call     0xa62930
008d8b51 mov      ecx, eax
008d8b53 mov      eax, dword ptr [ebp + 0xc]
008d8b56 mov      esi, edx
008d8b58 cdq      
008d8b59 push     edx
008d8b5a push     eax
008d8b5b push     esi
008d8b5c push     ecx
008d8b5d call     0xa62b40
008d8b62 push     0x69
008d8b64 pop      ecx
008d8b65 sub      ecx, eax
008d8b67 push     0
008d8b69 pop      eax
008d8b6a mov      dword ptr [ebp - 0x8c], ecx
008d8b70 sbb      eax, edx
008d8b72 push     0xbf6300
008d8b77 lea      ecx, [ebp - 0x74]
008d8b7a mov      dword ptr [ebp - 0x88], eax
008d8b80 call     0x402f85
008d8b85 mov      esi, dword ptr [ebp - 0x18]
008d8b88 cmp      dword ptr [esi + 0xbb8], ebx
008d8b8e mov      dword ptr [ebp - 4], 0x13
008d8b95 jne      0x8d8ba1
008d8b97 push     0x80004003
008d8b9c call     0xa5fde4
008d8ba1 mov      ecx, dword ptr [esi + 0xbb8]
008d8ba7 call     0x437476
008d8bac add      eax, 0x2bc
008d8bb1 push     3
008d8bb3 push     eax
008d8bb4 lea      ecx, [ebp - 0x64]
008d8bb7 call     0x402fab
008d8bbc cmp      dword ptr [esi + 0xbb8], ebx
008d8bc2 mov      byte ptr [ebp - 4], 0x14
008d8bc6 jne      0x8d8bd2
008d8bc8 push     0x80004003
008d8bcd call     0xa5fde4
008d8bd2 sub      esp, 0x10
008d8bd5 mov      edi, esp
008d8bd7 mov      eax, dword ptr [ebp - 0x18]
008d8bda mov      eax, dword ptr [eax + 0xbb8]
008d8be0 lea      esi, [ebp - 0x74]
008d8be3 movsd    dword ptr es:[edi], dword ptr [esi]
008d8be4 movsd    dword ptr es:[edi], dword ptr [esi]
008d8be5 mov      ecx, dword ptr [eax]
008d8be7 movsd    dword ptr es:[edi], dword ptr [esi]
008d8be8 movsd    dword ptr es:[edi], dword ptr [esi]
008d8be9 sub      esp, 0x10
008d8bec mov      edi, esp
008d8bee lea      esi, [ebp - 0x64]
008d8bf1 movsd    dword ptr es:[edi], dword ptr [esi]
008d8bf2 movsd    dword ptr es:[edi], dword ptr [esi]
008d8bf3 mov      edx, 0x145
008d8bf8 sub      edx, dword ptr [ebp - 0x8c]
008d8bfe push     0x245
008d8c03 movsd    dword ptr es:[edi], dword ptr [esi]
008d8c04 push     edx
008d8c05 push     eax
008d8c06 mov      dword ptr [ebp - 0x10], eax
008d8c09 movsd    dword ptr es:[edi], dword ptr [esi]
008d8c0a call     dword ptr [ecx + 0x90]
008d8c10 cmp      eax, ebx
008d8c12 jge      0x8d8c22
008d8c14 push     0xbd8348
008d8c19 push     dword ptr [ebp - 0x10]
008d8c1c push     eax
008d8c1d call     0xa5fdf2
008d8c22 lea      eax, [ebp - 0x64]
008d8c25 push     eax
008d8c26 mov      byte ptr [ebp - 4], 0x13
008d8c2a call     0x40291d
008d8c2f cmp      eax, ebx
008d8c31 pop      ecx
008d8c32 jge      0x8d8c3a
008d8c34 push     eax
008d8c35 call     0xa5fde4
008d8c3a or       dword ptr [ebp - 4], 0xffffffff
008d8c3e lea      eax, [ebp - 0x74]
008d8c41 push     eax
008d8c42 call     0x40291d
008d8c47 cmp      eax, ebx
008d8c49 pop      ecx
008d8c4a jge      0x8d8c52
008d8c4c push     eax
008d8c4d call     0xa5fde4
008d8c52 push     0xbf6300
008d8c57 lea      ecx, [ebp - 0x74]
008d8c5a call     0x402f85
008d8c5f mov      esi, dword ptr [ebp - 0x18]
008d8c62 cmp      dword ptr [esi + 0xbb8], ebx
008d8c68 mov      dword ptr [ebp - 4], 0x15
008d8c6f mov      edi, 0x80004003
008d8c74 jne      0x8d8c7c
008d8c76 push     edi
008d8c77 call     0xa5fde4
008d8c7c mov      ecx, dword ptr [esi + 0xbb8]
008d8c82 call     0x437476
008d8c87 add      eax, 0x2bc
008d8c8c push     3
008d8c8e push     eax
008d8c8f lea      ecx, [ebp - 0x64]
008d8c92 call     0x402fab
008d8c97 cmp      dword ptr [esi + 0xbb8], ebx
008d8c9d mov      byte ptr [ebp - 4], 0x16
008d8ca1 jne      0x8d8ca9
008d8ca3 push     edi
008d8ca4 call     0xa5fde4
008d8ca9 mov      ecx, dword ptr [esi + 0xbb8]
008d8caf lea      eax, [ebp - 0x14]
008d8cb2 push     eax
008d8cb3 call     0x439dcb
008d8cb8 mov      esi, eax
008d8cba cmp      dword ptr [esi], ebx
008d8cbc mov      byte ptr [ebp - 4], 0x17
008d8cc0 jne      0x8d8cc8
008d8cc2 push     edi
008d8cc3 call     0xa5fde4
008d8cc8 mov      eax, dword ptr [esi]
008d8cca sub      esp, 0x10
008d8ccd mov      edi, esp
008d8ccf lea      esi, [ebp - 0x74]
008d8cd2 movsd    dword ptr es:[edi], dword ptr [esi]
008d8cd3 movsd    dword ptr es:[edi], dword ptr [esi]
008d8cd4 mov      ecx, dword ptr [eax]
008d8cd6 movsd    dword ptr es:[edi], dword ptr [esi]
008d8cd7 movsd    dword ptr es:[edi], dword ptr [esi]
008d8cd8 sub      esp, 0x10
008d8cdb mov      edi, esp
008d8cdd lea      esi, [ebp - 0x64]
008d8ce0 movsd    dword ptr es:[edi], dword ptr [esi]
008d8ce1 movsd    dword ptr es:[edi], dword ptr [esi]
008d8ce2 push     0xf
008d8ce4 push     dword ptr [ebp - 0x8c]
008d8cea movsd    dword ptr es:[edi], dword ptr [esi]
008d8ceb push     eax
008d8cec mov      dword ptr [ebp - 0x10], eax
008d8cef movsd    dword ptr es:[edi], dword ptr [esi]
008d8cf0 call     dword ptr [ecx + 0x90]
008d8cf6 cmp      eax, ebx
008d8cf8 jge      0x8d8d08
008d8cfa push     0xbd8348
008d8cff push     dword ptr [ebp - 0x10]
008d8d02 push     eax
008d8d03 call     0xa5fdf2
008d8d08 lea      ecx, [ebp - 0x14]
008d8d0b call     0x41569e
008d8d10 lea      eax, [ebp - 0x64]
008d8d13 push     eax
008d8d14 mov      byte ptr [ebp - 4], 0x15
008d8d18 call     0x40291d
008d8d1d cmp      eax, ebx
008d8d1f pop      ecx
008d8d20 jge      0x8d8d28
008d8d22 push     eax
008d8d23 call     0xa5fde4
008d8d28 or       dword ptr [ebp - 4], 0xffffffff
008d8d2c lea      eax, [ebp - 0x74]
008d8d2f push     eax
008d8d30 call     0x40291d
008d8d35 cmp      eax, ebx
008d8d37 pop      ecx
008d8d38 jge      0x8d8d40
008d8d3a push     eax
008d8d3b call     0xa5fde4
008d8d40 mov      esi, dword ptr [ebp - 0x18]
008d8d43 cmp      dword ptr [esi + 0xbbc], ebx
008d8d49 jne      0x8d8d55
008d8d4b push     0x80004003
008d8d50 call     0xa5fde4
008d8d55 mov      ecx, dword ptr [esi + 0xbbc]
008d8d5b lea      eax, [ebp - 0x10]
008d8d5e push     eax
008d8d5f call     0x440c54
008d8d64 mov      edi, eax
008d8d66 cmp      dword ptr [edi], ebx
008d8d68 mov      dword ptr [ebp - 4], 0x18
008d8d6f jne      0x8d8d7b
008d8d71 push     0x80004003
008d8d76 call     0xa5fde4
008d8d7b mov      ecx, dword ptr [edi]
008d8d7d call     0x425aef
008d8d82 or       dword ptr [ebp - 4], 0xffffffff
008d8d86 lea      ecx, [ebp - 0x10]
008d8d89 mov      dword ptr [ebp - 0x30], eax
008d8d8c call     0x41569e
008d8d91 cmp      dword ptr [esi + 0xbbc], ebx
008d8d97 jne      0x8d8da3
008d8d99 push     0x80004003
008d8d9e call     0xa5fde4
008d8da3 mov      ecx, dword ptr [esi + 0xbbc]
008d8da9 lea      eax, [ebp - 0x10]
008d8dac push     eax
008d8dad call     0x440c54
008d8db2 mov      edi, eax
008d8db4 cmp      dword ptr [edi], ebx
008d8db6 mov      dword ptr [ebp - 4], 0x19
008d8dbd jne      0x8d8dc9
008d8dbf push     0x80004003
008d8dc4 call     0xa5fde4
008d8dc9 mov      ecx, dword ptr [edi]
008d8dcb call     0x425b16
008d8dd0 or       dword ptr [ebp - 4], 0xffffffff
008d8dd4 lea      ecx, [ebp - 0x10]
008d8dd7 mov      dword ptr [ebp - 0x24], eax
008d8dda call     0x41569e
008d8ddf cmp      dword ptr [esi + 0xbbc], ebx
008d8de5 jne      0x8d8df1
008d8de7 push     0x80004003
008d8dec call     0xa5fde4
008d8df1 mov      ecx, dword ptr [esi + 0xbbc]
008d8df7 lea      eax, [ebp - 0x10]
008d8dfa push     eax
008d8dfb call     0x440c54
008d8e00 mov      edi, eax
008d8e02 cmp      dword ptr [edi], ebx
008d8e04 mov      dword ptr [ebp - 4], 0x1a
008d8e0b jne      0x8d8e17
008d8e0d push     0x80004003
008d8e12 call     0xa5fde4
008d8e17 mov      ecx, dword ptr [edi]
008d8e19 lea      eax, [ebp - 0x84]
008d8e1f push     eax
008d8e20 call     0x441aa1
008d8e25 lea      ecx, [ebp - 0x10]
008d8e28 mov      byte ptr [ebp - 4], 0x1c
008d8e2c call     0x41569e
008d8e31 cmp      dword ptr [esi + 0xbbc], ebx
008d8e37 jne      0x8d8e43
008d8e39 push     0x80004003
008d8e3e call     0xa5fde4
008d8e43 mov      esi, dword ptr [esi + 0xbbc]
008d8e49 lea      eax, [ebp - 0x28]
008d8e4c push     eax
008d8e4d mov      ecx, esi
008d8e4f call     0x440c54
008d8e54 mov      esi, eax
008d8e56 cmp      dword ptr [esi], ebx
008d8e58 mov      byte ptr [ebp - 4], 0x1d
008d8e5c jne      0x8d8e68
008d8e5e push     0x80004003
008d8e63 call     0xa5fde4
008d8e68 mov      eax, dword ptr [esi]
008d8e6a mov      esi, 0xbf0b70
008d8e6f lea      edi, [ebp - 0x54]
008d8e72 movsd    dword ptr es:[edi], dword ptr [esi]
008d8e73 movsd    dword ptr es:[edi], dword ptr [esi]
008d8e74 mov      ecx, dword ptr [eax]
008d8e76 movsd    dword ptr es:[edi], dword ptr [esi]
008d8e77 movsd    dword ptr es:[edi], dword ptr [esi]
008d8e78 sub      esp, 0x10
008d8e7b mov      edi, esp
008d8e7d lea      esi, [ebp - 0x54]
008d8e80 movsd    dword ptr es:[edi], dword ptr [esi]
008d8e81 movsd    dword ptr es:[edi], dword ptr [esi]
008d8e82 movsd    dword ptr es:[edi], dword ptr [esi]
008d8e83 push     eax
008d8e84 mov      dword ptr [ebp - 0x14], eax
008d8e87 movsd    dword ptr es:[edi], dword ptr [esi]
008d8e88 call     dword ptr [ecx + 0x64]
008d8e8b cmp      eax, ebx
008d8e8d jge      0x8d8e9d
008d8e8f push     0xbd8348
008d8e94 push     dword ptr [ebp - 0x14]
008d8e97 push     eax
008d8e98 call     0xa5fdf2
008d8e9d lea      ecx, [ebp - 0x28]
008d8ea0 mov      byte ptr [ebp - 4], 0x1c
008d8ea4 call     0x41569e
008d8ea9 mov      esi, dword ptr [ebp - 0x18]
008d8eac cmp      dword ptr [esi + 0xbbc], ebx
008d8eb2 jne      0x8d8ebe
008d8eb4 push     0x80004003
008d8eb9 call     0xa5fde4
008d8ebe mov      ecx, dword ptr [esi + 0xbbc]
008d8ec4 lea      eax, [ebp - 0x28]
008d8ec7 push     eax
008d8ec8 call     0x440c54
008d8ecd mov      esi, eax
008d8ecf cmp      dword ptr [esi], ebx
008d8ed1 mov      byte ptr [ebp - 4], 0x1e
008d8ed5 jne      0x8d8ee1
008d8ed7 push     0x80004003
008d8edc call     0xa5fde4
008d8ee1 mov      eax, dword ptr [esi]
008d8ee3 mov      ecx, dword ptr [eax]
008d8ee5 sub      esp, 0x10
008d8ee8 mov      edi, esp
008d8eea lea      esi, [ebp - 0x84]
008d8ef0 movsd    dword ptr es:[edi], dword ptr [esi]
008d8ef1 movsd    dword ptr es:[edi], dword ptr [esi]
008d8ef2 movsd    dword ptr es:[edi], dword ptr [esi]
008d8ef3 push     eax
008d8ef4 mov      dword ptr [ebp - 0x14], eax
008d8ef7 movsd    dword ptr es:[edi], dword ptr [esi]
008d8ef8 call     dword ptr [ecx + 0x64]
008d8efb cmp      eax, ebx
008d8efd jge      0x8d8f0d
008d8eff push     0xbd8348
008d8f04 push     dword ptr [ebp - 0x14]
008d8f07 push     eax
008d8f08 call     0xa5fdf2
008d8f0d lea      ecx, [ebp - 0x28]
008d8f10 mov      byte ptr [ebp - 4], 0x1c
008d8f14 call     0x41569e
008d8f19 mov      esi, 0xbf6300
008d8f1e push     esi
008d8f1f lea      ecx, [ebp - 0x74]
008d8f22 call     0x402f85
008d8f27 push     esi
008d8f28 lea      ecx, [ebp - 0x64]
008d8f2b mov      byte ptr [ebp - 4], 0x1f
008d8f2f call     0x402f85
008d8f34 mov      esi, dword ptr [ebp - 0x18]
008d8f37 cmp      dword ptr [esi + 0xbbc], ebx
008d8f3d mov      byte ptr [ebp - 4], 0x20
008d8f41 jne      0x8d8f4d
008d8f43 push     0x80004003
008d8f48 call     0xa5fde4
008d8f4d mov      ecx, dword ptr [esi + 0xbbc]
008d8f53 lea      eax, [ebp - 0x28]
008d8f56 push     eax
008d8f57 call     0x440c54
008d8f5c mov      esi, eax
008d8f5e cmp      dword ptr [esi], ebx
008d8f60 mov      byte ptr [ebp - 4], 0x21
008d8f64 jne      0x8d8f70
008d8f66 push     0x80004003
008d8f6b call     0xa5fde4
008d8f70 mov      eax, dword ptr [esi]
008d8f72 sub      esp, 0x10
008d8f75 mov      edi, esp
008d8f77 lea      esi, [ebp - 0x74]
008d8f7a movsd    dword ptr es:[edi], dword ptr [esi]
008d8f7b movsd    dword ptr es:[edi], dword ptr [esi]
008d8f7c mov      ecx, dword ptr [eax]
008d8f7e movsd    dword ptr es:[edi], dword ptr [esi]
008d8f7f movsd    dword ptr es:[edi], dword ptr [esi]
008d8f80 sub      esp, 0x10
008d8f83 mov      edi, esp
008d8f85 push     dword ptr [ebp - 0x24]
008d8f88 lea      esi, [ebp - 0x64]
008d8f8b push     dword ptr [ebp - 0x30]
008d8f8e movsd    dword ptr es:[edi], dword ptr [esi]
008d8f8f movsd    dword ptr es:[edi], dword ptr [esi]
008d8f90 movsd    dword ptr es:[edi], dword ptr [esi]
008d8f91 push     eax
008d8f92 mov      dword ptr [ebp - 0x14], eax
008d8f95 movsd    dword ptr es:[edi], dword ptr [esi]
008d8f96 call     dword ptr [ecx + 0x90]
008d8f9c cmp      eax, ebx
008d8f9e jge      0x8d8fae
008d8fa0 push     0xbd8348
008d8fa5 push     dword ptr [ebp - 0x14]
008d8fa8 push     eax
008d8fa9 call     0xa5fdf2
008d8fae lea      ecx, [ebp - 0x28]
008d8fb1 call     0x41569e
008d8fb6 lea      eax, [ebp - 0x64]
008d8fb9 push     eax
008d8fba mov      byte ptr [ebp - 4], 0x1f
008d8fbe call     0x40291d
008d8fc3 cmp      eax, ebx
008d8fc5 pop      ecx
008d8fc6 jge      0x8d8fce
008d8fc8 push     eax
008d8fc9 call     0xa5fde4
008d8fce lea      eax, [ebp - 0x74]
008d8fd1 push     eax
008d8fd2 mov      byte ptr [ebp - 4], 0x1c
008d8fd6 call     0x40291d
008d8fdb cmp      eax, ebx
008d8fdd pop      ecx
008d8fde jge      0x8d8fe6
008d8fe0 push     eax
008d8fe1 call     0xa5fde4
008d8fe6 mov      esi, dword ptr [ebp - 0x18]
008d8fe9 cmp      dword ptr [esi + 0xbbc], ebx
008d8fef jne      0x8d8ffb
008d8ff1 push     0x80004003
008d8ff6 call     0xa5fde4
008d8ffb mov      ecx, dword ptr [esi + 0xbbc]
008d9001 lea      eax, [ebp - 0x14]
008d9004 push     eax
008d9005 call     0x439dcb
008d900a mov      edi, eax
008d900c cmp      dword ptr [edi], ebx
008d900e mov      byte ptr [ebp - 4], 0x22
008d9012 jne      0x8d901e
008d9014 push     0x80004003
008d9019 call     0xa5fde4
008d901e mov      ecx, dword ptr [edi]
008d9020 call     0x425aef
008d9025 lea      ecx, [ebp - 0x14]
008d9028 mov      dword ptr [ebp - 0x30], eax
008d902b mov      byte ptr [ebp - 4], 0x1c
008d902f call     0x41569e
008d9034 cmp      dword ptr [esi + 0xbbc], ebx
008d903a jne      0x8d9046
008d903c push     0x80004003
008d9041 call     0xa5fde4
008d9046 mov      ecx, dword ptr [esi + 0xbbc]
008d904c lea      eax, [ebp - 0x14]
008d904f push     eax
008d9050 call     0x439dcb
008d9055 mov      edi, eax
008d9057 cmp      dword ptr [edi], ebx
008d9059 mov      byte ptr [ebp - 4], 0x23
008d905d jne      0x8d9069
008d905f push     0x80004003
008d9064 call     0xa5fde4
008d9069 mov      ecx, dword ptr [edi]
008d906b call     0x425b16
008d9070 lea      ecx, [ebp - 0x14]
008d9073 mov      dword ptr [ebp - 0x24], eax
008d9076 mov      byte ptr [ebp - 4], 0x1c
008d907a call     0x41569e
008d907f cmp      dword ptr [esi + 0xbbc], ebx
008d9085 jne      0x8d9091
008d9087 push     0x80004003
008d908c call     0xa5fde4
008d9091 mov      ecx, dword ptr [esi + 0xbbc]
008d9097 lea      eax, [ebp - 0x14]
008d909a push     eax
008d909b call     0x439dcb
008d90a0 mov      edi, eax
008d90a2 cmp      dword ptr [edi], ebx
008d90a4 mov      byte ptr [ebp - 4], 0x24
008d90a8 jne      0x8d90b4
008d90aa push     0x80004003
008d90af call     0xa5fde4
008d90b4 mov      ecx, dword ptr [edi]
008d90b6 lea      eax, [ebp - 0x54]
008d90b9 push     eax
008d90ba call     0x441aa1
008d90bf push     eax
008d90c0 lea      ecx, [ebp - 0x84]
008d90c6 mov      byte ptr [ebp - 4], 0x25
008d90ca call     0x51ffc3
008d90cf lea      eax, [ebp - 0x54]
008d90d2 push     eax
008d90d3 mov      byte ptr [ebp - 4], 0x24
008d90d7 call     0x40291d
008d90dc cmp      eax, ebx
008d90de pop      ecx
008d90df jge      0x8d90e7
008d90e1 push     eax
008d90e2 call     0xa5fde4
008d90e7 lea      ecx, [ebp - 0x14]
008d90ea mov      byte ptr [ebp - 4], 0x1c
008d90ee call     0x41569e
008d90f3 cmp      dword ptr [esi + 0xbbc], ebx
008d90f9 jne      0x8d9105
008d90fb push     0x80004003
008d9100 call     0xa5fde4
008d9105 mov      esi, dword ptr [esi + 0xbbc]
008d910b lea      eax, [ebp - 0x28]
008d910e push     eax
008d910f mov      ecx, esi
008d9111 call     0x439dcb
008d9116 mov      esi, eax
008d9118 cmp      dword ptr [esi], ebx
008d911a mov      byte ptr [ebp - 4], 0x26
008d911e jne      0x8d912a
008d9120 push     0x80004003
008d9125 call     0xa5fde4
008d912a mov      eax, dword ptr [esi]
008d912c mov      esi, 0xbf0b70
008d9131 lea      edi, [ebp - 0x54]
008d9134 movsd    dword ptr es:[edi], dword ptr [esi]
008d9135 movsd    dword ptr es:[edi], dword ptr [esi]
008d9136 mov      ecx, dword ptr [eax]
008d9138 movsd    dword ptr es:[edi], dword ptr [esi]
008d9139 movsd    dword ptr es:[edi], dword ptr [esi]
008d913a sub      esp, 0x10
008d913d mov      edi, esp
008d913f lea      esi, [ebp - 0x54]
008d9142 movsd    dword ptr es:[edi], dword ptr [esi]
008d9143 movsd    dword ptr es:[edi], dword ptr [esi]
008d9144 movsd    dword ptr es:[edi], dword ptr [esi]
008d9145 push     eax
008d9146 mov      dword ptr [ebp - 0x14], eax
008d9149 movsd    dword ptr es:[edi], dword ptr [esi]
008d914a call     dword ptr [ecx + 0x64]
008d914d cmp      eax, ebx
008d914f jge      0x8d915f
008d9151 push     0xbd8348
008d9156 push     dword ptr [ebp - 0x14]
008d9159 push     eax
008d915a call     0xa5fdf2
008d915f lea      ecx, [ebp - 0x28]
008d9162 mov      byte ptr [ebp - 4], 0x1c
008d9166 call     0x41569e
008d916b mov      esi, dword ptr [ebp - 0x18]
008d916e cmp      dword ptr [esi + 0xbbc], ebx
008d9174 jne      0x8d9180
008d9176 push     0x80004003
008d917b call     0xa5fde4
008d9180 mov      ecx, dword ptr [esi + 0xbbc]
008d9186 lea      eax, [ebp - 0x28]
008d9189 push     eax
008d918a call     0x439dcb
008d918f mov      esi, eax
008d9191 cmp      dword ptr [esi], ebx
008d9193 mov      byte ptr [ebp - 4], 0x27
008d9197 jne      0x8d91a3
008d9199 push     0x80004003
008d919e call     0xa5fde4
008d91a3 mov      eax, dword ptr [esi]
008d91a5 mov      ecx, dword ptr [eax]
008d91a7 sub      esp, 0x10
008d91aa mov      edi, esp
008d91ac lea      esi, [ebp - 0x84]
008d91b2 movsd    dword ptr es:[edi], dword ptr [esi]
008d91b3 movsd    dword ptr es:[edi], dword ptr [esi]
008d91b4 movsd    dword ptr es:[edi], dword ptr [esi]
008d91b5 push     eax
008d91b6 mov      dword ptr [ebp - 0x14], eax
008d91b9 movsd    dword ptr es:[edi], dword ptr [esi]
008d91ba call     dword ptr [ecx + 0x64]
008d91bd cmp      eax, ebx
008d91bf jge      0x8d91cf
008d91c1 push     0xbd8348
008d91c6 push     dword ptr [ebp - 0x14]
008d91c9 push     eax
008d91ca call     0xa5fdf2
008d91cf lea      ecx, [ebp - 0x28]
008d91d2 mov      byte ptr [ebp - 4], 0x1c
008d91d6 call     0x41569e
008d91db mov      esi, 0xbf6300
008d91e0 push     esi
008d91e1 lea      ecx, [ebp - 0x74]
008d91e4 call     0x402f85
008d91e9 push     esi
008d91ea lea      ecx, [ebp - 0x64]
008d91ed mov      byte ptr [ebp - 4], 0x28
008d91f1 call     0x402f85
008d91f6 mov      esi, dword ptr [ebp - 0x18]
008d91f9 cmp      dword ptr [esi + 0xbbc], ebx
008d91ff mov      byte ptr [ebp - 4], 0x29
008d9203 jne      0x8d920f
008d9205 push     0x80004003
008d920a call     0xa5fde4
008d920f mov      ecx, dword ptr [esi + 0xbbc]
008d9215 lea      eax, [ebp - 0x28]
008d9218 push     eax
008d9219 call     0x439dcb
008d921e mov      esi, eax
008d9220 cmp      dword ptr [esi], ebx
008d9222 mov      byte ptr [ebp - 4], 0x2a
008d9226 jne      0x8d9232
008d9228 push     0x80004003
008d922d call     0xa5fde4
008d9232 mov      eax, dword ptr [esi]
008d9234 sub      esp, 0x10
008d9237 mov      edi, esp
008d9239 lea      esi, [ebp - 0x74]
008d923c movsd    dword ptr es:[edi], dword ptr [esi]
008d923d movsd    dword ptr es:[edi], dword ptr [esi]
008d923e mov      ecx, dword ptr [eax]
008d9240 movsd    dword ptr es:[edi], dword ptr [esi]
008d9241 movsd    dword ptr es:[edi], dword ptr [esi]
008d9242 sub      esp, 0x10
008d9245 mov      edi, esp
008d9247 push     dword ptr [ebp - 0x24]
008d924a lea      esi, [ebp - 0x64]
008d924d push     dword ptr [ebp - 0x30]
008d9250 movsd    dword ptr es:[edi], dword ptr [esi]
008d9251 movsd    dword ptr es:[edi], dword ptr [esi]
008d9252 movsd    dword ptr es:[edi], dword ptr [esi]
008d9253 push     eax
008d9254 mov      dword ptr [ebp - 0x14], eax
008d9257 movsd    dword ptr es:[edi], dword ptr [esi]
008d9258 call     dword ptr [ecx + 0x90]
008d925e cmp      eax, ebx
008d9260 jge      0x8d9270
008d9262 push     0xbd8348
008d9267 push     dword ptr [ebp - 0x14]
008d926a push     eax
008d926b call     0xa5fdf2
008d9270 lea      ecx, [ebp - 0x28]
008d9273 call     0x41569e
008d9278 lea      eax, [ebp - 0x64]
008d927b push     eax
008d927c mov      byte ptr [ebp - 4], 0x28
008d9280 call     0x40291d
008d9285 cmp      eax, ebx
008d9287 pop      ecx
008d9288 jge      0x8d9290
008d928a push     eax
008d928b call     0xa5fde4
008d9290 lea      eax, [ebp - 0x74]
008d9293 push     eax
008d9294 mov      byte ptr [ebp - 4], 0x1c
008d9298 call     0x40291d
008d929d cmp      eax, ebx
008d929f pop      ecx
008d92a0 jge      0x8d92a8
008d92a2 push     eax
008d92a3 call     0xa5fde4
008d92a8 or       dword ptr [ebp - 4], 0xffffffff
008d92ac lea      eax, [ebp - 0x84]
008d92b2 push     eax
008d92b3 call     0x40291d
008d92b8 cmp      eax, ebx
008d92ba pop      ecx
008d92bb jge      0x8d92c3
008d92bd push     eax
008d92be call     0xa5fde4
008d92c3 mov      eax, dword ptr [ebp + 0x10]
008d92c6 push     ebx
008d92c7 cdq      
008d92c8 push     0x69
008d92ca push     edx
008d92cb push     eax
008d92cc call     0xa62930
008d92d1 mov      ecx, eax
008d92d3 mov      eax, dword ptr [ebp + 0x14]
008d92d6 mov      esi, edx
008d92d8 cdq      
008d92d9 push     edx
008d92da push     eax
008d92db push     esi
008d92dc push     ecx
008d92dd call     0xa62b40
008d92e2 push     0x69
008d92e4 pop      ecx
008d92e5 sub      ecx, eax
008d92e7 push     0
008d92e9 pop      eax
008d92ea mov      dword ptr [ebp - 0x8c], ecx
008d92f0 sbb      eax, edx
008d92f2 push     0xbf6300
008d92f7 lea      ecx, [ebp - 0x74]
008d92fa mov      dword ptr [ebp - 0x88], eax
008d9300 call     0x402f85
008d9305 mov      esi, dword ptr [ebp - 0x18]
008d9308 cmp      dword ptr [esi + 0xbbc], ebx
008d930e mov      dword ptr [ebp - 4], 0x2b
008d9315 jne      0x8d9321
008d9317 push     0x80004003
008d931c call     0xa5fde4
008d9321 mov      ecx, dword ptr [esi + 0xbbc]
008d9327 call     0x437476
008d932c add      eax, 0x2bc
008d9331 push     3
008d9333 push     eax
008d9334 lea      ecx, [ebp - 0x64]
008d9337 call     0x402fab
008d933c cmp      dword ptr [esi + 0xbbc], ebx
008d9342 mov      byte ptr [ebp - 4], 0x2c
008d9346 jne      0x8d9352
008d9348 push     0x80004003
008d934d call     0xa5fde4
008d9352 sub      esp, 0x10
008d9355 mov      edi, esp
008d9357 mov      eax, dword ptr [ebp - 0x18]
008d935a mov      eax, dword ptr [eax + 0xbbc]
008d9360 lea      esi, [ebp - 0x74]
008d9363 movsd    dword ptr es:[edi], dword ptr [esi]
008d9364 movsd    dword ptr es:[edi], dword ptr [esi]
008d9365 mov      ecx, dword ptr [eax]
008d9367 movsd    dword ptr es:[edi], dword ptr [esi]
008d9368 movsd    dword ptr es:[edi], dword ptr [esi]
008d9369 sub      esp, 0x10
008d936c mov      edi, esp
008d936e lea      esi, [ebp - 0x64]
008d9371 movsd    dword ptr es:[edi], dword ptr [esi]
008d9372 movsd    dword ptr es:[edi], dword ptr [esi]
008d9373 mov      edx, 0x1b1
008d9378 sub      edx, dword ptr [ebp - 0x8c]
008d937e push     0x245
008d9383 movsd    dword ptr es:[edi], dword ptr [esi]
008d9384 push     edx
008d9385 push     eax
008d9386 mov      dword ptr [ebp - 0x10], eax
008d9389 movsd    dword ptr es:[edi], dword ptr [esi]
008d938a call     dword ptr [ecx + 0x90]
008d9390 cmp      eax, ebx
008d9392 jge      0x8d93a2
008d9394 push     0xbd8348
008d9399 push     dword ptr [ebp - 0x10]
008d939c push     eax
008d939d call     0xa5fdf2
008d93a2 lea      eax, [ebp - 0x64]
008d93a5 push     eax
008d93a6 mov      byte ptr [ebp - 4], 0x2b
008d93aa call     0x40291d
008d93af cmp      eax, ebx
008d93b1 pop      ecx
008d93b2 jge      0x8d93ba
008d93b4 push     eax
008d93b5 call     0xa5fde4
008d93ba or       dword ptr [ebp - 4], 0xffffffff
008d93be lea      eax, [ebp - 0x74]
008d93c1 push     eax
008d93c2 call     0x40291d
008d93c7 cmp      eax, ebx
008d93c9 pop      ecx
008d93ca jge      0x8d93d2
008d93cc push     eax
008d93cd call     0xa5fde4
008d93d2 push     0xbf6300
008d93d7 lea      ecx, [ebp - 0x74]
008d93da call     0x402f85
008d93df mov      esi, dword ptr [ebp - 0x18]
008d93e2 cmp      dword ptr [esi + 0xbbc], ebx
008d93e8 mov      dword ptr [ebp - 4], 0x2d
008d93ef mov      edi, 0x80004003
008d93f4 jne      0x8d93fc
008d93f6 push     edi
008d93f7 call     0xa5fde4
008d93fc mov      ecx, dword ptr [esi + 0xbbc]
008d9402 call     0x437476
008d9407 add      eax, 0x2bc
008d940c push     3
008d940e push     eax
008d940f lea      ecx, [ebp - 0x64]
008d9412 call     0x402fab
008d9417 cmp      dword ptr [esi + 0xbbc], ebx
008d941d mov      byte ptr [ebp - 4], 0x2e
008d9421 jne      0x8d9429
008d9423 push     edi
008d9424 call     0xa5fde4
008d9429 mov      ecx, dword ptr [esi + 0xbbc]
008d942f lea      eax, [ebp - 0x14]
008d9432 push     eax
008d9433 call     0x439dcb
008d9438 mov      esi, eax
008d943a cmp      dword ptr [esi], ebx
008d943c mov      byte ptr [ebp - 4], 0x2f
008d9440 jne      0x8d9448
008d9442 push     edi
008d9443 call     0xa5fde4
008d9448 mov      eax, dword ptr [esi]
008d944a sub      esp, 0x10
008d944d mov      edi, esp
008d944f lea      esi, [ebp - 0x74]
008d9452 movsd    dword ptr es:[edi], dword ptr [esi]
008d9453 movsd    dword ptr es:[edi], dword ptr [esi]
008d9454 mov      ecx, dword ptr [eax]
008d9456 movsd    dword ptr es:[edi], dword ptr [esi]
008d9457 movsd    dword ptr es:[edi], dword ptr [esi]
008d9458 sub      esp, 0x10
008d945b mov      edi, esp
008d945d lea      esi, [ebp - 0x64]
008d9460 movsd    dword ptr es:[edi], dword ptr [esi]
008d9461 movsd    dword ptr es:[edi], dword ptr [esi]
008d9462 push     0xf
008d9464 push     dword ptr [ebp - 0x8c]
008d946a movsd    dword ptr es:[edi], dword ptr [esi]
008d946b push     eax
008d946c mov      dword ptr [ebp - 0x10], eax
008d946f movsd    dword ptr es:[edi], dword ptr [esi]
008d9470 call     dword ptr [ecx + 0x90]
008d9476 cmp      eax, ebx
008d9478 jge      0x8d9488
008d947a push     0xbd8348
008d947f push     dword ptr [ebp - 0x10]
008d9482 push     eax
008d9483 call     0xa5fdf2
008d9488 lea      ecx, [ebp - 0x14]
008d948b call     0x41569e
008d9490 lea      eax, [ebp - 0x64]
008d9493 push     eax
008d9494 mov      byte ptr [ebp - 4], 0x2d
008d9498 call     0x40291d
008d949d cmp      eax, ebx
008d949f pop      ecx
008d94a0 jge      0x8d94a8
008d94a2 push     eax
008d94a3 call     0xa5fde4
008d94a8 or       dword ptr [ebp - 4], 0xffffffff
008d94ac lea      eax, [ebp - 0x74]
008d94af push     eax
008d94b0 call     0x40291d
008d94b5 cmp      eax, ebx
008d94b7 pop      ecx
008d94b8 jge      0x8d94c0
008d94ba push     eax
008d94bb call     0xa5fde4
008d94c0 mov      eax, dword ptr [ebp - 0x18]
008d94c3 cmp      dword ptr [eax + 0xbc0], ebx
008d94c9 lea      esi, [eax + 0xbc0]
008d94cf jne      0x8d94db
008d94d1 push     0x80004003
008d94d6 call     0xa5fde4
008d94db mov      ecx, dword ptr [esi]
008d94dd lea      eax, [ebp - 0x10]
008d94e0 push     eax
008d94e1 call     0x440c54
008d94e6 mov      edi, eax
008d94e8 cmp      dword ptr [edi], ebx
008d94ea mov      dword ptr [ebp - 4], 0x30
008d94f1 jne      0x8d94fd
008d94f3 push     0x80004003
008d94f8 call     0xa5fde4
008d94fd mov      ecx, dword ptr [edi]
008d94ff call     0x425aef
008d9504 or       dword ptr [ebp - 4], 0xffffffff
008d9508 lea      ecx, [ebp - 0x10]
008d950b mov      dword ptr [ebp - 0x30], eax
008d950e call     0x41569e
008d9513 cmp      dword ptr [esi], ebx
008d9515 jne      0x8d9521
008d9517 push     0x80004003
008d951c call     0xa5fde4
008d9521 mov      ecx, dword ptr [esi]
008d9523 lea      eax, [ebp - 0x10]
008d9526 push     eax
008d9527 call     0x440c54
008d952c mov      edi, eax
008d952e cmp      dword ptr [edi], ebx
008d9530 mov      dword ptr [ebp - 4], 0x31
008d9537 jne      0x8d9543
008d9539 push     0x80004003
008d953e call     0xa5fde4
008d9543 mov      ecx, dword ptr [edi]
008d9545 call     0x425b16
008d954a or       dword ptr [ebp - 4], 0xffffffff
008d954e lea      ecx, [ebp - 0x10]
008d9551 mov      dword ptr [ebp - 0x24], eax
008d9554 call     0x41569e
008d9559 cmp      dword ptr [esi], ebx
008d955b jne      0x8d9567
008d955d push     0x80004003
008d9562 call     0xa5fde4
008d9567 mov      ecx, dword ptr [esi]
008d9569 lea      eax, [ebp - 0x10]
008d956c push     eax
008d956d call     0x440c54
008d9572 mov      edi, eax
008d9574 cmp      dword ptr [edi], ebx
008d9576 mov      dword ptr [ebp - 4], 0x32
008d957d jne      0x8d9589
008d957f push     0x80004003
008d9584 call     0xa5fde4
008d9589 mov      ecx, dword ptr [edi]
008d958b lea      eax, [ebp - 0x74]
008d958e push     eax
008d958f call     0x441aa1
008d9594 lea      ecx, [ebp - 0x10]
008d9597 mov      byte ptr [ebp - 4], 0x34
008d959b call     0x41569e
008d95a0 cmp      dword ptr [esi], ebx
008d95a2 jne      0x8d95ae
008d95a4 push     0x80004003
008d95a9 call     0xa5fde4
008d95ae mov      esi, dword ptr [esi]
008d95b0 lea      eax, [ebp - 0x28]
008d95b3 push     eax
008d95b4 mov      ecx, esi
008d95b6 call     0x440c54
008d95bb mov      esi, eax
008d95bd cmp      dword ptr [esi], ebx
008d95bf mov      byte ptr [ebp - 4], 0x35
008d95c3 jne      0x8d95cf
008d95c5 push     0x80004003
008d95ca call     0xa5fde4
008d95cf mov      eax, dword ptr [esi]
008d95d1 mov      esi, 0xbf0b70
008d95d6 lea      edi, [ebp - 0x54]
008d95d9 movsd    dword ptr es:[edi], dword ptr [esi]
008d95da movsd    dword ptr es:[edi], dword ptr [esi]
008d95db mov      ecx, dword ptr [eax]
008d95dd movsd    dword ptr es:[edi], dword ptr [esi]
008d95de movsd    dword ptr es:[edi], dword ptr [esi]
008d95df sub      esp, 0x10
008d95e2 mov      edi, esp
008d95e4 lea      esi, [ebp - 0x54]
008d95e7 movsd    dword ptr es:[edi], dword ptr [esi]
008d95e8 movsd    dword ptr es:[edi], dword ptr [esi]
008d95e9 movsd    dword ptr es:[edi], dword ptr [esi]
008d95ea push     eax
008d95eb mov      dword ptr [ebp - 0x14], eax
008d95ee movsd    dword ptr es:[edi], dword ptr [esi]
008d95ef call     dword ptr [ecx + 0x64]
008d95f2 cmp      eax, ebx
008d95f4 jge      0x8d9604
008d95f6 push     0xbd8348
008d95fb push     dword ptr [ebp - 0x14]
008d95fe push     eax
008d95ff call     0xa5fdf2
008d9604 lea      ecx, [ebp - 0x28]
008d9607 mov      byte ptr [ebp - 4], 0x34
008d960b call     0x41569e
008d9610 mov      eax, dword ptr [ebp - 0x18]
008d9613 cmp      dword ptr [eax + 0xbc0], ebx
008d9619 lea      esi, [eax + 0xbc0]
008d961f jne      0x8d962b
008d9621 push     0x80004003
008d9626 call     0xa5fde4
008d962b mov      ecx, dword ptr [esi]
008d962d lea      eax, [ebp - 0x28]
008d9630 push     eax
008d9631 call     0x440c54
008d9636 mov      esi, eax
008d9638 mov      byte ptr [ebp - 4], 0x36
008d963c cmp      dword ptr [esi], ebx
008d963e jne      0x8d964a
008d9640 push     0x80004003
008d9645 call     0xa5fde4
008d964a mov      eax, dword ptr [esi]
008d964c sub      esp, 0x10
008d964f mov      edi, esp
008d9651 mov      dword ptr [ebp - 0x14], eax
008d9654 mov      ecx, dword ptr [eax]
008d9656 lea      esi, [ebp - 0x74]
008d9659 movsd    dword ptr es:[edi], dword ptr [esi]
008d965a movsd    dword ptr es:[edi], dword ptr [esi]
008d965b movsd    dword ptr es:[edi], dword ptr [esi]
008d965c push     eax
008d965d movsd    dword ptr es:[edi], dword ptr [esi]
008d965e call     dword ptr [ecx + 0x64]
008d9661 cmp      eax, ebx
008d9663 jge      0x8d9673
008d9665 push     0xbd8348
008d966a push     dword ptr [ebp - 0x14]
008d966d push     eax
008d966e call     0xa5fdf2
008d9673 lea      ecx, [ebp - 0x28]
008d9676 mov      byte ptr [ebp - 4], 0x34
008d967a call     0x41569e
008d967f mov      esi, 0xbf6300
008d9684 push     esi
008d9685 lea      ecx, [ebp - 0x84]
008d968b call     0x402f85
008d9690 push     esi
008d9691 lea      ecx, [ebp - 0x64]
008d9694 mov      byte ptr [ebp - 4], 0x37
008d9698 call     0x402f85
008d969d mov      eax, dword ptr [ebp - 0x18]
008d96a0 lea      esi, [eax + 0xbc0]
008d96a6 mov      byte ptr [ebp - 4], 0x38
008d96aa cmp      dword ptr [esi], ebx
008d96ac jne      0x8d96b8
008d96ae push     0x80004003
008d96b3 call     0xa5fde4
008d96b8 mov      ecx, dword ptr [esi]
008d96ba lea      eax, [ebp - 0x1c]
008d96bd push     eax
008d96be call     0x440c54
008d96c3 mov      esi, eax
008d96c5 cmp      dword ptr [esi], ebx
008d96c7 mov      byte ptr [ebp - 4], 0x39
008d96cb jne      0x8d96d7
008d96cd push     0x80004003
008d96d2 call     0xa5fde4
008d96d7 mov      eax, dword ptr [esi]
008d96d9 sub      esp, 0x10
008d96dc mov      edi, esp
008d96de mov      dword ptr [ebp - 0x14], eax
008d96e1 mov      ecx, dword ptr [eax]
008d96e3 lea      esi, [ebp - 0x84]
008d96e9 movsd    dword ptr es:[edi], dword ptr [esi]
008d96ea movsd    dword ptr es:[edi], dword ptr [esi]
008d96eb movsd    dword ptr es:[edi], dword ptr [esi]
008d96ec movsd    dword ptr es:[edi], dword ptr [esi]
008d96ed sub      esp, 0x10
008d96f0 mov      edi, esp
008d96f2 push     dword ptr [ebp - 0x24]
008d96f5 lea      esi, [ebp - 0x64]
008d96f8 push     dword ptr [ebp - 0x30]
008d96fb movsd    dword ptr es:[edi], dword ptr [esi]
008d96fc movsd    dword ptr es:[edi], dword ptr [esi]
008d96fd movsd    dword ptr es:[edi], dword ptr [esi]
008d96fe push     eax
008d96ff movsd    dword ptr es:[edi], dword ptr [esi]
008d9700 call     dword ptr [ecx + 0x90]
008d9706 cmp      eax, ebx
008d9708 jge      0x8d9718
008d970a push     0xbd8348
008d970f push     dword ptr [ebp - 0x14]
008d9712 push     eax
008d9713 call     0xa5fdf2
008d9718 lea      ecx, [ebp - 0x1c]
008d971b call     0x41569e
008d9720 lea      eax, [ebp - 0x64]
008d9723 push     eax
008d9724 mov      byte ptr [ebp - 4], 0x37
008d9728 call     0x40291d
008d972d cmp      eax, ebx
008d972f pop      ecx
008d9730 jge      0x8d9738
008d9732 push     eax
008d9733 call     0xa5fde4
008d9738 lea      eax, [ebp - 0x84]
008d973e push     eax
008d973f mov      byte ptr [ebp - 4], 0x34
008d9743 call     0x40291d
008d9748 cmp      eax, ebx
008d974a pop      ecx
008d974b jge      0x8d9753
008d974d push     eax
008d974e call     0xa5fde4
008d9753 mov      eax, dword ptr [ebp - 0x18]
008d9756 cmp      dword ptr [eax + 0xbc0], ebx
008d975c lea      esi, [eax + 0xbc0]
008d9762 jne      0x8d976e
008d9764 push     0x80004003
008d9769 call     0xa5fde4
008d976e mov      ecx, dword ptr [esi]
008d9770 lea      eax, [ebp - 0x1c]
008d9773 push     eax
008d9774 call     0x439dcb
008d9779 mov      edi, eax
008d977b cmp      dword ptr [edi], ebx
008d977d mov      byte ptr [ebp - 4], 0x3a
008d9781 jne      0x8d978d
008d9783 push     0x80004003
008d9788 call     0xa5fde4
008d978d mov      ecx, dword ptr [edi]
008d978f call     0x425aef
008d9794 lea      ecx, [ebp - 0x1c]
008d9797 mov      dword ptr [ebp - 0x30], eax
008d979a mov      byte ptr [ebp - 4], 0x34
008d979e call     0x41569e
008d97a3 cmp      dword ptr [esi], ebx
008d97a5 jne      0x8d97b1
008d97a7 push     0x80004003
008d97ac call     0xa5fde4
008d97b1 mov      ecx, dword ptr [esi]
008d97b3 lea      eax, [ebp - 0x1c]
008d97b6 push     eax
008d97b7 call     0x439dcb
008d97bc mov      edi, eax
008d97be cmp      dword ptr [edi], ebx
008d97c0 mov      byte ptr [ebp - 4], 0x3b
008d97c4 jne      0x8d97d0
008d97c6 push     0x80004003
008d97cb call     0xa5fde4
008d97d0 mov      ecx, dword ptr [edi]
008d97d2 call     0x425b16
008d97d7 lea      ecx, [ebp - 0x1c]
008d97da mov      dword ptr [ebp - 0x24], eax
008d97dd mov      byte ptr [ebp - 4], 0x34
008d97e1 call     0x41569e
008d97e6 cmp      dword ptr [esi], ebx
008d97e8 jne      0x8d97f4
008d97ea push     0x80004003
008d97ef call     0xa5fde4
008d97f4 mov      ecx, dword ptr [esi]
008d97f6 lea      eax, [ebp - 0x1c]
008d97f9 push     eax
008d97fa call     0x439dcb
008d97ff mov      edi, eax
008d9801 cmp      dword ptr [edi], ebx
008d9803 mov      byte ptr [ebp - 4], 0x3c
008d9807 jne      0x8d9813
008d9809 push     0x80004003
008d980e call     0xa5fde4
008d9813 mov      ecx, dword ptr [edi]
008d9815 lea      eax, [ebp - 0x54]
008d9818 push     eax
008d9819 call     0x441aa1
008d981e push     eax
008d981f lea      ecx, [ebp - 0x74]
008d9822 mov      byte ptr [ebp - 4], 0x3d
008d9826 call     0x51ffc3
008d982b lea      eax, [ebp - 0x54]
008d982e push     eax
008d982f mov      byte ptr [ebp - 4], 0x3c
008d9833 call     0x40291d
008d9838 cmp      eax, ebx
008d983a pop      ecx
008d983b jge      0x8d9843
008d983d push     eax
008d983e call     0xa5fde4
008d9843 lea      ecx, [ebp - 0x1c]
008d9846 mov      byte ptr [ebp - 4], 0x34
008d984a call     0x41569e
008d984f cmp      dword ptr [esi], ebx
008d9851 jne      0x8d985d
008d9853 push     0x80004003
008d9858 call     0xa5fde4
008d985d mov      esi, dword ptr [esi]
008d985f lea      eax, [ebp - 0x14]
008d9862 push     eax
008d9863 mov      ecx, esi
008d9865 call     0x439dcb
008d986a mov      esi, eax
008d986c cmp      dword ptr [esi], ebx
008d986e mov      byte ptr [ebp - 4], 0x3e
008d9872 jne      0x8d987e
008d9874 push     0x80004003
008d9879 call     0xa5fde4
008d987e mov      eax, dword ptr [esi]
008d9880 mov      esi, 0xbf0b70
008d9885 lea      edi, [ebp - 0x54]
008d9888 movsd    dword ptr es:[edi], dword ptr [esi]
008d9889 movsd    dword ptr es:[edi], dword ptr [esi]
008d988a movsd    dword ptr es:[edi], dword ptr [esi]
008d988b movsd    dword ptr es:[edi], dword ptr [esi]
008d988c sub      esp, 0x10
008d988f mov      edi, esp
008d9891 mov      dword ptr [ebp - 0x1c], eax
008d9894 mov      ecx, dword ptr [eax]
008d9896 lea      esi, [ebp - 0x54]
008d9899 movsd    dword ptr es:[edi], dword ptr [esi]
008d989a movsd    dword ptr es:[edi], dword ptr [esi]
008d989b movsd    dword ptr es:[edi], dword ptr [esi]
008d989c push     eax
008d989d movsd    dword ptr es:[edi], dword ptr [esi]
008d989e call     dword ptr [ecx + 0x64]
008d98a1 cmp      eax, ebx
008d98a3 jge      0x8d98b3
008d98a5 push     0xbd8348
008d98aa push     dword ptr [ebp - 0x1c]
008d98ad push     eax
008d98ae call     0xa5fdf2
008d98b3 lea      ecx, [ebp - 0x14]
008d98b6 mov      byte ptr [ebp - 4], 0x34
008d98ba call     0x41569e
008d98bf mov      eax, dword ptr [ebp - 0x18]
008d98c2 cmp      dword ptr [eax + 0xbc0], ebx
008d98c8 lea      esi, [eax + 0xbc0]
008d98ce jne      0x8d98da
008d98d0 push     0x80004003
008d98d5 call     0xa5fde4
008d98da mov      ecx, dword ptr [esi]
008d98dc lea      eax, [ebp - 0x14]
008d98df push     eax
008d98e0 call     0x439dcb
008d98e5 mov      esi, eax
008d98e7 cmp      dword ptr [esi], ebx
008d98e9 mov      byte ptr [ebp - 4], 0x3f
008d98ed jne      0x8d98f9
008d98ef push     0x80004003
008d98f4 call     0xa5fde4
008d98f9 mov      eax, dword ptr [esi]
008d98fb sub      esp, 0x10
008d98fe mov      edi, esp
008d9900 mov      dword ptr [ebp - 0x1c], eax
008d9903 mov      ecx, dword ptr [eax]
008d9905 lea      esi, [ebp - 0x74]
008d9908 movsd    dword ptr es:[edi], dword ptr [esi]
008d9909 movsd    dword ptr es:[edi], dword ptr [esi]
008d990a movsd    dword ptr es:[edi], dword ptr [esi]
008d990b push     eax
008d990c movsd    dword ptr es:[edi], dword ptr [esi]
008d990d call     dword ptr [ecx + 0x64]
008d9910 cmp      eax, ebx
008d9912 jge      0x8d9922
008d9914 push     0xbd8348
008d9919 push     dword ptr [ebp - 0x1c]
008d991c push     eax
008d991d call     0xa5fdf2
008d9922 lea      ecx, [ebp - 0x14]
008d9925 mov      byte ptr [ebp - 4], 0x34
008d9929 call     0x41569e
008d992e mov      esi, 0xbf6300
008d9933 push     esi
008d9934 lea      ecx, [ebp - 0x84]
008d993a call     0x402f85
008d993f push     esi
008d9940 lea      ecx, [ebp - 0x64]
008d9943 mov      byte ptr [ebp - 4], 0x40
008d9947 call     0x402f85
008d994c mov      eax, dword ptr [ebp - 0x18]
008d994f lea      esi, [eax + 0xbc0]
008d9955 mov      byte ptr [ebp - 4], 0x41
008d9959 cmp      dword ptr [esi], ebx
008d995b jne      0x8d9967
008d995d push     0x80004003
008d9962 call     0xa5fde4
008d9967 mov      ecx, dword ptr [esi]
008d9969 lea      eax, [ebp - 0x14]
008d996c push     eax
008d996d call     0x439dcb
008d9972 mov      esi, eax
008d9974 cmp      dword ptr [esi], ebx
008d9976 mov      byte ptr [ebp - 4], 0x42
008d997a jne      0x8d9986
008d997c push     0x80004003
008d9981 call     0xa5fde4
008d9986 mov      eax, dword ptr [esi]
008d9988 sub      esp, 0x10
008d998b mov      edi, esp
008d998d mov      dword ptr [ebp - 0x1c], eax
008d9990 mov      ecx, dword ptr [eax]
008d9992 lea      esi, [ebp - 0x84]
008d9998 movsd    dword ptr es:[edi], dword ptr [esi]
008d9999 movsd    dword ptr es:[edi], dword ptr [esi]
008d999a movsd    dword ptr es:[edi], dword ptr [esi]
008d999b movsd    dword ptr es:[edi], dword ptr [esi]
008d999c sub      esp, 0x10
008d999f mov      edi, esp
008d99a1 push     dword ptr [ebp - 0x24]
008d99a4 lea      esi, [ebp - 0x64]
008d99a7 push     dword ptr [ebp - 0x30]
008d99aa movsd    dword ptr es:[edi], dword ptr [esi]
008d99ab movsd    dword ptr es:[edi], dword ptr [esi]
008d99ac movsd    dword ptr es:[edi], dword ptr [esi]
008d99ad push     eax
008d99ae movsd    dword ptr es:[edi], dword ptr [esi]
008d99af call     dword ptr [ecx + 0x90]
008d99b5 cmp      eax, ebx
008d99b7 jge      0x8d99c7
008d99b9 push     0xbd8348
008d99be push     dword ptr [ebp - 0x1c]
008d99c1 push     eax
008d99c2 call     0xa5fdf2
008d99c7 lea      ecx, [ebp - 0x14]
008d99ca call     0x41569e
008d99cf lea      eax, [ebp - 0x64]
008d99d2 push     eax
008d99d3 mov      byte ptr [ebp - 4], 0x40
008d99d7 call     0x40291d
008d99dc cmp      eax, ebx
008d99de pop      ecx
008d99df jge      0x8d99e7
008d99e1 push     eax
008d99e2 call     0xa5fde4
008d99e7 lea      eax, [ebp - 0x84]
008d99ed push     eax
008d99ee mov      byte ptr [ebp - 4], 0x34
008d99f2 call     0x40291d
008d99f7 cmp      eax, ebx
008d99f9 pop      ecx
008d99fa jge      0x8d9a02
008d99fc push     eax
008d99fd call     0xa5fde4
008d9a02 or       dword ptr [ebp - 4], 0xffffffff
008d9a06 lea      eax, [ebp - 0x74]
008d9a09 push     eax
008d9a0a call     0x40291d
008d9a0f cmp      eax, ebx
008d9a11 pop      ecx
008d9a12 jge      0x8d9a1a
008d9a14 push     eax
008d9a15 call     0xa5fde4
008d9a1a mov      eax, dword ptr [ebp + 0x1c]
008d9a1d cdq      
008d9a1e push     ebx
008d9a1f push     0x73
008d9a21 mov      dword ptr [ebp - 0x7c], eax
008d9a24 mov      eax, dword ptr [ebp + 0x18]
008d9a27 pop      esi
008d9a28 mov      dword ptr [ebp - 0x78], edx
008d9a2b cdq      
008d9a2c push     esi
008d9a2d push     edx
008d9a2e push     eax
008d9a2f call     0xa62930
008d9a34 push     dword ptr [ebp - 0x78]
008d9a37 push     dword ptr [ebp - 0x7c]
008d9a3a push     edx
008d9a3b push     eax
008d9a3c call     0xa62b40
008d9a41 sub      esi, eax
008d9a43 push     0
008d9a45 pop      eax
008d9a46 sbb      eax, edx
008d9a48 mov      edi, 0xbf6300
008d9a4d push     edi
008d9a4e lea      ecx, [ebp - 0x9c]
008d9a54 mov      dword ptr [ebp - 0x8c], esi
008d9a5a mov      dword ptr [ebp - 0x88], eax
008d9a60 call     0x402f85
008d9a65 push     edi
008d9a66 lea      ecx, [ebp - 0x64]
008d9a69 mov      dword ptr [ebp - 4], 0x43
008d9a70 call     0x402f85
008d9a75 cmp      esi, ebx
008d9a77 mov      byte ptr [ebp - 4], 0x44
008d9a7b mov      dword ptr [ebp - 0x24], esi
008d9a7e jg       0x8d9a83
008d9a80 mov      dword ptr [ebp - 0x24], ebx
008d9a83 mov      eax, dword ptr [ebp - 0x18]
008d9a86 add      eax, 0xbc0
008d9a8b cmp      dword ptr [eax], ebx
008d9a8d jne      0x8d9a99
008d9a8f push     0x80004003
008d9a94 call     0xa5fde4
008d9a99 mov      eax, dword ptr [ebp - 0x18]
008d9a9c sub      esp, 0x10
008d9a9f add      eax, 0xbc0
008d9aa4 mov      eax, dword ptr [eax]
008d9aa6 mov      edi, esp
008d9aa8 mov      dword ptr [ebp - 0x1c], eax
008d9aab mov      ecx, dword ptr [eax]
008d9aad lea      esi, [ebp - 0x9c]
008d9ab3 movsd    dword ptr es:[edi], dword ptr [esi]
008d9ab4 movsd    dword ptr es:[edi], dword ptr [esi]
008d9ab5 movsd    dword ptr es:[edi], dword ptr [esi]
008d9ab6 movsd    dword ptr es:[edi], dword ptr [esi]
008d9ab7 sub      esp, 0x10
008d9aba mov      edi, esp
008d9abc lea      esi, [ebp - 0x64]
008d9abf movsd    dword ptr es:[edi], dword ptr [esi]
008d9ac0 movsd    dword ptr es:[edi], dword ptr [esi]
008d9ac1 mov      edx, 0x22c
008d9ac6 sub      edx, dword ptr [ebp - 0x24]
008d9ac9 push     0x245
008d9ace movsd    dword ptr es:[edi], dword ptr [esi]
008d9acf push     edx
008d9ad0 push     eax
008d9ad1 movsd    dword ptr es:[edi], dword ptr [esi]
008d9ad2 call     dword ptr [ecx + 0x90]
008d9ad8 cmp      eax, ebx
008d9ada jge      0x8d9aea
008d9adc push     0xbd8348
008d9ae1 push     dword ptr [ebp - 0x1c]
008d9ae4 push     eax
008d9ae5 call     0xa5fdf2
008d9aea lea      eax, [ebp - 0x64]
008d9aed push     eax
008d9aee mov      byte ptr [ebp - 4], 0x43
008d9af2 call     0x40291d
008d9af7 cmp      eax, ebx
008d9af9 pop      ecx
008d9afa jge      0x8d9b02
008d9afc push     eax
008d9afd call     0xa5fde4
008d9b02 or       dword ptr [ebp - 4], 0xffffffff
008d9b06 lea      eax, [ebp - 0x9c]
008d9b0c push     eax
008d9b0d call     0x40291d
008d9b12 cmp      eax, ebx
008d9b14 pop      ecx
008d9b15 jge      0x8d9b1d
008d9b17 push     eax
008d9b18 call     0xa5fde4
008d9b1d mov      esi, 0xbf6300
008d9b22 push     esi
008d9b23 lea      ecx, [ebp - 0x64]
008d9b26 call     0x402f85
008d9b2b push     esi
008d9b2c lea      ecx, [ebp - 0x9c]
008d9b32 mov      dword ptr [ebp - 4], 0x45
008d9b39 call     0x402f85
008d9b3e mov      eax, dword ptr [ebp - 0x8c]
008d9b44 cmp      eax, ebx
008d9b46 mov      byte ptr [ebp - 4], 0x46
008d9b4a mov      dword ptr [ebp - 0x24], eax
008d9b4d jg       0x8d9b52
008d9b4f mov      dword ptr [ebp - 0x24], ebx
008d9b52 mov      eax, dword ptr [ebp - 0x18]
008d9b55 cmp      dword ptr [eax + 0xbc0], ebx
008d9b5b lea      esi, [eax + 0xbc0]
008d9b61 jne      0x8d9b6d
008d9b63 push     0x80004003
008d9b68 call     0xa5fde4
008d9b6d mov      ecx, dword ptr [esi]
008d9b6f lea      eax, [ebp - 0x10]
008d9b72 push     eax
008d9b73 call     0x439dcb
008d9b78 mov      esi, eax
008d9b7a cmp      dword ptr [esi], ebx
008d9b7c mov      byte ptr [ebp - 4], 0x47
008d9b80 jne      0x8d9b8c
008d9b82 push     0x80004003
008d9b87 call     0xa5fde4
008d9b8c mov      eax, dword ptr [esi]
008d9b8e sub      esp, 0x10
008d9b91 mov      edi, esp
008d9b93 mov      dword ptr [ebp - 0x1c], eax
008d9b96 mov      ecx, dword ptr [eax]
008d9b98 lea      esi, [ebp - 0x64]
008d9b9b movsd    dword ptr es:[edi], dword ptr [esi]
008d9b9c movsd    dword ptr es:[edi], dword ptr [esi]
008d9b9d movsd    dword ptr es:[edi], dword ptr [esi]
008d9b9e movsd    dword ptr es:[edi], dword ptr [esi]
008d9b9f sub      esp, 0x10
008d9ba2 mov      edi, esp
008d9ba4 lea      esi, [ebp - 0x9c]
008d9baa movsd    dword ptr es:[edi], dword ptr [esi]
008d9bab movsd    dword ptr es:[edi], dword ptr [esi]
008d9bac push     0xf
008d9bae push     dword ptr [ebp - 0x24]
008d9bb1 movsd    dword ptr es:[edi], dword ptr [esi]
008d9bb2 push     eax
008d9bb3 movsd    dword ptr es:[edi], dword ptr [esi]
008d9bb4 call     dword ptr [ecx + 0x90]
008d9bba cmp      eax, ebx
008d9bbc jge      0x8d9bcc
008d9bbe push     0xbd8348
008d9bc3 push     dword ptr [ebp - 0x1c]
008d9bc6 push     eax
008d9bc7 call     0xa5fdf2
008d9bcc lea      ecx, [ebp - 0x10]
008d9bcf call     0x41569e
008d9bd4 lea      eax, [ebp - 0x9c]
008d9bda push     eax
008d9bdb mov      byte ptr [ebp - 4], 0x45
008d9bdf call     0x40291d
008d9be4 cmp      eax, ebx
008d9be6 pop      ecx
008d9be7 jge      0x8d9bef
008d9be9 push     eax
008d9bea call     0xa5fde4
008d9bef or       dword ptr [ebp - 4], 0xffffffff
008d9bf3 lea      eax, [ebp - 0x64]
008d9bf6 push     eax
008d9bf7 call     0x40291d
008d9bfc cmp      eax, ebx
008d9bfe pop      ecx
008d9bff jge      0x8d9c07
008d9c01 push     eax
008d9c02 call     0xa5fde4
008d9c07 mov      eax, dword ptr [ebp - 0x18]
008d9c0a cmp      dword ptr [eax + 0xbc4], ebx
008d9c10 lea      esi, [eax + 0xbc4]
008d9c16 jne      0x8d9c22
008d9c18 push     0x80004003
008d9c1d call     0xa5fde4
008d9c22 mov      ecx, dword ptr [esi]
008d9c24 lea      eax, [ebp - 0x1c]
008d9c27 push     eax
008d9c28 call     0x440c54
008d9c2d mov      edi, eax
008d9c2f cmp      dword ptr [edi], ebx
008d9c31 mov      dword ptr [ebp - 4], 0x48
008d9c38 jne      0x8d9c44
008d9c3a push     0x80004003
008d9c3f call     0xa5fde4
008d9c44 mov      ecx, dword ptr [edi]
008d9c46 call     0x425aef
008d9c4b or       dword ptr [ebp - 4], 0xffffffff
008d9c4f lea      ecx, [ebp - 0x1c]
008d9c52 mov      dword ptr [ebp - 0x30], eax
008d9c55 call     0x41569e
008d9c5a cmp      dword ptr [esi], ebx
008d9c5c jne      0x8d9c68
008d9c5e push     0x80004003
008d9c63 call     0xa5fde4
008d9c68 mov      ecx, dword ptr [esi]
008d9c6a lea      eax, [ebp - 0x1c]
008d9c6d push     eax
008d9c6e call     0x440c54
008d9c73 mov      edi, eax
008d9c75 cmp      dword ptr [edi], ebx
008d9c77 mov      dword ptr [ebp - 4], 0x49
008d9c7e jne      0x8d9c8a
008d9c80 push     0x80004003
008d9c85 call     0xa5fde4
008d9c8a mov      ecx, dword ptr [edi]
008d9c8c call     0x425b16
008d9c91 or       dword ptr [ebp - 4], 0xffffffff
008d9c95 lea      ecx, [ebp - 0x1c]
008d9c98 mov      dword ptr [ebp - 0x24], eax
008d9c9b call     0x41569e
008d9ca0 cmp      dword ptr [esi], ebx
008d9ca2 jne      0x8d9cae
008d9ca4 push     0x80004003
008d9ca9 call     0xa5fde4
008d9cae mov      ecx, dword ptr [esi]
008d9cb0 lea      eax, [ebp - 0x1c]
008d9cb3 push     eax
008d9cb4 call     0x440c54
008d9cb9 mov      edi, eax
008d9cbb cmp      dword ptr [edi], ebx
008d9cbd mov      dword ptr [ebp - 4], 0x4a
008d9cc4 jne      0x8d9cd0
008d9cc6 push     0x80004003
008d9ccb call     0xa5fde4
008d9cd0 mov      ecx, dword ptr [edi]
008d9cd2 lea      eax, [ebp - 0x64]
008d9cd5 push     eax
008d9cd6 call     0x441aa1
008d9cdb lea      ecx, [ebp - 0x1c]
008d9cde mov      byte ptr [ebp - 4], 0x4c
008d9ce2 call     0x41569e
008d9ce7 cmp      dword ptr [esi], ebx
008d9ce9 jne      0x8d9cf5
008d9ceb push     0x80004003
008d9cf0 call     0xa5fde4
008d9cf5 mov      esi, dword ptr [esi]
008d9cf7 lea      eax, [ebp - 0x14]
008d9cfa push     eax
008d9cfb mov      ecx, esi
008d9cfd call     0x440c54
008d9d02 mov      esi, eax
008d9d04 cmp      dword ptr [esi], ebx
008d9d06 mov      byte ptr [ebp - 4], 0x4d
008d9d0a jne      0x8d9d16
008d9d0c push     0x80004003
008d9d11 call     0xa5fde4
008d9d16 mov      eax, dword ptr [esi]
008d9d18 mov      esi, 0xbf0b70
008d9d1d lea      edi, [ebp - 0x54]
008d9d20 movsd    dword ptr es:[edi], dword ptr [esi]
008d9d21 movsd    dword ptr es:[edi], dword ptr [esi]
008d9d22 movsd    dword ptr es:[edi], dword ptr [esi]
008d9d23 movsd    dword ptr es:[edi], dword ptr [esi]
008d9d24 sub      esp, 0x10
008d9d27 mov      edi, esp
008d9d29 mov      dword ptr [ebp - 0x10], eax
008d9d2c mov      ecx, dword ptr [eax]
008d9d2e lea      esi, [ebp - 0x54]
008d9d31 movsd    dword ptr es:[edi], dword ptr [esi]
008d9d32 movsd    dword ptr es:[edi], dword ptr [esi]
008d9d33 movsd    dword ptr es:[edi], dword ptr [esi]
008d9d34 push     eax
008d9d35 movsd    dword ptr es:[edi], dword ptr [esi]
008d9d36 call     dword ptr [ecx + 0x64]
008d9d39 cmp      eax, ebx
008d9d3b jge      0x8d9d4b
008d9d3d push     0xbd8348
008d9d42 push     dword ptr [ebp - 0x10]
008d9d45 push     eax
008d9d46 call     0xa5fdf2
008d9d4b lea      ecx, [ebp - 0x14]
008d9d4e mov      byte ptr [ebp - 4], 0x4c
008d9d52 call     0x41569e
008d9d57 mov      eax, dword ptr [ebp - 0x18]
008d9d5a cmp      dword ptr [eax + 0xbc4], ebx
008d9d60 lea      esi, [eax + 0xbc4]
008d9d66 jne      0x8d9d72
008d9d68 push     0x80004003
008d9d6d call     0xa5fde4
008d9d72 mov      ecx, dword ptr [esi]
008d9d74 lea      eax, [ebp - 0x14]
008d9d77 push     eax
008d9d78 call     0x440c54
008d9d7d mov      esi, eax
008d9d7f cmp      dword ptr [esi], ebx
008d9d81 mov      byte ptr [ebp - 4], 0x4e
008d9d85 jne      0x8d9d91
008d9d87 push     0x80004003
008d9d8c call     0xa5fde4
008d9d91 mov      eax, dword ptr [esi]
008d9d93 sub      esp, 0x10
008d9d96 mov      edi, esp
008d9d98 mov      dword ptr [ebp - 0x10], eax
008d9d9b mov      ecx, dword ptr [eax]
008d9d9d lea      esi, [ebp - 0x64]
008d9da0 movsd    dword ptr es:[edi], dword ptr [esi]
008d9da1 movsd    dword ptr es:[edi], dword ptr [esi]
008d9da2 movsd    dword ptr es:[edi], dword ptr [esi]
008d9da3 push     eax
008d9da4 movsd    dword ptr es:[edi], dword ptr [esi]
008d9da5 call     dword ptr [ecx + 0x64]
008d9da8 cmp      eax, ebx
008d9daa jge      0x8d9dba
008d9dac push     0xbd8348
008d9db1 push     dword ptr [ebp - 0x10]
008d9db4 push     eax
008d9db5 call     0xa5fdf2
008d9dba lea      ecx, [ebp - 0x14]
008d9dbd mov      byte ptr [ebp - 4], 0x4c
008d9dc1 call     0x41569e
008d9dc6 mov      esi, 0xbf6300
008d9dcb push     esi
008d9dcc lea      ecx, [ebp - 0x44]
008d9dcf call     0x402f85
008d9dd4 push     esi
008d9dd5 lea      ecx, [ebp - 0x9c]
008d9ddb mov      byte ptr [ebp - 4], 0x4f
008d9ddf call     0x402f85
008d9de4 mov      eax, dword ptr [ebp - 0x18]
008d9de7 lea      esi, [eax + 0xbc4]
008d9ded mov      byte ptr [ebp - 4], 0x50
008d9df1 cmp      dword ptr [esi], ebx
008d9df3 jne      0x8d9dff
008d9df5 push     0x80004003
008d9dfa call     0xa5fde4
008d9dff mov      ecx, dword ptr [esi]
008d9e01 lea      eax, [ebp - 0x14]
008d9e04 push     eax
008d9e05 call     0x440c54
008d9e0a mov      esi, eax
008d9e0c cmp      dword ptr [esi], ebx
008d9e0e mov      byte ptr [ebp - 4], 0x51
008d9e12 jne      0x8d9e1e
008d9e14 push     0x80004003
008d9e19 call     0xa5fde4
008d9e1e mov      eax, dword ptr [esi]
008d9e20 sub      esp, 0x10
008d9e23 mov      edi, esp
008d9e25 mov      dword ptr [ebp - 0x10], eax
008d9e28 mov      ecx, dword ptr [eax]
008d9e2a lea      esi, [ebp - 0x44]
008d9e2d movsd    dword ptr es:[edi], dword ptr [esi]
008d9e2e movsd    dword ptr es:[edi], dword ptr [esi]
008d9e2f movsd    dword ptr es:[edi], dword ptr [esi]
008d9e30 movsd    dword ptr es:[edi], dword ptr [esi]
008d9e31 sub      esp, 0x10
008d9e34 mov      edi, esp
008d9e36 push     dword ptr [ebp - 0x24]
008d9e39 lea      esi, [ebp - 0x9c]
008d9e3f push     dword ptr [ebp - 0x30]
008d9e42 movsd    dword ptr es:[edi], dword ptr [esi]
008d9e43 movsd    dword ptr es:[edi], dword ptr [esi]
008d9e44 movsd    dword ptr es:[edi], dword ptr [esi]
008d9e45 push     eax
008d9e46 movsd    dword ptr es:[edi], dword ptr [esi]
008d9e47 call     dword ptr [ecx + 0x90]
008d9e4d cmp      eax, ebx
008d9e4f jge      0x8d9e5f
008d9e51 push     0xbd8348
008d9e56 push     dword ptr [ebp - 0x10]
008d9e59 push     eax
008d9e5a call     0xa5fdf2
008d9e5f lea      ecx, [ebp - 0x14]
008d9e62 call     0x41569e
008d9e67 lea      eax, [ebp - 0x9c]
008d9e6d push     eax
008d9e6e mov      byte ptr [ebp - 4], 0x4f
008d9e72 call     0x40291d
008d9e77 cmp      eax, ebx
008d9e79 pop      ecx
008d9e7a jge      0x8d9e82
008d9e7c push     eax
008d9e7d call     0xa5fde4
008d9e82 lea      eax, [ebp - 0x44]
008d9e85 push     eax
008d9e86 mov      byte ptr [ebp - 4], 0x4c
008d9e8a call     0x40291d
008d9e8f cmp      eax, ebx
008d9e91 pop      ecx
008d9e92 jge      0x8d9e9a
008d9e94 push     eax
008d9e95 call     0xa5fde4
008d9e9a mov      eax, dword ptr [ebp - 0x18]
008d9e9d cmp      dword ptr [eax + 0xbc4], ebx
008d9ea3 lea      edi, [eax + 0xbc4]
008d9ea9 jne      0x8d9eb5
008d9eab push     0x80004003
008d9eb0 call     0xa5fde4
008d9eb5 mov      ecx, dword ptr [edi]
008d9eb7 lea      eax, [ebp - 0x10]
008d9eba push     eax
008d9ebb call     0x439dcb
008d9ec0 mov      esi, eax
008d9ec2 cmp      dword ptr [esi], ebx
008d9ec4 mov      byte ptr [ebp - 4], 0x52
008d9ec8 jne      0x8d9ed4
008d9eca push     0x80004003
008d9ecf call     0xa5fde4
008d9ed4 mov      ecx, dword ptr [esi]
008d9ed6 call     0x425aef
008d9edb lea      ecx, [ebp - 0x10]
008d9ede mov      dword ptr [ebp - 0x30], eax
008d9ee1 mov      byte ptr [ebp - 4], 0x4c
008d9ee5 call     0x41569e
008d9eea cmp      dword ptr [edi], ebx
008d9eec jne      0x8d9ef8
008d9eee push     0x80004003
008d9ef3 call     0xa5fde4
008d9ef8 mov      ecx, dword ptr [edi]
008d9efa lea      eax, [ebp - 0x10]
008d9efd push     eax
008d9efe call     0x439dcb
008d9f03 mov      esi, eax
008d9f05 cmp      dword ptr [esi], ebx
008d9f07 mov      byte ptr [ebp - 4], 0x53
008d9f0b jne      0x8d9f17
008d9f0d push     0x80004003
008d9f12 call     0xa5fde4
008d9f17 mov      ecx, dword ptr [esi]
008d9f19 call     0x425b16
008d9f1e lea      ecx, [ebp - 0x10]
008d9f21 mov      dword ptr [ebp - 0x24], eax
008d9f24 mov      byte ptr [ebp - 4], 0x4c
008d9f28 call     0x41569e
008d9f2d cmp      dword ptr [edi], ebx
008d9f2f jne      0x8d9f3b
008d9f31 push     0x80004003
008d9f36 call     0xa5fde4
008d9f3b mov      ecx, dword ptr [edi]
008d9f3d lea      eax, [ebp - 0x10]
008d9f40 push     eax
008d9f41 call     0x439dcb
008d9f46 mov      esi, eax
008d9f48 cmp      dword ptr [esi], ebx
008d9f4a mov      byte ptr [ebp - 4], 0x54
008d9f4e jne      0x8d9f5a
008d9f50 push     0x80004003
008d9f55 call     0xa5fde4
008d9f5a mov      ecx, dword ptr [esi]
008d9f5c lea      eax, [ebp - 0x54]
008d9f5f push     eax
008d9f60 call     0x441aa1
008d9f65 push     eax
008d9f66 lea      ecx, [ebp - 0x64]
008d9f69 mov      byte ptr [ebp - 4], 0x55
008d9f6d call     0x8daffd
008d9f72 lea      eax, [ebp - 0x54]
008d9f75 push     eax
008d9f76 mov      byte ptr [ebp - 4], 0x54
008d9f7a call     0x40291d
008d9f7f cmp      eax, ebx
008d9f81 pop      ecx
008d9f82 jge      0x8d9f8a
008d9f84 push     eax
008d9f85 call     0xa5fde4
008d9f8a lea      ecx, [ebp - 0x10]
008d9f8d mov      byte ptr [ebp - 4], 0x4c
008d9f91 call     0x41569e
008d9f96 lea      eax, [ebp - 0x10]
008d9f99 push     eax
008d9f9a mov      ecx, edi
008d9f9c call     0x4281fb
008d9fa1 mov      ecx, eax
008d9fa3 call     0x439dcb
008d9fa8 push     0xbf0b70
008d9fad mov      ecx, eax
008d9faf mov      byte ptr [ebp - 4], 0x56
008d9fb3 call     0x432bfa
008d9fb8 mov      ecx, eax
008d9fba call     0x43749d
008d9fbf lea      ecx, [ebp - 0x10]
008d9fc2 mov      byte ptr [ebp - 4], 0x4c
008d9fc6 call     0x41569e
008d9fcb lea      eax, [ebp - 0x10]
008d9fce push     eax
008d9fcf mov      ecx, edi
008d9fd1 call     0x4281fb
008d9fd6 mov      ecx, eax
008d9fd8 call     0x439dcb
008d9fdd lea      ecx, [ebp - 0x64]
008d9fe0 push     ecx
008d9fe1 mov      ecx, eax
008d9fe3 mov      byte ptr [ebp - 4], 0x57
008d9fe7 call     0x432bfa
008d9fec mov      ecx, eax
008d9fee call     0x43749d
008d9ff3 lea      ecx, [ebp - 0x10]
008d9ff6 mov      byte ptr [ebp - 4], 0x4c
008d9ffa call     0x41569e
008d9fff mov      esi, 0xbf6300
008da004 push     esi
008da005 lea      ecx, [ebp - 0x9c]
008da00b call     0x426b85
008da010 push     esi
008da011 lea      ecx, [ebp - 0x44]
008da014 mov      byte ptr [ebp - 4], 0x58
008da018 call     0x426b85
008da01d lea      eax, [ebp - 0x10]
008da020 push     eax
008da021 mov      ecx, edi
008da023 mov      byte ptr [ebp - 4], 0x59
008da027 call     0x4281fb
008da02c mov      ecx, eax
008da02e call     0x439dcb
008da033 lea      ecx, [ebp - 0x9c]
008da039 push     ecx
008da03a lea      ecx, [ebp - 0x44]
008da03d push     ecx
008da03e push     dword ptr [ebp - 0x24]
008da041 mov      ecx, eax
008da043 push     dword ptr [ebp - 0x30]
008da046 mov      byte ptr [ebp - 4], 0x5a
008da04a call     0x432bfa
008da04f mov      ecx, eax
008da051 call     0x432c2d
008da056 lea      ecx, [ebp - 0x10]
008da059 call     0x41569e
008da05e lea      eax, [ebp - 0x44]
008da061 push     eax
008da062 mov      byte ptr [ebp - 4], 0x58
008da066 call     0x40291d
008da06b cmp      eax, ebx
008da06d pop      ecx
008da06e jge      0x8da076
008da070 push     eax
008da071 call     0xa5fde4
008da076 lea      eax, [ebp - 0x9c]
008da07c push     eax
008da07d mov      byte ptr [ebp - 4], 0x4c
008da081 call     0x40291d
008da086 cmp      eax, ebx
008da088 pop      ecx
008da089 jge      0x8da091
008da08b push     eax
008da08c call     0xa5fde4
008da091 or       dword ptr [ebp - 4], 0xffffffff
008da095 lea      eax, [ebp - 0x64]
008da098 push     eax
008da099 call     0x40291d
008da09e cmp      eax, ebx
008da0a0 pop      ecx
008da0a1 jge      0x8da0a9
008da0a3 push     eax
008da0a4 call     0xa5fde4
008da0a9 mov      eax, dword ptr [ebp + 0x20]
008da0ac mov      ecx, dword ptr [ebp + 0x18]
008da0af lea      edx, [ecx + eax]
008da0b2 mov      eax, dword ptr [ebp + 0x1c]
008da0b5 cmp      edx, eax
008da0b7 jle      0x8da0bd
008da0b9 sub      eax, ecx
008da0bb jmp      0x8da0c0
008da0bd mov      eax, dword ptr [ebp + 0x20]
008da0c0 push     ebx
008da0c1 cdq      
008da0c2 push     0x73
008da0c4 push     edx
008da0c5 push     eax
008da0c6 call     0xa62930
008da0cb push     dword ptr [ebp - 0x78]
008da0ce push     dword ptr [ebp - 0x7c]
008da0d1 push     edx
008da0d2 push     eax
008da0d3 call     0xa62b40
008da0d8 push     esi
008da0d9 lea      ecx, [ebp - 0x9c]
008da0df mov      dword ptr [ebp - 0x7c], eax
008da0e2 mov      dword ptr [ebp - 0x78], edx
008da0e5 call     0x426b85
008da0ea push     esi
008da0eb lea      ecx, [ebp - 0x44]
008da0ee mov      dword ptr [ebp - 4], 0x5b
008da0f5 call     0x426b85
008da0fa mov      eax, dword ptr [ebp - 0x8c]
008da100 cmp      eax, ebx
008da102 mov      byte ptr [ebp - 4], 0x5c
008da106 jg       0x8da10a
008da108 xor      eax, eax
008da10a lea      ecx, [ebp - 0x9c]
008da110 push     ecx
008da111 lea      ecx, [ebp - 0x44]
008da114 push     ecx
008da115 mov      ecx, 0x22c
008da11a sub      ecx, eax
008da11c push     0x245
008da121 push     ecx
008da122 mov      ecx, edi
008da124 call     0x4281fb
008da129 mov      ecx, eax
008da12b call     0x432c2d
008da130 lea      eax, [ebp - 0x44]
008da133 push     eax
008da134 mov      byte ptr [ebp - 4], 0x5b
008da138 call     0x40291d
008da13d cmp      eax, ebx
008da13f pop      ecx
008da140 jge      0x8da148
008da142 push     eax
008da143 call     0xa5fde4
008da148 or       dword ptr [ebp - 4], 0xffffffff
008da14c lea      eax, [ebp - 0x9c]
008da152 push     eax
008da153 call     0x40291d
008da158 cmp      eax, ebx
008da15a pop      ecx
008da15b jge      0x8da163
008da15d push     eax
008da15e call     0xa5fde4
008da163 push     esi
008da164 lea      ecx, [ebp - 0x9c]
008da16a call     0x426b85
008da16f push     esi
008da170 lea      ecx, [ebp - 0x44]
008da173 mov      dword ptr [ebp - 4], 0x5d
008da17a call     0x426b85
008da17f cmp      dword ptr [ebp - 0x7c], ebx
008da182 mov      byte ptr [ebp - 4], 0x5e
008da186 jle      0x8da190
008da188 mov      eax, dword ptr [ebp - 0x7c]
008da18b mov      dword ptr [ebp + 0x20], eax
008da18e jmp      0x8da193
008da190 mov      dword ptr [ebp + 0x20], ebx
008da193 lea      eax, [ebp - 0x1c]
008da196 push     eax
008da197 mov      ecx, edi
008da199 call     0x4281fb
008da19e mov      ecx, eax
008da1a0 call     0x439dcb
008da1a5 lea      ecx, [ebp - 0x9c]
008da1ab push     ecx
008da1ac lea      ecx, [ebp - 0x44]
008da1af push     ecx
008da1b0 push     0xf
008da1b2 push     dword ptr [ebp + 0x20]
008da1b5 mov      ecx, eax
008da1b7 mov      byte ptr [ebp - 4], 0x5f
008da1bb call     0x432bfa
008da1c0 mov      ecx, eax
008da1c2 call     0x432c2d
008da1c7 lea      ecx, [ebp - 0x1c]
008da1ca call     0x41569e
008da1cf lea      eax, [ebp - 0x44]
008da1d2 push     eax
008da1d3 mov      byte ptr [ebp - 4], 0x5d
008da1d7 call     0x40291d
008da1dc cmp      eax, ebx
008da1de pop      ecx
008da1df jge      0x8da1e7
008da1e1 push     eax
008da1e2 call     0xa5fde4
008da1e7 or       dword ptr [ebp - 4], 0xffffffff
008da1eb lea      eax, [ebp - 0x9c]
008da1f1 push     eax
008da1f2 call     0x40291d
008da1f7 cmp      eax, ebx
008da1f9 pop      ecx
008da1fa jge      0x8da202
008da1fc push     eax
008da1fd call     0xa5fde4
008da202 fild     dword ptr [ebp + 0x18]
008da205 fmul     qword ptr [0xaf29e0]
008da20b fidiv    dword ptr [ebp + 0x1c]
008da20e fcom     qword ptr [0xb3bb80]
008da214 fnstsw   ax
008da216 sahf     
008da217 jb       0x8da221
008da219 fstp     st(0)
008da21b fld      qword ptr [0xb3bb80]
008da221 mov      ecx, dword ptr [ebp - 0x18]
008da224 lea      eax, [ebp - 0x20]
008da227 fstp     qword ptr [ecx + 0xbc8]
008da22d push     eax
008da22e call     0x425c4c
008da233 push     esi
008da234 lea      ecx, [ebp - 0x9c]
008da23a mov      dword ptr [ebp - 4], 0x60
008da241 call     0x426b85
008da246 push     esi
008da247 lea      ecx, [ebp - 0x44]
008da24a mov      byte ptr [ebp - 4], 0x61
008da24e call     0x426b85
008da253 push     ebx
008da254 push     ebx
008da255 lea      eax, [ebp - 0x9c]
008da25b push     eax
008da25c lea      eax, [ebp - 0x44]
008da25f push     eax
008da260 push     ecx
008da261 mov      eax, esp
008da263 mov      dword ptr [ebp + 0x20], esp
008da266 push     0x96f
008da26b push     eax
008da26c mov      byte ptr [ebp - 4], 0x62
008da270 call     0x79e805
008da275 mov      ecx, eax
008da277 call     0x406292
008da27c lea      eax, [ebp - 0x54]
008da27f push     eax
008da280 mov      byte ptr [ebp - 4], 0x62
008da284 call     0x406472
008da289 mov      ecx, eax
008da28b call     0x402680
008da290 mov      ecx, eax
008da292 call     0x403a93
008da297 mov      ecx, eax
008da299 mov      byte ptr [ebp - 4], 0x64
008da29d call     0x4032b2
008da2a2 mov      dword ptr [ebp + 0x1c], eax
008da2a5 lea      eax, [ebp + 0x1c]
008da2a8 push     eax
008da2a9 lea      ecx, [ebp - 0xa0]
008da2af call     0x428dcc
008da2b4 lea      eax, [ebp - 0x54]
008da2b7 push     eax
008da2b8 mov      byte ptr [ebp - 4], 0x68
008da2bc call     0x40291d
008da2c1 cmp      eax, ebx
008da2c3 pop      ecx
008da2c4 jge      0x8da2cc
008da2c6 push     eax
008da2c7 call     0xa5fde4
008da2cc lea      eax, [ebp - 0x44]
008da2cf push     eax
008da2d0 mov      byte ptr [ebp - 4], 0x67
008da2d4 call     0x40291d
008da2d9 cmp      eax, ebx
008da2db pop      ecx
008da2dc jge      0x8da2e4
008da2de push     eax
008da2df call     0xa5fde4
008da2e4 lea      eax, [ebp - 0x9c]
008da2ea push     eax
008da2eb mov      byte ptr [ebp - 4], 0x66
008da2ef call     0x40291d
008da2f4 cmp      eax, ebx
008da2f6 pop      ecx
008da2f7 jge      0x8da2ff
008da2f9 push     eax
008da2fa call     0xa5fde4
008da2ff push     esi
008da300 lea      ecx, [ebp - 0x74]
008da303 call     0x426b85
008da308 push     esi
008da309 lea      ecx, [ebp - 0x64]
008da30c mov      byte ptr [ebp - 4], 0x69
008da310 call     0x426b85
008da315 push     ebx
008da316 push     ebx
008da317 lea      eax, [ebp - 0x74]
008da31a push     eax
008da31b lea      eax, [ebp - 0x64]
008da31e push     eax
008da31f push     ecx
008da320 mov      eax, esp
008da322 mov      dword ptr [ebp + 0x1c], esp
008da325 push     0x970
008da32a push     eax
008da32b mov      byte ptr [ebp - 4], 0x6a
008da32f call     0x79e805
008da334 mov      ecx, eax
008da336 call     0x406292
008da33b lea      eax, [ebp - 0x84]
008da341 push     eax
008da342 mov      byte ptr [ebp - 4], 0x6a
008da346 call     0x406472
008da34b mov      ecx, eax
008da34d call     0x402680
008da352 mov      ecx, eax
008da354 call     0x403a93
008da359 mov      ecx, eax
008da35b mov      byte ptr [ebp - 4], 0x6c
008da35f call     0x4032b2
008da364 mov      dword ptr [ebp + 0x20], eax
008da367 lea      eax, [ebp + 0x20]
008da36a push     eax
008da36b lea      ecx, [ebp - 0x2c]
008da36e call     0x433363
008da373 lea      eax, [ebp - 0x84]
008da379 push     eax
008da37a mov      byte ptr [ebp - 4], 0x70
008da37e call     0x40291d
008da383 cmp      eax, ebx
008da385 pop      ecx
008da386 jge      0x8da38e
008da388 push     eax
008da389 call     0xa5fde4
008da38e lea      eax, [ebp - 0x64]
008da391 push     eax
008da392 mov      byte ptr [ebp - 4], 0x6f
008da396 call     0x40291d
008da39b cmp      eax, ebx
008da39d pop      ecx
008da39e jge      0x8da3a6
008da3a0 push     eax
008da3a1 call     0xa5fde4
008da3a6 lea      eax, [ebp - 0x74]
008da3a9 push     eax
008da3aa mov      byte ptr [ebp - 4], 0x6e
008da3ae call     0x40291d
008da3b3 cmp      eax, ebx
008da3b5 pop      ecx
008da3b6 jge      0x8da3be
008da3b8 push     eax
008da3b9 call     0xa5fde4
008da3be push     esi
008da3bf lea      ecx, [ebp - 0x44]
008da3c2 call     0x426b85
008da3c7 lea      eax, [ebp - 0x44]
008da3ca push     eax
008da3cb push     dword ptr [ebp - 0xa0]
008da3d1 lea      ecx, [ebp - 0x20]
008da3d4 push     0x220
008da3d9 push     0xda
008da3de mov      byte ptr [ebp - 4], 0x71
008da3e2 call     0x414576
008da3e7 mov      ecx, eax
008da3e9 call     0x424ba7
008da3ee lea      eax, [ebp - 0x44]
008da3f1 push     eax
008da3f2 mov      byte ptr [ebp - 4], 0x6e
008da3f6 call     0x40291d
008da3fb cmp      eax, ebx
008da3fd pop      ecx
008da3fe jge      0x8da406
008da400 push     eax
008da401 call     0xa5fde4
008da406 push     0x20
008da408 pop      eax
008da409 call     0xa61190
008da40e mov      eax, esp
008da410 push     -1
008da412 push     0xa
008da414 push     eax
008da415 push     dword ptr [ebp + 0x18]
008da418 call     0xa61ac0
008da41d add      esp, 0xc
008da420 push     eax
008da421 lea      ecx, [ebp - 0x34]
008da424 call     0x428db3
008da429 mov      eax, dword ptr [ebp - 0x34]
008da42c cmp      eax, ebx
008da42e je       0x8da435
008da430 mov      eax, dword ptr [eax - 4]
008da433 jmp      0x8da437
008da435 xor      eax, eax
008da437 lea      eax, [eax + eax*2]
008da43a shl      eax, 1
008da43c push     1
008da43e push     ecx
008da43f mov      dword ptr [ebp + 0x18], eax
008da442 lea      eax, [ebp - 0x2c]
008da445 mov      ecx, esp
008da447 mov      dword ptr [ebp + 0x1c], esp
008da44a push     eax
008da44b call     0x4281c2
008da450 push     ecx
008da451 lea      eax, [ebp - 0x34]
008da454 mov      ecx, esp
008da456 mov      dword ptr [ebp - 0x1c], esp
008da459 push     eax
008da45a mov      byte ptr [ebp - 4], 0x73
008da45e call     0x428211
008da463 mov      esi, 0x225
008da468 push     esi
008da469 push     0x1d2
008da46e push     ecx
008da46f lea      eax, [ebp - 0x20]
008da472 mov      ecx, esp
008da474 mov      dword ptr [ebp + 0x1c], esp
008da477 push     eax
008da478 call     0x4281dd
008da47d mov      byte ptr [ebp - 4], 0x72
008da481 call     0x98812c
008da486 add      esp, 0x18
008da489 push     3
008da48b mov      edi, 0xff
008da490 push     edi
008da491 lea      ecx, [ebp - 0x44]
008da494 call     0x424b91
008da499 push     ebx
008da49a push     ebx
008da49b push     ecx
008da49c mov      eax, esp
008da49e mov      dword ptr [ebp - 0x10], esp
008da4a1 push     0x971
008da4a6 push     eax
008da4a7 mov      byte ptr [ebp - 4], 0x74
008da4ab call     0x79e805
008da4b0 mov      ecx, eax
008da4b2 call     0x406292
008da4b7 lea      eax, [ebp - 0x54]
008da4ba push     eax
008da4bb lea      ecx, [ebp - 0x2c]
008da4be mov      byte ptr [ebp - 4], 0x74
008da4c2 call     0x40263b
008da4c7 mov      ecx, eax
008da4c9 call     0x403935
008da4ce mov      ecx, eax
008da4d0 mov      byte ptr [ebp - 4], 0x76
008da4d4 call     0x4032b2
008da4d9 mov      dword ptr [ebp + 0x1c], eax
008da4dc lea      eax, [ebp + 0x1c]
008da4df push     eax
008da4e0 lea      ecx, [ebp - 0x1c]
008da4e3 call     0x428dcc
008da4e8 mov      eax, dword ptr [eax]
008da4ea lea      ecx, [ebp - 0x44]
008da4ed push     ecx
008da4ee push     eax
008da4ef mov      eax, dword ptr [ebp + 0x18]
008da4f2 push     0x224
008da4f7 add      eax, 0x1d2
008da4fc push     eax
008da4fd lea      ecx, [ebp - 0x20]
008da500 mov      byte ptr [ebp - 4], 0x77
008da504 call     0x414576
008da509 mov      ecx, eax
008da50b call     0x424ba7
008da510 lea      ecx, [ebp - 0x1c]
008da513 call     0x4156ab
008da518 mov      byte ptr [ebp - 4], 0x74
008da51c lea      eax, [ebp - 0x54]
008da51f push     eax
008da520 call     0x40291d
008da525 cmp      eax, ebx
008da527 pop      ecx
008da528 jge      0x8da530
008da52a push     eax
008da52b call     0xa5fde4
008da530 lea      eax, [ebp - 0x44]
008da533 push     eax
008da534 mov      byte ptr [ebp - 4], 0x72
008da538 call     0x40291d
008da53d cmp      eax, ebx
008da53f pop      ecx
008da540 jge      0x8da548
008da542 push     eax
008da543 call     0xa5fde4
008da548 mov      eax, dword ptr [ebp - 0x18]
008da54b fld      qword ptr [eax + 0xbc8]
008da551 push     ecx
008da552 push     ecx
008da553 fstp     qword ptr [esp]
008da556 call     0xa634a7
008da55b fcomp    qword ptr [0xaf82a0]
008da561 pop      ecx
008da562 pop      ecx
008da563 mov      dword ptr [ebp + 0x1c], 6
008da56a fnstsw   ax
008da56c sahf     
008da56d jae      0x8da572
008da56f mov      dword ptr [ebp + 0x1c], ebx
008da572 push     1
008da574 push     ecx
008da575 lea      eax, [ebp - 0x2c]
008da578 mov      ecx, esp
008da57a mov      dword ptr [ebp - 0x1c], esp
008da57d push     eax
008da57e call     0x4281c2
008da583 mov      eax, dword ptr [ebp - 0x18]
008da586 fld      qword ptr [eax + 0xbc8]
008da58c push     ecx
008da58d push     ecx
008da58e fstp     qword ptr [esp]
008da591 call     0xa634a7
008da596 pop      ecx
008da597 pop      ecx
008da598 call     0xa62018
008da59d push     eax
008da59e mov      eax, dword ptr [ebp + 0x18]
008da5a1 push     esi
008da5a2 add      eax, 0x1d6
008da5a7 push     eax
008da5a8 push     ecx
008da5a9 lea      eax, [ebp - 0x20]
008da5ac mov      ecx, esp
008da5ae mov      dword ptr [ebp - 0x1c], esp
008da5b1 push     eax
008da5b2 call     0x4281dd
008da5b7 call     0x988345
008da5bc add      esp, 0x18
008da5bf push     0xbf6300
008da5c4 lea      ecx, [ebp - 0x9c]
008da5ca call     0x426b85
008da5cf push     0xbf6300
008da5d4 lea      ecx, [ebp - 0x44]
008da5d7 mov      byte ptr [ebp - 4], 0x78
008da5db call     0x426b85
008da5e0 lea      eax, [ebp - 0x9c]
008da5e6 push     eax
008da5e7 lea      eax, [ebp - 0x44]
008da5ea push     eax
008da5eb mov      eax, dword ptr [ebp - 0x18]
008da5ee lea      ecx, [eax + 0xbe0]
008da5f4 mov      byte ptr [ebp - 4], 0x79
008da5f8 call     0x4284a5
008da5fd push     eax
008da5fe push     ecx
008da5ff mov      ecx, esp
008da601 mov      dword ptr [ebp - 0x1c], esp
008da604 push     0xb3bb7c
008da609 call     0x403382
008da60e mov      eax, dword ptr [ebp + 0x18]
008da611 mov      ecx, dword ptr [ebp + 0x1c]
008da614 lea      eax, [ecx + eax + 6]
008da618 mov      dword ptr [ebp + 0x18], eax
008da61b push     0x220
008da620 add      eax, 0x1d6
008da625 push     eax
008da626 lea      ecx, [ebp - 0x20]
008da629 mov      byte ptr [ebp - 4], 0x79
008da62d call     0x414576
008da632 mov      ecx, eax
008da634 call     0x4277ad
008da639 lea      eax, [ebp - 0x44]
008da63c push     eax
008da63d mov      byte ptr [ebp - 4], 0x78
008da641 call     0x40291d
008da646 cmp      eax, ebx
008da648 pop      ecx
008da649 jge      0x8da651
008da64b push     eax
008da64c call     0xa5fde4
008da651 lea      eax, [ebp - 0x9c]
008da657 push     eax
008da658 mov      byte ptr [ebp - 4], 0x72
008da65c call     0x40291d
008da661 cmp      eax, ebx
008da663 pop      ecx
008da664 jge      0x8da66c
008da666 push     eax
008da667 call     0xa5fde4
008da66c mov      eax, dword ptr [ebp - 0x18]
008da66f fld      qword ptr [eax + 0xbc8]
008da675 push     ecx
008da676 push     ecx
008da677 fstp     qword ptr [esp]
008da67a call     0xa634a7
008da67f mov      eax, dword ptr [ebp - 0x18]
008da682 fsubr    qword ptr [eax + 0xbc8]
008da688 pop      ecx
008da689 pop      ecx
008da68a fmul     qword ptr [0xaf29e0]
008da690 call     0xa62018
008da695 cmp      eax, 0xa
008da698 jge      0x8da799
008da69e push     3
008da6a0 push     edi
008da6a1 lea      ecx, [ebp - 0x44]
008da6a4 call     0x424b91
008da6a9 push     ebx
008da6aa push     ebx
008da6ab push     ecx
008da6ac mov      ecx, esp
008da6ae mov      dword ptr [ebp - 0x10], esp
008da6b1 push     0xaf1e24
008da6b6 mov      byte ptr [ebp - 4], 0x7b
008da6ba call     0x403382
008da6bf lea      eax, [ebp - 0x54]
008da6c2 push     eax
008da6c3 lea      ecx, [ebp - 0x2c]
008da6c6 mov      byte ptr [ebp - 4], 0x7b
008da6ca call     0x40263b
008da6cf mov      ecx, eax
008da6d1 call     0x403935
008da6d6 mov      ecx, eax
008da6d8 mov      byte ptr [ebp - 4], 0x7d
008da6dc call     0x4032b2
008da6e1 mov      dword ptr [ebp + 0x1c], eax
008da6e4 lea      eax, [ebp + 0x1c]
008da6e7 push     eax
008da6e8 lea      ecx, [ebp - 0x1c]
008da6eb call     0x428dcc
008da6f0 mov      eax, dword ptr [eax]
008da6f2 lea      ecx, [ebp - 0x44]
008da6f5 push     ecx
008da6f6 push     eax
008da6f7 mov      eax, dword ptr [ebp + 0x18]
008da6fa push     esi
008da6fb add      eax, 0x1da
008da700 push     eax
008da701 lea      ecx, [ebp - 0x20]
008da704 mov      byte ptr [ebp - 4], 0x7e
008da708 call     0x414576
008da70d mov      ecx, eax
008da70f call     0x424ba7
008da714 lea      ecx, [ebp - 0x1c]
008da717 call     0x4156ab
008da71c lea      eax, [ebp - 0x54]
008da71f push     eax
008da720 mov      byte ptr [ebp - 4], 0x7b
008da724 call     0x40291d
008da729 cmp      eax, ebx
008da72b pop      ecx
008da72c jge      0x8da734
008da72e push     eax
008da72f call     0xa5fde4
008da734 lea      eax, [ebp - 0x44]
008da737 push     eax
008da738 mov      byte ptr [ebp - 4], 0x72
008da73c call     0x40291d
008da741 cmp      eax, ebx
008da743 pop      ecx
008da744 jge      0x8da74c
008da746 push     eax
008da747 call     0xa5fde4
008da74c push     1
008da74e push     ecx
008da74f lea      eax, [ebp - 0x2c]
008da752 mov      ecx, esp
008da754 mov      dword ptr [ebp + 0x1c], esp
008da757 push     eax
008da758 call     0x4281c2
008da75d mov      eax, dword ptr [ebp - 0x18]
008da760 fld      qword ptr [eax + 0xbc8]
008da766 push     ecx
008da767 push     ecx
008da768 fstp     qword ptr [esp]
008da76b call     0xa634a7
008da770 mov      eax, dword ptr [ebp - 0x18]
008da773 fsubr    qword ptr [eax + 0xbc8]
008da779 pop      ecx
008da77a pop      ecx
008da77b fmul     qword ptr [0xaf29e0]
008da781 call     0xa62018
008da786 push     eax
008da787 mov      eax, dword ptr [ebp + 0x18]
008da78a push     esi
008da78b add      eax, 0x1e0
008da790 push     eax
008da791 push     ecx
008da792 mov      ecx, esp
008da794 mov      dword ptr [ebp + 0x1c], esp
008da797 jmp      0x8da7e4
008da799 push     1
008da79b push     ecx
008da79c lea      eax, [ebp - 0x2c]
008da79f mov      ecx, esp
008da7a1 mov      dword ptr [ebp + 0x1c], esp
008da7a4 push     eax
008da7a5 call     0x4281c2
008da7aa mov      eax, dword ptr [ebp - 0x18]
008da7ad fld      qword ptr [eax + 0xbc8]
008da7b3 push     ecx
008da7b4 push     ecx
008da7b5 fstp     qword ptr [esp]
008da7b8 call     0xa634a7
008da7bd mov      eax, dword ptr [ebp - 0x18]
008da7c0 fsubr    qword ptr [eax + 0xbc8]
008da7c6 pop      ecx
008da7c7 pop      ecx
008da7c8 fmul     qword ptr [0xaf29e0]
008da7ce call     0xa62018
008da7d3 push     eax
008da7d4 mov      eax, dword ptr [ebp + 0x18]
008da7d7 push     esi
008da7d8 add      eax, 0x1da
008da7dd push     eax
008da7de push     ecx
008da7df mov      ecx, esp
008da7e1 mov      dword ptr [ebp + 0x1c], esp
008da7e4 lea      eax, [ebp - 0x20]
008da7e7 push     eax
008da7e8 call     0x4281dd
008da7ed call     0x988345
008da7f2 add      esp, 0x18
008da7f5 push     3
008da7f7 push     edi
008da7f8 lea      ecx, [ebp - 0x44]
008da7fb call     0x424b91
008da800 push     ebx
008da801 push     ebx
008da802 push     ecx
008da803 mov      eax, esp
008da805 mov      dword ptr [ebp - 0x10], esp
008da808 push     0x973
008da80d push     eax
008da80e mov      byte ptr [ebp - 4], 0x7f
008da812 call     0x79e805
008da817 mov      ecx, eax
008da819 call     0x406292
008da81e lea      eax, [ebp - 0x54]
008da821 push     eax
008da822 lea      ecx, [ebp - 0x2c]
008da825 mov      byte ptr [ebp - 4], 0x7f
008da829 call     0x40263b
008da82e mov      ecx, eax
008da830 call     0x403935
008da835 mov      ecx, eax
008da837 mov      byte ptr [ebp - 4], 0x81
008da83b call     0x4032b2
008da840 mov      dword ptr [ebp + 0x1c], eax
008da843 lea      eax, [ebp + 0x1c]
008da846 push     eax
008da847 lea      ecx, [ebp - 0x1c]
008da84a call     0x428dcc
008da84f mov      eax, dword ptr [eax]
008da851 lea      ecx, [ebp - 0x44]
008da854 push     ecx
008da855 push     eax
008da856 mov      eax, dword ptr [ebp + 0x18]
008da859 push     esi
008da85a add      eax, 0x1e6
008da85f push     eax
008da860 lea      ecx, [ebp - 0x20]
008da863 mov      byte ptr [ebp - 4], 0x82
008da867 call     0x414576
008da86c mov      ecx, eax
008da86e call     0x424ba7
008da873 lea      ecx, [ebp - 0x1c]
008da876 call     0x4156ab
008da87b lea      eax, [ebp - 0x54]
008da87e push     eax
008da87f mov      byte ptr [ebp - 4], 0x7f
008da883 call     0x40291d
008da888 cmp      eax, ebx
008da88a pop      ecx
008da88b jge      0x8da893
008da88d push     eax
008da88e call     0xa5fde4
008da893 lea      eax, [ebp - 0x44]
008da896 push     eax
008da897 mov      byte ptr [ebp - 4], 0x72
008da89b call     0x40291d
008da8a0 cmp      eax, ebx
008da8a2 pop      ecx
008da8a3 jge      0x8da8ab
008da8a5 push     eax
008da8a6 call     0xa5fde4
008da8ab push     3
008da8ad push     edi
008da8ae lea      ecx, [ebp - 0x44]
008da8b1 call     0x424b91
008da8b6 push     ebx
008da8b7 push     ebx
008da8b8 push     ecx
008da8b9 mov      eax, esp
008da8bb mov      dword ptr [ebp - 0x10], esp
008da8be push     0x972
008da8c3 push     eax
008da8c4 mov      byte ptr [ebp - 4], 0x83
008da8c8 call     0x79e805
008da8cd mov      ecx, eax
008da8cf call     0x406292
008da8d4 lea      eax, [ebp - 0x54]
008da8d7 push     eax
008da8d8 lea      ecx, [ebp - 0x2c]
008da8db mov      byte ptr [ebp - 4], 0x83
008da8df call     0x40263b
008da8e4 mov      ecx, eax
008da8e6 call     0x403935
008da8eb mov      ecx, eax
008da8ed mov      byte ptr [ebp - 4], 0x85
008da8f1 call     0x4032b2
008da8f6 mov      dword ptr [ebp + 0x1c], eax
008da8f9 lea      eax, [ebp + 0x1c]
008da8fc push     eax
008da8fd lea      ecx, [ebp - 0x1c]
008da900 call     0x428dcc
008da905 mov      eax, dword ptr [eax]
008da907 lea      ecx, [ebp - 0x44]
008da90a push     ecx
008da90b push     eax
008da90c mov      eax, dword ptr [ebp + 0x18]
008da90f push     0x224
008da914 add      eax, 0x1ee
008da919 push     eax
008da91a lea      ecx, [ebp - 0x20]
008da91d mov      byte ptr [ebp - 4], 0x86
008da921 call     0x414576
008da926 mov      ecx, eax
008da928 call     0x424ba7
008da92d lea      ecx, [ebp - 0x1c]
008da930 call     0x4156ab
008da935 lea      eax, [ebp - 0x54]
008da938 push     eax
008da939 mov      byte ptr [ebp - 4], 0x83
008da93d call     0x40291d
008da942 cmp      eax, ebx
008da944 pop      ecx
008da945 jge      0x8da94d
008da947 push     eax
008da948 call     0xa5fde4
008da94d lea      eax, [ebp - 0x44]
008da950 push     eax
008da951 mov      byte ptr [ebp - 4], 0x72
008da955 call     0x40291d
008da95a cmp      eax, ebx
008da95c pop      ecx
008da95d jge      0x8da965
008da95f push     eax
008da960 call     0xa5fde4
008da965 push     3
008da967 push     edi
008da968 lea      ecx, [ebp - 0x44]
008da96b call     0x424b91
008da970 push     ebx
008da971 push     ebx
008da972 push     ecx
008da973 mov      eax, esp
008da975 mov      dword ptr [ebp - 0x1c], esp
008da978 push     0x971
008da97d push     eax
008da97e mov      byte ptr [ebp - 4], 0x87
008da982 call     0x79e805
008da987 mov      ecx, eax
008da989 call     0x406292
008da98e lea      eax, [ebp - 0x54]
008da991 push     eax
008da992 lea      ecx, [ebp - 0x2c]
008da995 mov      byte ptr [ebp - 4], 0x87
008da999 call     0x40263b
008da99e mov      ecx, eax
008da9a0 call     0x403935
008da9a5 mov      ecx, eax
008da9a7 mov      byte ptr [ebp - 4], 0x89
008da9ab call     0x4032b2
008da9b0 mov      dword ptr [ebp + 0x18], eax
008da9b3 lea      eax, [ebp + 0x18]
008da9b6 push     eax
008da9b7 lea      ecx, [ebp + 0x1c]
008da9ba call     0x428dcc
008da9bf mov      eax, dword ptr [eax]
008da9c1 lea      ecx, [ebp - 0x44]
008da9c4 push     ecx
008da9c5 push     eax
008da9c6 push     0x224
008da9cb push     0xed
008da9d0 lea      ecx, [ebp - 0x20]
008da9d3 mov      byte ptr [ebp - 4], 0x8a
008da9d7 call     0x414576
008da9dc mov      ecx, eax
008da9de call     0x424ba7
008da9e3 lea      ecx, [ebp + 0x1c]
008da9e6 call     0x4156ab
008da9eb lea      eax, [ebp - 0x54]
008da9ee push     eax
008da9ef mov      byte ptr [ebp - 4], 0x87
008da9f3 call     0x40291d
008da9f8 cmp      eax, ebx
008da9fa pop      ecx
008da9fb jge      0x8daa03
008da9fd push     eax
008da9fe call     0xa5fde4
008daa03 lea      eax, [ebp - 0x44]
008daa06 push     eax
008daa07 mov      byte ptr [ebp - 4], 0x72
008daa0b call     0x40291d
008daa10 cmp      eax, ebx
008daa12 pop      ecx
008daa13 jge      0x8daa1b
008daa15 push     eax
008daa16 call     0xa5fde4
008daa1b push     0x20
008daa1d pop      eax
008daa1e call     0xa61190
008daa23 mov      eax, esp
008daa25 push     0xa
008daa27 push     eax
008daa28 push     dword ptr [ebp + 8]
008daa2b call     0xa61ac0
008daa30 add      esp, 0xc
008daa33 push     -1
008daa35 push     eax
008daa36 lea      ecx, [ebp - 0x34]
008daa39 call     0x414617
008daa3e mov      eax, dword ptr [ebp - 0x34]
008daa41 cmp      eax, ebx
008daa43 je       0x8daa4a
008daa45 mov      eax, dword ptr [eax - 4]
008daa48 jmp      0x8daa4c
008daa4a xor      eax, eax
008daa4c lea      eax, [eax + eax*2]
008daa4f shl      eax, 1
008daa51 push     1
008daa53 push     ecx
008daa54 mov      dword ptr [ebp + 0x18], eax
008daa57 lea      eax, [ebp - 0x2c]
008daa5a mov      ecx, esp
008daa5c mov      dword ptr [ebp + 8], esp
008daa5f push     eax
008daa60 call     0x4281c2
008daa65 push     ecx
008daa66 lea      eax, [ebp - 0x34]
008daa69 mov      ecx, esp
008daa6b mov      dword ptr [ebp + 0x1c], esp
008daa6e push     eax
008daa6f mov      byte ptr [ebp - 4], 0x8b
008daa73 call     0x428211
008daa78 push     esi
008daa79 push     0xf1
008daa7e push     ecx
008daa7f lea      eax, [ebp - 0x20]
008daa82 mov      ecx, esp
008daa84 mov      dword ptr [ebp + 8], esp
008daa87 push     eax
008daa88 call     0x4281dd
008daa8d mov      byte ptr [ebp - 4], 0x72
008daa91 call     0x98812c
008daa96 add      esp, 0x18
008daa99 push     3
008daa9b push     edi
008daa9c lea      ecx, [ebp - 0x44]
008daa9f call     0x424b91
008daaa4 push     ebx
008daaa5 push     ebx
008daaa6 push     ecx
008daaa7 mov      eax, esp
008daaa9 mov      dword ptr [ebp - 0x1c], esp
008daaac push     0x974
008daab1 push     eax
008daab2 mov      byte ptr [ebp - 4], 0x8c
008daab6 call     0x79e805
008daabb mov      ecx, eax
008daabd call     0x406292
008daac2 lea      eax, [ebp - 0x54]
008daac5 push     eax
008daac6 lea      ecx, [ebp - 0x2c]
008daac9 mov      byte ptr [ebp - 4], 0x8c
008daacd call     0x40263b
008daad2 mov      ecx, eax
008daad4 call     0x403935
008daad9 mov      ecx, eax
008daadb mov      byte ptr [ebp - 4], 0x8e
008daadf call     0x4032b2
008daae4 mov      dword ptr [ebp + 8], eax
008daae7 lea      eax, [ebp + 8]
008daaea push     eax
008daaeb lea      ecx, [ebp + 0x1c]
008daaee call     0x428dcc
008daaf3 mov      eax, dword ptr [eax]
008daaf5 lea      ecx, [ebp - 0x44]
008daaf8 push     ecx
008daaf9 push     eax
008daafa mov      eax, dword ptr [ebp + 0x18]
008daafd push     esi
008daafe add      eax, 0xf1
008dab03 push     eax
008dab04 lea      ecx, [ebp - 0x20]
008dab07 mov      byte ptr [ebp - 4], 0x8f
008dab0b call     0x414576
008dab10 mov      ecx, eax
008dab12 call     0x424ba7
008dab17 lea      ecx, [ebp + 0x1c]
008dab1a call     0x4156ab
008dab1f lea      eax, [ebp - 0x54]
008dab22 mov      byte ptr [ebp - 4], 0x8c
008dab26 push     eax
008dab27 call     0x40291d
008dab2c cmp      eax, ebx
008dab2e pop      ecx
008dab2f jge      0x8dab37
008dab31 push     eax
008dab32 call     0xa5fde4
008dab37 lea      eax, [ebp - 0x44]
008dab3a push     eax
008dab3b mov      byte ptr [ebp - 4], 0x72
008dab3f call     0x40291d
008dab44 cmp      eax, ebx
008dab46 pop      ecx
008dab47 jge      0x8dab4f
008dab49 push     eax
008dab4a call     0xa5fde4
008dab4f push     0x20
008dab51 pop      eax
008dab52 call     0xa61190
008dab57 mov      eax, esp
008dab59 push     0xa
008dab5b push     eax
008dab5c push     dword ptr [ebp + 0xc]
008dab5f call     0xa61ac0
008dab64 add      esp, 0xc
008dab67 push     -1
008dab69 push     eax
008dab6a lea      ecx, [ebp - 0x34]
008dab6d call     0x414617
008dab72 mov      eax, dword ptr [ebp - 0x34]
008dab75 cmp      eax, ebx
008dab77 je       0x8dab7e
008dab79 mov      eax, dword ptr [eax - 4]
008dab7c jmp      0x8dab80
008dab7e xor      eax, eax
008dab80 lea      eax, [eax + eax*2]
008dab83 shl      eax, 1
008dab85 push     1
008dab87 push     ecx
008dab88 mov      dword ptr [ebp + 8], eax
008dab8b lea      eax, [ebp - 0x2c]
008dab8e mov      ecx, esp
008dab90 mov      dword ptr [ebp + 0xc], esp
008dab93 push     eax
008dab94 call     0x4281c2
008dab99 push     ecx
008dab9a lea      eax, [ebp - 0x34]
008dab9d mov      ecx, esp
008dab9f mov      dword ptr [ebp + 0x1c], esp
008daba2 push     eax
008daba3 mov      byte ptr [ebp - 4], 0x90
008daba7 call     0x428211
008dabac mov      eax, dword ptr [ebp + 0x18]
008dabaf push     esi
008dabb0 add      eax, 0xf9
008dabb5 push     eax
008dabb6 push     ecx
008dabb7 lea      eax, [ebp - 0x20]
008dabba mov      ecx, esp
008dabbc mov      dword ptr [ebp + 0xc], esp
008dabbf push     eax
008dabc0 call     0x4281dd
008dabc5 mov      byte ptr [ebp - 4], 0x72
008dabc9 call     0x98812c
008dabce add      esp, 0x18
008dabd1 push     3
008dabd3 push     edi
008dabd4 lea      ecx, [ebp - 0x44]
008dabd7 call     0x424b91
008dabdc push     ebx
008dabdd push     ebx
008dabde push     ecx
008dabdf mov      eax, esp
008dabe1 mov      dword ptr [ebp - 0x1c], esp
008dabe4 push     0x972
008dabe9 push     eax
008dabea mov      byte ptr [ebp - 4], 0x91
008dabee call     0x79e805
008dabf3 mov      ecx, eax
008dabf5 call     0x406292
008dabfa lea      eax, [ebp - 0x54]
008dabfd push     eax
008dabfe lea      ecx, [ebp - 0x2c]
008dac01 mov      byte ptr [ebp - 4], 0x91
008dac05 call     0x40263b
008dac0a mov      ecx, eax
008dac0c call     0x403935
008dac11 mov      ecx, eax
008dac13 mov      byte ptr [ebp - 4], 0x93
008dac17 call     0x4032b2
008dac1c mov      dword ptr [ebp + 0xc], eax
008dac1f lea      eax, [ebp + 0xc]
008dac22 push     eax
008dac23 lea      ecx, [ebp + 0x1c]
008dac26 call     0x428dcc
008dac2b mov      eax, dword ptr [eax]
008dac2d lea      ecx, [ebp - 0x44]
008dac30 push     ecx
008dac31 mov      ecx, dword ptr [ebp + 0x18]
008dac34 push     eax
008dac35 mov      eax, dword ptr [ebp + 8]
008dac38 lea      eax, [eax + ecx + 0xfa]
008dac3f push     0x224
008dac44 push     eax
008dac45 lea      ecx, [ebp - 0x20]
008dac48 mov      byte ptr [ebp - 4], 0x94
008dac4c call     0x414576
008dac51 mov      ecx, eax
008dac53 call     0x424ba7
008dac58 lea      ecx, [ebp + 0x1c]
008dac5b call     0x4156ab
008dac60 lea      eax, [ebp - 0x54]
008dac63 push     eax
008dac64 mov      byte ptr [ebp - 4], 0x91
008dac68 call     0x40291d
008dac6d cmp      eax, ebx
008dac6f pop      ecx
008dac70 jge      0x8dac78
008dac72 push     eax
008dac73 call     0xa5fde4
008dac78 lea      eax, [ebp - 0x44]
008dac7b push     eax
008dac7c mov      byte ptr [ebp - 4], 0x72
008dac80 call     0x40291d
008dac85 cmp      eax, ebx
008dac87 pop      ecx
008dac88 jge      0x8dac90
008dac8a push     eax
008dac8b call     0xa5fde4
008dac90 push     3
008dac92 push     edi
008dac93 lea      ecx, [ebp - 0x44]
008dac96 call     0x424b91
008dac9b push     ebx
008dac9c push     ebx
008dac9d push     ecx
008dac9e mov      eax, esp
008daca0 mov      dword ptr [ebp + 0xc], esp
008daca3 push     0x971
008daca8 push     eax
008daca9 mov      byte ptr [ebp - 4], 0x95
008dacad call     0x79e805
008dacb2 mov      ecx, eax
008dacb4 call     0x406292
008dacb9 lea      eax, [ebp - 0x54]
008dacbc push     eax
008dacbd lea      ecx, [ebp - 0x2c]
008dacc0 mov      byte ptr [ebp - 4], 0x95
008dacc4 call     0x40263b
008dacc9 mov      ecx, eax
008daccb call     0x403935
008dacd0 mov      ecx, eax
008dacd2 mov      byte ptr [ebp - 4], 0x97
008dacd6 call     0x4032b2
008dacdb mov      dword ptr [ebp + 8], eax
008dacde lea      eax, [ebp + 8]
008dace1 push     eax
008dace2 lea      ecx, [ebp + 0x18]
008dace5 call     0x428dcc
008dacea mov      eax, dword ptr [eax]
008dacec lea      ecx, [ebp - 0x44]
008dacef push     ecx
008dacf0 push     eax
008dacf1 push     0x224
008dacf6 push     0x15d
008dacfb lea      ecx, [ebp - 0x20]
008dacfe mov      byte ptr [ebp - 4], 0x98
008dad02 call     0x414576
008dad07 mov      ecx, eax
008dad09 call     0x424ba7
008dad0e lea      ecx, [ebp + 0x18]
008dad11 call     0x4156ab
008dad16 lea      eax, [ebp - 0x54]
008dad19 push     eax
008dad1a mov      byte ptr [ebp - 4], 0x95
008dad1e call     0x40291d
008dad23 cmp      eax, ebx
008dad25 pop      ecx
008dad26 jge      0x8dad2e
008dad28 push     eax
008dad29 call     0xa5fde4
008dad2e lea      eax, [ebp - 0x44]
008dad31 push     eax
008dad32 mov      byte ptr [ebp - 4], 0x72
008dad36 call     0x40291d
008dad3b cmp      eax, ebx
008dad3d pop      ecx
008dad3e jge      0x8dad46
008dad40 push     eax
008dad41 call     0xa5fde4
008dad46 push     0x20
008dad48 pop      eax
008dad49 call     0xa61190
008dad4e mov      eax, esp
008dad50 push     0xa
008dad52 push     eax
008dad53 push     dword ptr [ebp + 0x10]
008dad56 call     0xa61ac0
008dad5b add      esp, 0xc
008dad5e push     -1
008dad60 push     eax
008dad61 lea      ecx, [ebp - 0x34]
008dad64 call     0x414617
008dad69 mov      eax, dword ptr [ebp - 0x34]
008dad6c cmp      eax, ebx
008dad6e je       0x8dad75
008dad70 mov      eax, dword ptr [eax - 4]
008dad73 jmp      0x8dad77
008dad75 xor      eax, eax
008dad77 lea      eax, [eax + eax*2]
008dad7a shl      eax, 1
008dad7c push     1
008dad7e push     ecx
008dad7f mov      dword ptr [ebp + 0x18], eax
008dad82 lea      eax, [ebp - 0x2c]
008dad85 mov      ecx, esp
008dad87 mov      dword ptr [ebp + 8], esp
008dad8a push     eax
008dad8b call     0x4281c2
008dad90 push     ecx
008dad91 lea      eax, [ebp - 0x34]
008dad94 mov      ecx, esp
008dad96 mov      dword ptr [ebp + 0x10], esp
008dad99 push     eax
008dad9a mov      byte ptr [ebp - 4], 0x99
008dad9e call     0x428211
008dada3 push     esi
008dada4 push     0x161
008dada9 push     ecx
008dadaa lea      eax, [ebp - 0x20]
008dadad mov      ecx, esp
008dadaf mov      dword ptr [ebp + 8], esp
008dadb2 push     eax
008dadb3 call     0x4281dd
008dadb8 mov      byte ptr [ebp - 4], 0x72
008dadbc call     0x98812c
008dadc1 add      esp, 0x18
008dadc4 push     3
008dadc6 push     edi
008dadc7 lea      ecx, [ebp - 0x44]
008dadca call     0x424b91
008dadcf push     ebx
008dadd0 push     ebx
008dadd1 push     ecx
008dadd2 mov      eax, esp
008dadd4 mov      dword ptr [ebp + 0xc], esp
008dadd7 push     0x974
008daddc push     eax
008daddd mov      byte ptr [ebp - 4], 0x9a
008dade1 call     0x79e805
008dade6 mov      ecx, eax
008dade8 call     0x406292
008daded lea      eax, [ebp - 0x54]
008dadf0 push     eax
008dadf1 lea      ecx, [ebp - 0x2c]
008dadf4 mov      byte ptr [ebp - 4], 0x9a
008dadf8 call     0x40263b
008dadfd mov      ecx, eax
008dadff call     0x403935
008dae04 mov      ecx, eax
008dae06 mov      byte ptr [ebp - 4], 0x9c
008dae0a call     0x4032b2
008dae0f mov      dword ptr [ebp + 8], eax
008dae12 lea      eax, [ebp + 8]
008dae15 push     eax
008dae16 lea      ecx, [ebp + 0x10]
008dae19 call     0x428dcc
008dae1e mov      eax, dword ptr [eax]
008dae20 lea      ecx, [ebp - 0x44]
008dae23 push     ecx
008dae24 push     eax
008dae25 mov      eax, dword ptr [ebp + 0x18]
008dae28 push     esi
008dae29 add      eax, 0x161
008dae2e push     eax
008dae2f lea      ecx, [ebp - 0x20]
008dae32 mov      byte ptr [ebp - 4], 0x9d
008dae36 call     0x414576
008dae3b mov      ecx, eax
008dae3d call     0x424ba7
008dae42 lea      ecx, [ebp + 0x10]
008dae45 call     0x4156ab
008dae4a lea      eax, [ebp - 0x54]
008dae4d mov      byte ptr [ebp - 4], 0x9a
008dae51 push     eax
008dae52 call     0x40291d
008dae57 cmp      eax, ebx
008dae59 pop      ecx
008dae5a jge      0x8dae62
008dae5c push     eax
008dae5d call     0xa5fde4
008dae62 lea      eax, [ebp - 0x44]
008dae65 push     eax
008dae66 mov      byte ptr [ebp - 4], 0x72
008dae6a call     0x40291d
008dae6f cmp      eax, ebx
008dae71 pop      ecx
008dae72 jge      0x8dae7a
008dae74 push     eax
008dae75 call     0xa5fde4
008dae7a push     0x20
008dae7c pop      eax
008dae7d call     0xa61190
008dae82 mov      eax, esp
008dae84 push     0xa
008dae86 push     eax
008dae87 push     dword ptr [ebp + 0x14]
008dae8a call     0xa61ac0
008dae8f add      esp, 0xc
008dae92 push     -1
008dae94 push     eax
008dae95 lea      ecx, [ebp - 0x34]
008dae98 call     0x414617
008dae9d mov      eax, dword ptr [ebp - 0x34]
008daea0 cmp      eax, ebx
008daea2 je       0x8daea9
008daea4 mov      eax, dword ptr [eax - 4]
008daea7 jmp      0x8daeab
008daea9 xor      eax, eax
008daeab lea      eax, [eax + eax*2]
008daeae shl      eax, 1
008daeb0 push     1
008daeb2 push     ecx
008daeb3 mov      dword ptr [ebp + 8], eax
008daeb6 lea      eax, [ebp - 0x2c]
008daeb9 mov      ecx, esp
008daebb mov      dword ptr [ebp + 0x10], esp
008daebe push     eax
008daebf call     0x4281c2
008daec4 push     ecx
008daec5 lea      eax, [ebp - 0x34]
008daec8 mov      ecx, esp
008daeca mov      dword ptr [ebp + 0xc], esp
008daecd push     eax
008daece mov      byte ptr [ebp - 4], 0x9e
008daed2 call     0x428211
008daed7 push     esi
008daed8 mov      esi, dword ptr [ebp + 0x18]
008daedb lea      eax, [esi + 0x169]
008daee1 push     eax
008daee2 push     ecx
008daee3 lea      eax, [ebp - 0x20]
008daee6 mov      ecx, esp
008daee8 mov      dword ptr [ebp + 0x10], esp
008daeeb push     eax
008daeec call     0x4281dd
008daef1 mov      byte ptr [ebp - 4], 0x72
008daef5 call     0x98812c
008daefa add      esp, 0x18
008daefd push     3
008daeff push     edi
008daf00 lea      ecx, [ebp - 0x54]
008daf03 call     0x424b91
008daf08 push     ebx
008daf09 push     ebx
008daf0a push     ecx
008daf0b mov      eax, esp
008daf0d mov      dword ptr [ebp + 0xc], esp
008daf10 push     0x972
008daf15 push     eax
008daf16 mov      byte ptr [ebp - 4], 0x9f
008daf1a call     0x79e805
008daf1f mov      ecx, eax
008daf21 call     0x406292
008daf26 lea      eax, [ebp - 0xb0]
008daf2c push     eax
008daf2d lea      ecx, [ebp - 0x2c]
008daf30 mov      byte ptr [ebp - 4], 0x9f
008daf34 call     0x40263b
008daf39 mov      ecx, eax
008daf3b call     0x403935
008daf40 mov      ecx, eax
008daf42 mov      byte ptr [ebp - 4], 0xa1
008daf46 call     0x4032b2
008daf4b mov      dword ptr [ebp + 0x10], eax
008daf4e lea      eax, [ebp + 0x10]
008daf51 push     eax
008daf52 lea      ecx, [ebp + 0x18]
008daf55 call     0x428dcc
008daf5a mov      eax, dword ptr [eax]
008daf5c lea      ecx, [ebp - 0x54]
008daf5f push     ecx
008daf60 push     eax
008daf61 mov      eax, dword ptr [ebp + 8]
008daf64 push     0x224
008daf69 lea      eax, [eax + esi + 0x16a]
008daf70 push     eax
008daf71 lea      ecx, [ebp - 0x20]
008daf74 mov      byte ptr [ebp - 4], 0xa2
008daf78 call     0x414576
008daf7d mov      ecx, eax
008daf7f call     0x424ba7
008daf84 lea      ecx, [ebp + 0x18]
008daf87 call     0x4156ab
008daf8c mov      byte ptr [ebp - 4], 0x9f
008daf90 lea      eax, [ebp - 0xb0]
008daf96 push     eax
008daf97 call     0x40291d
008daf9c cmp      eax, ebx
008daf9e pop      ecx
008daf9f jge      0x8dafa7
008dafa1 push     eax
008dafa2 call     0xa5fde4
008dafa7 lea      eax, [ebp - 0x54]
008dafaa push     eax
008dafab mov      byte ptr [ebp - 4], 0x72
008dafaf call     0x40291d
008dafb4 cmp      eax, ebx
008dafb6 pop      ecx
008dafb7 jge      0x8dafbf
008dafb9 push     eax
008dafba call     0xa5fde4
008dafbf lea      ecx, [ebp - 0x34]
008dafc2 mov      byte ptr [ebp - 4], 0x6e
008dafc6 call     0x4062df
008dafcb lea      ecx, [ebp - 0x2c]
008dafce call     0x4027e9
008dafd3 lea      ecx, [ebp - 0xa0]
008dafd9 call     0x4156ab
008dafde lea      ecx, [ebp - 0x20]
008dafe1 call     0x4156ab
008dafe6 mov      ecx, dword ptr [ebp - 0xc]
008dafe9 lea      esp, [ebp - 0xbc]
008dafef pop      edi
008daff0 pop      esi
008daff1 mov      dword ptr fs:[0], ecx
008daff8 pop      ebx
008daff9 leave    
008daffa ret      0x1c
