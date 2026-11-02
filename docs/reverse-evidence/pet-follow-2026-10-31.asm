SHA256 1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2
# function 0x9c4020: 8 instructions, 0x9c4020..0x9c4038
009c4020 push     esi
009c4021 mov      esi, ecx
009c4023 call     0x9b0f71
009c4028 mov      dword ptr [esi], 0xb3e8a8
009c402e mov      dword ptr [esi + 0xc], 0xb3e7f8
009c4035 mov      eax, esi
009c4037 pop      esi
009c4038 ret      
SHA256 1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2
# function 0x9c40c0: 10 instructions, 0x9c40c0..0x9c40e5
009c40c0 push     esi
009c40c1 push     dword ptr [esp + 8]
009c40c5 mov      esi, ecx
009c40c7 call     0x9b1288
009c40cc mov      eax, dword ptr [esp + 0xc]
009c40d0 and      dword ptr [esi + 0x264], 0
009c40d7 and      dword ptr [esi + 0x268], 0
009c40de mov      dword ptr [esi + 0x248], eax
009c40e4 pop      esi
009c40e5 ret      8
SHA256 1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2
# function 0x9c4122: 936 instructions, 0x9c4122..0x9c4cc1
009c4122 mov      eax, 0xae58e8
009c4127 call     0xa60b98
009c412c sub      esp, 0x6c
009c412f mov      eax, dword ptr [0xbebfa0]
009c4134 push     ebx
009c4135 mov      ebx, ecx
009c4137 mov      dword ptr [ebp - 0x24], eax
009c413a mov      eax, dword ptr [ebx + 0x14]
009c413d test     eax, eax
009c413f push     esi
009c4140 push     edi
009c4141 je       0x9c4148
009c4143 add      eax, -4
009c4146 jmp      0x9c414a
009c4148 xor      eax, eax
009c414a mov      dword ptr [ebp - 0x1c], eax
009c414d mov      eax, dword ptr [eax + 0x94]
009c4153 lea      ecx, [ebp - 0x28]
009c4156 push     ecx
009c4157 mov      ecx, eax
009c4159 mov      dword ptr [ebp - 0x14], eax
009c415c call     0x4ad42b
009c4161 mov      esi, dword ptr [eax]
009c4163 lea      eax, [esi - 0xc]
009c4166 neg      esi
009c4168 sbb      esi, esi
009c416a and      esi, eax
009c416c lea      ecx, [ebp - 0x28]
009c416f mov      dword ptr [ebp - 0x10], esi
009c4172 call     0x41569e
009c4177 push     dword ptr [esi + 0x12c]
009c417d lea      edi, [esi + 0x124]
009c4183 push     edi
009c4184 mov      dword ptr [ebp - 0x30], edi
009c4187 call     0x4b1fe1
009c418c test     eax, eax
009c418e pop      ecx
009c418f pop      ecx
009c4190 je       0x9c41ff
009c4192 push     dword ptr [ebx + 0x12c]
009c4198 lea      eax, [ebx + 0x124]
009c419e push     eax
009c419f call     0x4b1fe1
009c41a4 test     eax, eax
009c41a6 pop      ecx
009c41a7 pop      ecx
009c41a8 je       0x9c41ff
009c41aa lea      ecx, [ebx + 0x68]
009c41ad fldz     
009c41af push     ecx
009c41b0 push     ecx
009c41b1 fstp     qword ptr [esp]
009c41b4 call     0x5393b6
009c41b9 lea      ecx, [ebx + 0x50]
009c41bc fldz     
009c41be push     ecx
009c41bf push     ecx
009c41c0 fstp     qword ptr [esp]
009c41c3 mov      dword ptr [ebx + 0x78], eax
009c41c6 call     0x5393b6
009c41cb mov      dword ptr [ebx + 0x60], eax
009c41ce lea      eax, [ebp - 0x28]
009c41d1 push     eax
009c41d2 lea      eax, [ebp + 8]
009c41d5 push     eax
009c41d6 mov      ecx, esi
009c41d8 call     0x9c4cc4
009c41dd mov      ecx, dword ptr [ebp + 8]
009c41e0 lea      edx, [ebx + 0x150]
009c41e6 call     0x4165b1
009c41eb mov      ecx, dword ptr [ebp - 0x28]
009c41ee mov      dword ptr [ebx + 0x158], eax
009c41f4 lea      edx, [ebx + 0x15c]
009c41fa jmp      0x9c43d2
009c41ff push     dword ptr [esi + 0x12c]
009c4205 push     edi
009c4206 call     0x4b1fe1
009c420b test     eax, eax
009c420d pop      ecx
009c420e pop      ecx
009c420f je       0x9c4375
009c4215 push     dword ptr [ebx + 0x30]
009c4218 lea      edi, [ebx + 0x20]
009c421b push     edi
009c421c call     0x539338
009c4221 call     0xa62018
009c4226 push     dword ptr [edi + 0x28]
009c4229 mov      dword ptr [ebp - 0x18], eax
009c422c lea      eax, [edi + 0x18]
009c422f push     eax
009c4230 call     0x539338
009c4235 call     0xa62018
009c423a mov      dword ptr [ebp - 0x14], eax
009c423d mov      eax, dword ptr [ebp - 0x10]
009c4240 push     0x18
009c4242 pop      ecx
009c4243 lea      esi, [eax + 0x20]
009c4246 rep movsd dword ptr es:[edi], dword ptr [esi]
009c4248 push     dword ptr [ebx + 0x30]
009c424b lea      esi, [ebx + 0x20]
009c424e push     esi
009c424f call     0x539338
009c4254 call     0xa62018
009c4259 push     dword ptr [esi + 0x28]
009c425c mov      dword ptr [ebp - 0x20], eax
009c425f lea      eax, [esi + 0x18]
009c4262 push     eax
009c4263 call     0x539338
009c4268 call     0xa62018
009c426d push     dword ptr [esi + 0x58]
009c4270 mov      edi, dword ptr [ebx + 0x148]
009c4276 mov      dword ptr [ebp - 0x1c], eax
009c4279 lea      eax, [esi + 0x48]
009c427c push     eax
009c427d call     0x539338
009c4282 fstp     qword ptr [ebp - 0x48]
009c4285 push     dword ptr [esi + 0x40]
009c4288 lea      eax, [esi + 0x30]
009c428b push     eax
009c428c call     0x539338
009c4291 fstp     qword ptr [ebp - 0x40]
009c4294 push     dword ptr [esi + 0x28]
009c4297 lea      eax, [esi + 0x18]
009c429a push     eax
009c429b call     0x539338
009c42a0 fstp     qword ptr [ebp - 0x50]
009c42a3 push     dword ptr [esi + 0x10]
009c42a6 push     esi
009c42a7 call     0x539338
009c42ac fstp     qword ptr [ebp - 0x58]
009c42af add      esp, 0x40
009c42b2 push     dword ptr [ebx + 0x12c]
009c42b8 lea      eax, [ebx + 0x124]
009c42be push     eax
009c42bf call     0x4b1fe1
009c42c4 fld      qword ptr [ebp - 0x48]
009c42c7 pop      ecx
009c42c8 pop      ecx
009c42c9 push     0
009c42cb mov      esi, eax
009c42cd push     edi
009c42ce call     0xa62018
009c42d3 fld      qword ptr [ebp - 0x40]
009c42d6 push     eax
009c42d7 call     0xa62018
009c42dc fld      qword ptr [ebp - 0x50]
009c42df push     eax
009c42e0 call     0xa62018
009c42e5 fld      qword ptr [ebp - 0x58]
009c42e8 push     eax
009c42e9 call     0xa62018
009c42ee push     eax
009c42ef push     esi
009c42f0 push     0
009c42f2 push     dword ptr [ebx + 0x110]
009c42f8 lea      ecx, [ebx + 0x1ac]
009c42fe push     5
009c4300 call     0x68ab85
009c4305 mov      eax, dword ptr [ebx + 0x14]
009c4308 test     eax, eax
009c430a je       0x9c4311
009c430c lea      ecx, [eax - 4]
009c430f jmp      0x9c4313
009c4311 xor      ecx, ecx
009c4313 lea      eax, [ebp - 0x20]
009c4316 push     eax
009c4317 lea      eax, [ebp - 0x18]
009c431a push     eax
009c431b push     2
009c431d call     0x70537d
009c4322 mov      eax, dword ptr [ebp - 0x10]
009c4325 push     dword ptr [eax + 0x12c]
009c432b push     dword ptr [ebp - 0x30]
009c432e call     0x4b1fe1
009c4333 mov      eax, dword ptr [eax]
009c4335 cmp      eax, 1
009c4338 pop      ecx
009c4339 pop      ecx
009c433a jb       0x9c435b
009c433c mov      ecx, dword ptr [ebp - 0x24]
009c433f mov      edx, dword ptr [ecx + 0xa8]
009c4345 add      ecx, 0xa8
009c434b test     edx, edx
009c434d je       0x9c435b
009c434f cmp      eax, dword ptr [edx - 4]
009c4352 jae      0x9c435b
009c4354 imul     eax, eax, 0x1c
009c4357 add      eax, edx
009c4359 jmp      0x9c435d
009c435b xor      eax, eax
009c435d mov      edx, dword ptr [ebx]
009c435f push     0
009c4361 push     eax
009c4362 push     0
009c4364 mov      ecx, ebx
009c4366 call     dword ptr [edx + 0x14]
009c4369 and      dword ptr [ebx + 0x24c], 0
009c4370 jmp      0x9c4ca6
009c4375 push     dword ptr [ebx + 0x12c]
009c437b lea      eax, [ebx + 0x124]
009c4381 push     eax
009c4382 call     0x4b1fe1
009c4387 test     eax, eax
009c4389 pop      ecx
009c438a pop      ecx
009c438b je       0x9c440a
009c438d add      dword ptr [ebx + 0x24c], 0x1e
009c4394 cmp      dword ptr [ebx + 0x24c], 0xc8
009c439e lea      eax, [ebx + 0x24c]
009c43a4 jge      0x9c43e9
009c43a6 lea      eax, [ebp - 0x30]
009c43a9 push     eax
009c43aa lea      eax, [ebp + 8]
009c43ad push     eax
009c43ae mov      ecx, esi
009c43b0 call     0x9c4cc4
009c43b5 mov      ecx, dword ptr [ebp + 8]
009c43b8 lea      edx, [ebx + 0x150]
009c43be call     0x4165b1
009c43c3 mov      ecx, dword ptr [ebp - 0x30]
009c43c6 mov      dword ptr [ebx + 0x158], eax
009c43cc lea      edx, [ebx + 0x15c]
009c43d2 call     0x4165b1
009c43d7 mov      ecx, ebx
009c43d9 mov      dword ptr [ebx + 0x164], eax
009c43df call     0x9c4cfe
009c43e4 jmp      0x9c4cb0
009c43e9 mov      eax, dword ptr [ebp - 0x14]
009c43ec push     0
009c43ee push     dword ptr [eax + 0x2a84]
009c43f4 mov      ecx, ebx
009c43f6 call     0x9b7b4a
009c43fb mov      dword ptr [ebx + 0x168], 1
009c4405 jmp      0x9c4ca6
009c440a cmp      dword ptr [ebx + 0x110], 0
009c4411 jne      0x9c4422
009c4413 mov      ecx, ebx
009c4415 call     0x7046ac
009c441a test     eax, eax
009c441c je       0x9c4ca6
009c4422 push     -0x32
009c4424 pop      eax
009c4425 push     0x32
009c4427 mov      dword ptr [ebp - 0x78], eax
009c442a mov      dword ptr [ebp - 0x74], eax
009c442d pop      eax
009c442e push     dword ptr [ebx + 0x48]
009c4431 lea      edi, [ebx + 0x20]
009c4434 mov      dword ptr [ebp - 0x70], eax
009c4437 mov      dword ptr [ebp - 0x6c], eax
009c443a lea      eax, [edi + 0x18]
009c443d push     eax
009c443e mov      dword ptr [ebp - 0x68], 0xfffffed4
009c4445 mov      dword ptr [ebp - 0x64], 0xffffff38
009c444c mov      dword ptr [ebp - 0x60], 0x12c
009c4453 mov      dword ptr [ebp - 0x5c], 0xc8
009c445a call     0x539338
009c445f fstp     qword ptr [ebp - 0x58]
009c4462 push     dword ptr [edi + 0x10]
009c4465 push     edi
009c4466 call     0x539338
009c446b fld      qword ptr [ebp - 0x58]
009c446e add      esp, 0x10
009c4471 call     0xa62018
009c4476 push     eax
009c4477 call     0xa62018
009c447c push     eax
009c447d lea      eax, [ebp - 0x68]
009c4480 push     eax
009c4481 call     dword ptr [0xbf0454]
009c4487 mov      eax, dword ptr [ebp - 0x14]
009c448a lea      esi, [eax + 4]
009c448d mov      eax, dword ptr [esi]
009c448f mov      ecx, esi
009c4491 call     dword ptr [eax + 0x10]
009c4494 push     dword ptr [eax + 4]
009c4497 push     dword ptr [eax]
009c4499 lea      eax, [ebp - 0x68]
009c449c push     eax
009c449d call     dword ptr [0xbf0484]
009c44a3 test     eax, eax
009c44a5 jne      0x9c455f
009c44ab mov      eax, dword ptr [esi]
009c44ad mov      ecx, esi
009c44af call     dword ptr [eax + 0x10]
009c44b2 mov      eax, dword ptr [eax]
009c44b4 mov      dword ptr [ebp - 0x1c], eax
009c44b7 mov      eax, dword ptr [esi]
009c44b9 mov      ecx, esi
009c44bb call     dword ptr [eax + 0x10]
009c44be mov      edi, dword ptr [eax + 4]
009c44c1 sub      edi, 0xa
009c44c4 mov      ecx, ebx
009c44c6 mov      dword ptr [ebp - 0x24], edi
009c44c9 call     0x7046ac
009c44ce test     eax, eax
009c44d0 jne      0x9c44ec
009c44d2 mov      ecx, dword ptr [0xbebfa0]
009c44d8 push     1
009c44da push     0x7fffffff
009c44df lea      eax, [ebp - 0x24]
009c44e2 push     eax
009c44e3 push     edi
009c44e4 push     dword ptr [ebp - 0x1c]
009c44e7 call     0xa45585
009c44ec push     dword ptr [ebp - 0x24]
009c44ef lea      eax, [ebp - 0x78]
009c44f2 push     dword ptr [ebp - 0x1c]
009c44f5 push     eax
009c44f6 call     dword ptr [0xbf0454]
009c44fc mov      eax, dword ptr [esi]
009c44fe mov      ecx, esi
009c4500 call     dword ptr [eax + 0x10]
009c4503 push     dword ptr [eax + 4]
009c4506 push     dword ptr [eax]
009c4508 lea      eax, [ebp - 0x78]
009c450b push     eax
009c450c call     dword ptr [0xbf0484]
009c4512 test     eax, eax
009c4514 je       0x9c4ca6
009c451a mov      eax, dword ptr [ebx + 0xc]
009c451d push     edi
009c451e push     dword ptr [ebp - 0x1c]
009c4521 lea      esi, [ebx + 0xc]
009c4524 push     esi
009c4525 call     dword ptr [eax + 0x40]
009c4528 test     eax, eax
009c452a jge      0x9c4538
009c452c push     0xbd8328
009c4531 push     esi
009c4532 push     eax
009c4533 call     0xa5fdf2
009c4538 push     4
009c453a mov      ecx, ebx
009c453c call     0x52ef17
009c4541 mov      eax, dword ptr [ebx + 0x14]
009c4544 test     eax, eax
009c4546 je       0x9c454d
009c4548 lea      ecx, [eax - 4]
009c454b jmp      0x9c454f
009c454d xor      ecx, ecx
009c454f push     0
009c4551 push     0
009c4553 push     1
009c4555 call     0x70537d
009c455a jmp      0x9c4ca6
009c455f mov      ecx, ebx
009c4561 call     0x7046ac
009c4566 test     eax, eax
009c4568 jne      0x9c4751
009c456e push     dword ptr [ebx + 0xf0]
009c4574 lea      eax, [ebx + 0xe0]
009c457a push     eax
009c457b call     0x539338
009c4580 fstp     qword ptr [ebp - 0x58]
009c4583 push     dword ptr [ebx + 0x158]
009c4589 lea      eax, [ebx + 0x150]
009c458f push     eax
009c4590 call     0x416563
009c4595 fld      qword ptr [0xafe9a0]
009c459b fstp     qword ptr [esp + 8]
009c459f add      esp, 8
009c45a2 fld      qword ptr [ebp - 0x58]
009c45a5 push     ecx
009c45a6 push     ecx
009c45a7 fstp     qword ptr [esp]
009c45aa mov      dword ptr [ebp - 0x30], eax
009c45ad fild     dword ptr [ebp - 0x30]
009c45b0 push     ecx
009c45b1 push     ecx
009c45b2 mov      ecx, dword ptr [ebx + 0x110]
009c45b8 fstp     qword ptr [esp]
009c45bb call     0xa433a3
009c45c0 test     eax, eax
009c45c2 mov      dword ptr [ebp - 0x10], eax
009c45c5 je       0x9c4751
009c45cb push     dword ptr [ebx + 0x158]
009c45d1 lea      eax, [ebx + 0x150]
009c45d7 push     eax
009c45d8 call     0x416563
009c45dd test     eax, eax
009c45df pop      ecx
009c45e0 pop      ecx
009c45e1 je       0x9c4751
009c45e7 mov      eax, dword ptr [ebp - 0x10]
009c45ea fld      qword ptr [eax + 0x30]
009c45ed fcomp    qword ptr [0xaf0de8]
009c45f3 fnstsw   ax
009c45f5 sahf     
009c45f6 ja       0x9c4751
009c45fc push     dword ptr [ebx + 0x158]
009c4602 lea      eax, [ebx + 0x150]
009c4608 push     eax
009c4609 call     0x416563
009c460e mov      dword ptr [ebp - 0x30], eax
009c4611 mov      eax, dword ptr [ebp - 0x10]
009c4614 fld      qword ptr [eax + 0x38]
009c4617 pop      ecx
009c4618 fild     dword ptr [ebp - 0x30]
009c461b pop      ecx
009c461c fmul     st(1)
009c461e fcomp    qword ptr [0xaf0de8]
009c4624 fnstsw   ax
009c4626 fstp     st(0)
009c4628 sahf     
009c4629 jae      0x9c4751
009c462f push     dword ptr [ebx + 0x158]
009c4635 lea      eax, [ebx + 0x150]
009c463b push     eax
009c463c call     0x416563
009c4641 pop      ecx
009c4642 test     eax, eax
009c4644 mov      eax, dword ptr [ebx + 0x110]
009c464a pop      ecx
009c464b jge      0x9c4655
009c464d mov      eax, dword ptr [eax + 0x4c]
009c4650 mov      dword ptr [ebp - 0x10], eax
009c4653 jmp      0x9c465b
009c4655 mov      eax, dword ptr [eax + 0x50]
009c4658 mov      dword ptr [ebp - 0x10], eax
009c465b test     eax, eax
009c465d je       0x9c43fb
009c4663 fld      qword ptr [eax + 0x30]
009c4666 fcomp    qword ptr [0xaf0de8]
009c466c fnstsw   ax
009c466e sahf     
009c466f ja       0x9c43fb
009c4675 push     dword ptr [ebx + 0x158]
009c467b lea      eax, [ebx + 0x150]
009c4681 push     eax
009c4682 call     0x416563
009c4687 mov      dword ptr [ebp - 0x30], eax
009c468a mov      eax, dword ptr [ebp - 0x10]
009c468d fld      qword ptr [eax + 0x38]
009c4690 pop      ecx
009c4691 fild     dword ptr [ebp - 0x30]
009c4694 pop      ecx
009c4695 fmul     st(1)
009c4697 fcomp    qword ptr [0xaf0de8]
009c469d fnstsw   ax
009c469f fstp     st(0)
009c46a1 sahf     
009c46a2 jae      0x9c43fb
009c46a8 push     dword ptr [ebx + 0x158]
009c46ae lea      eax, [ebx + 0x150]
009c46b4 push     eax
009c46b5 call     0x416563
009c46ba test     eax, eax
009c46bc pop      ecx
009c46bd pop      ecx
009c46be jge      0x9c46df
009c46c0 push     dword ptr [ebx + 0xf0]
009c46c6 lea      eax, [ebx + 0xe0]
009c46cc push     eax
009c46cd call     0x539338
009c46d2 fcomp    qword ptr [0xaf0de0]
009c46d8 pop      ecx
009c46d9 pop      ecx
009c46da fnstsw   ax
009c46dc sahf     
009c46dd jb       0x9c4731
009c46df push     dword ptr [ebx + 0x158]
009c46e5 lea      eax, [ebx + 0x150]
009c46eb push     eax
009c46ec call     0x416563
009c46f1 test     eax, eax
009c46f3 pop      ecx
009c46f4 pop      ecx
009c46f5 jle      0x9c43fb
009c46fb push     dword ptr [ebx + 0xf0]
009c4701 lea      eax, [ebx + 0xe0]
009c4707 push     eax
009c4708 call     0x539338
009c470d mov      eax, dword ptr [ebx + 0x110]
009c4713 fld      qword ptr [eax + 0x40]
009c4716 pop      ecx
009c4717 fld      st(0)
009c4719 pop      ecx
009c471a fsub     qword ptr [0xaf0de0]
009c4720 fld      st(2)
009c4722 fcompp   
009c4724 fnstsw   ax
009c4726 fstp     st(0)
009c4728 sahf     
009c4729 fstp     st(0)
009c472b jbe      0x9c43fb
009c4731 push     dword ptr [ebx + 0x158]
009c4737 lea      eax, [ebx + 0x150]
009c473d push     eax
009c473e call     0x416563
009c4743 pop      ecx
009c4744 pop      ecx
009c4745 push     0
009c4747 neg      eax
009c4749 push     eax
009c474a mov      ecx, ebx
009c474c call     0x9b7b4a
009c4751 mov      ecx, ebx
009c4753 call     0x7046ac
009c4758 test     eax, eax
009c475a je       0x9c487c
009c4760 push     dword ptr [edi + 0x28]
009c4763 lea      eax, [edi + 0x18]
009c4766 push     eax
009c4767 call     0x539338
009c476c fstp     qword ptr [ebp - 0x58]
009c476f mov      eax, dword ptr [esi]
009c4771 pop      ecx
009c4772 pop      ecx
009c4773 mov      ecx, esi
009c4775 call     dword ptr [eax + 0x10]
009c4778 fild     dword ptr [eax + 4]
009c477b fsubr    qword ptr [ebp - 0x58]
009c477e fcomp    qword ptr [0xb3e6e8]
009c4784 fnstsw   ax
009c4786 sahf     
009c4787 jbe      0x9c487c
009c478d mov      dword ptr [ebx + 0x168], 1
009c4797 mov      eax, dword ptr [esi]
009c4799 mov      ecx, esi
009c479b mov      dword ptr [ebp - 0x10], 0x50
009c47a2 call     dword ptr [eax + 0x10]
009c47a5 mov      ecx, dword ptr [eax]
009c47a7 mov      eax, dword ptr [eax + 4]
009c47aa mov      dword ptr [ebp - 0x28], eax
009c47ad mov      eax, dword ptr [ebp - 0x14]
009c47b0 mov      eax, dword ptr [eax + 0x570]
009c47b6 test     al, 1
009c47b8 mov      dword ptr [ebp - 0x2c], ecx
009c47bb mov      edx, 0xa0
009c47c0 je       0x9c47d9
009c47c2 mov      ecx, dword ptr [ebx + 0x260]
009c47c8 cmp      ecx, 1
009c47cb je       0x9c47d2
009c47cd cmp      ecx, 3
009c47d0 jne      0x9c47d5
009c47d2 sub      dword ptr [ebp - 0x2c], edx
009c47d5 test     al, 1
009c47d7 jne      0x9c47ec
009c47d9 mov      ecx, dword ptr [ebx + 0x260]
009c47df cmp      ecx, 2
009c47e2 je       0x9c47e9
009c47e4 cmp      ecx, 4
009c47e7 jne      0x9c47ec
009c47e9 add      dword ptr [ebp - 0x2c], edx
009c47ec test     al, 1
009c47ee push     0x46
009c47f0 pop      ecx
009c47f1 jne      0x9c4807
009c47f3 cmp      dword ptr [ebx + 0x260], 3
009c47fa jne      0x9c4803
009c47fc sub      dword ptr [ebp - 0x2c], 0x28
009c4800 mov      dword ptr [ebp - 0x10], ecx
009c4803 test     al, 1
009c4805 je       0x9c4817
009c4807 cmp      dword ptr [ebx + 0x260], 4
009c480e jne      0x9c4817
009c4810 add      dword ptr [ebp - 0x2c], 0x28
009c4814 mov      dword ptr [ebp - 0x10], ecx
009c4817 cmp      dword ptr [ebx + 0x260], 5
009c481e jne      0x9c4827
009c4820 mov      dword ptr [ebp - 0x10], 0x3c
009c4827 push     dword ptr [ebx + 0x30]
009c482a lea      eax, [ebx + 0x20]
009c482d push     eax
009c482e call     0x539338
009c4833 fild     dword ptr [ebp - 0x2c]
009c4836 fst      qword ptr [ebp - 0x48]
009c4839 fsub     st(1)
009c483b call     0xa62018
009c4840 fstp     st(0)
009c4842 push     eax
009c4843 call     0xa61a33
009c4848 mov      edi, eax
009c484a mov      eax, dword ptr [ebp - 0x1c]
009c484d lea      esi, [eax + 4]
009c4850 mov      eax, dword ptr [esi]
009c4852 add      esp, 0xc
009c4855 mov      ecx, esi
009c4857 mov      dword ptr [ebp - 0x24], edi
009c485a call     dword ptr [eax + 0x10]
009c485d mov      eax, dword ptr [eax]
009c485f mov      dword ptr [ebp - 0x14], eax
009c4862 mov      eax, dword ptr [ebp - 0x2c]
009c4865 cmp      eax, dword ptr [ebx + 0x250]
009c486b jne      0x9c49ab
009c4871 mov      eax, dword ptr [ebx + 0x254]
009c4877 jmp      0x9c49b1
009c487c mov      eax, dword ptr [ebx + 0x110]
009c4882 test     eax, eax
009c4884 je       0x9c4797
009c488a mov      eax, dword ptr [eax + 0x20]
009c488d mov      dword ptr [ebp - 0x30], eax
009c4890 mov      eax, dword ptr [esi]
009c4892 mov      ecx, esi
009c4894 call     dword ptr [eax + 0x18]
009c4897 cmp      dword ptr [ebp - 0x30], eax
009c489a je       0x9c4797
009c48a0 push     dword ptr [edi + 0x28]
009c48a3 add      edi, 0x18
009c48a6 push     edi
009c48a7 call     0x539338
009c48ac fstp     qword ptr [ebp - 0x58]
009c48af mov      eax, dword ptr [esi]
009c48b1 pop      ecx
009c48b2 pop      ecx
009c48b3 mov      ecx, esi
009c48b5 call     dword ptr [eax + 0x10]
009c48b8 fild     dword ptr [eax + 4]
009c48bb fcomp    qword ptr [ebp - 0x58]
009c48be fnstsw   ax
009c48c0 sahf     
009c48c1 jae      0x9c4797
009c48c7 mov      eax, dword ptr [esi]
009c48c9 mov      ecx, esi
009c48cb call     dword ptr [eax + 0x18]
009c48ce push     dword ptr [ebx + 0x30]
009c48d1 mov      edi, eax
009c48d3 lea      eax, [ebx + 0x20]
009c48d6 push     eax
009c48d7 call     0x539338
009c48dc mov      eax, dword ptr [ebp - 0x24]
009c48df mov      eax, dword ptr [eax + 0x34]
009c48e2 shl      edi, 3
009c48e5 fild     dword ptr [eax + edi]
009c48e8 pop      ecx
009c48e9 fld      st(1)
009c48eb pop      ecx
009c48ec fcompp   
009c48ee fnstsw   ax
009c48f0 fstp     st(0)
009c48f2 sahf     
009c48f3 jbe      0x9c4797
009c48f9 push     dword ptr [ebx + 0x30]
009c48fc lea      eax, [ebx + 0x20]
009c48ff push     eax
009c4900 call     0x539338
009c4905 mov      eax, dword ptr [ebp - 0x24]
009c4908 mov      eax, dword ptr [eax + 0x34]
009c490b fild     dword ptr [eax + edi + 4]
009c490f pop      ecx
009c4910 fld      st(1)
009c4912 pop      ecx
009c4913 fcompp   
009c4915 fnstsw   ax
009c4917 fstp     st(0)
009c4919 sahf     
009c491a jae      0x9c4797
009c4920 cmp      dword ptr [ebx + 0x26c], 0
009c4927 jne      0x9c4993
009c4929 push     dword ptr [ebx + 0x30]
009c492c lea      edi, [ebx + 0x20]
009c492f push     edi
009c4930 call     0x539338
009c4935 fstp     qword ptr [ebp - 0x58]
009c4938 mov      eax, dword ptr [esi]
009c493a pop      ecx
009c493b pop      ecx
009c493c mov      ecx, esi
009c493e call     dword ptr [eax + 0x10]
009c4941 fild     dword ptr [eax]
009c4943 fcomp    qword ptr [ebp - 0x58]
009c4946 fnstsw   ax
009c4948 sahf     
009c4949 jne      0x9c4962
009c494b mov      eax, dword ptr [ebp - 0x14]
009c494e mov      eax, dword ptr [eax + 0x570]
009c4954 and      al, 1
009c4956 neg      al
009c4958 push     0
009c495a sbb      eax, eax
009c495c and      eax, 0xfffffffe
009c495f inc      eax
009c4960 jmp      0x9c498b
009c4962 push     dword ptr [edi + 0x10]
009c4965 push     edi
009c4966 call     0x539338
009c496b fstp     qword ptr [ebp - 0x58]
009c496e mov      eax, dword ptr [esi]
009c4970 pop      ecx
009c4971 pop      ecx
009c4972 mov      ecx, esi
009c4974 call     dword ptr [eax + 0x10]
009c4977 fild     dword ptr [eax]
009c4979 fcomp    qword ptr [ebp - 0x58]
009c497c fnstsw   ax
009c497e sahf     
009c497f jae      0x9c4986
009c4981 or       eax, 0xffffffff
009c4984 jmp      0x9c4989
009c4986 push     1
009c4988 pop      eax
009c4989 push     0
009c498b push     eax
009c498c mov      ecx, ebx
009c498e call     0x9b7b4a
009c4993 push     8
009c4995 lea      eax, [ebx + 0x26c]
009c499b push     0
009c499d push     eax
009c499e call     0xa61040
009c49a3 add      esp, 0xc
009c49a6 jmp      0x9c43fb
009c49ab call     dword ptr [0xaf0344]
009c49b1 mov      dword ptr [ebx + 0x254], eax
009c49b7 mov      eax, dword ptr [ebp - 0x1c]
009c49ba push     dword ptr [eax + 0x154]
009c49c0 add      eax, 0x14c
009c49c5 push     eax
009c49c6 call     0x508a95
009c49cb neg      eax
009c49cd sbb      eax, eax
009c49cf and      eax, 0x5a
009c49d2 add      eax, 0x3c
009c49d5 cmp      dword ptr [ebx + 0x264], 0
009c49dc pop      ecx
009c49dd pop      ecx
009c49de mov      dword ptr [ebp - 0x30], eax
009c49e1 jne      0x9c4a6c
009c49e7 cmp      dword ptr [ebx + 0x260], 0
009c49ee je       0x9c4a6c
009c49f0 cmp      dword ptr [ebx + 0x25c], 0
009c49f7 jne      0x9c4a6c
009c49f9 mov      eax, dword ptr [ebp - 0x14]
009c49fc cmp      eax, dword ptr [ebx + 0x258]
009c4a02 je       0x9c4a6c
009c4a04 call     0xa61c6d
009c4a09 cdq      
009c4a0a mov      ecx, 0x3e8
009c4a0f idiv     ecx
009c4a11 test     edx, edx
009c4a13 jne      0x9c4a6c
009c4a15 mov      eax, dword ptr [esi]
009c4a17 lea      ecx, [ebp - 0x40]
009c4a1a push     ecx
009c4a1b mov      ecx, esi
009c4a1d call     dword ptr [eax + 0xc]
009c4a20 mov      eax, dword ptr [eax + 4]
009c4a23 and      dword ptr [ebp - 4], 0
009c4a27 lea      edi, [eax + 0x24]
009c4a2a mov      ecx, edi
009c4a2c call     0x6724fc
009c4a31 fadd     st(0), st(0)
009c4a33 push     ecx
009c4a34 push     ecx
009c4a35 mov      ecx, edi
009c4a37 fstp     qword ptr [esp]
009c4a3a call     0x500d61
009c4a3f or       dword ptr [ebp - 4], 0xffffffff
009c4a43 cmp      dword ptr [ebp - 0x3c], 0
009c4a47 je       0x9c4a53
009c4a49 push     0
009c4a4b lea      ecx, [ebp - 0x40]
009c4a4e call     0x500e1e
009c4a53 call     dword ptr [0xaf0344]
009c4a59 mov      edi, dword ptr [ebp - 0x24]
009c4a5c mov      dword ptr [ebx + 0x264], eax
009c4a62 mov      dword ptr [ebx + 0x168], 1
009c4a6c xor      eax, eax
009c4a6e cmp      dword ptr [ebx + 0x264], eax
009c4a74 jne      0x9c4b00
009c4a7a cmp      dword ptr [ebx + 0x260], eax
009c4a80 je       0x9c4b00
009c4a82 cmp      dword ptr [ebx + 0x25c], eax
009c4a88 jne      0x9c4b00
009c4a8a mov      eax, dword ptr [ebp - 0x14]
009c4a8d cmp      eax, dword ptr [ebx + 0x258]
009c4a93 je       0x9c4b00
009c4a95 call     0xa61c6d
009c4a9a cdq      
009c4a9b mov      ecx, 0x2bc
009c4aa0 idiv     ecx
009c4aa2 test     edx, edx
009c4aa4 jne      0x9c4b00
009c4aa6 mov      eax, dword ptr [esi]
009c4aa8 lea      ecx, [ebp - 0x40]
009c4aab push     ecx
009c4aac mov      ecx, esi
009c4aae call     dword ptr [eax + 0xc]
009c4ab1 mov      eax, dword ptr [eax + 4]
009c4ab4 lea      edi, [eax + 0x24]
009c4ab7 mov      ecx, edi
009c4ab9 mov      dword ptr [ebp - 4], 1
009c4ac0 call     0x6724fc
009c4ac5 fadd     st(0), st(0)
009c4ac7 push     ecx
009c4ac8 push     ecx
009c4ac9 mov      ecx, edi
009c4acb fstp     qword ptr [esp]
009c4ace call     0x500d61
009c4ad3 or       dword ptr [ebp - 4], 0xffffffff
009c4ad7 cmp      dword ptr [ebp - 0x3c], 0
009c4adb je       0x9c4ae7
009c4add push     0
009c4adf lea      ecx, [ebp - 0x40]
009c4ae2 call     0x500e1e
009c4ae7 call     dword ptr [0xaf0344]
009c4aed mov      edi, dword ptr [ebp - 0x24]
009c4af0 mov      dword ptr [ebx + 0x264], eax
009c4af6 mov      dword ptr [ebx + 0x268], 0x64
009c4b00 cmp      dword ptr [ebx + 0x264], 0
009c4b07 je       0x9c4b76
009c4b09 call     dword ptr [0xaf0344]
009c4b0f mov      ecx, dword ptr [ebx + 0x264]
009c4b15 add      ecx, 0x2bc
009c4b1b cmp      eax, ecx
009c4b1d jbe      0x9c4b76
009c4b1f mov      eax, dword ptr [esi]
009c4b21 lea      ecx, [ebp - 0x38]
009c4b24 push     ecx
009c4b25 mov      ecx, esi
009c4b27 call     dword ptr [eax + 0xc]
009c4b2a mov      eax, dword ptr [eax + 4]
009c4b2d lea      esi, [eax + 0x24]
009c4b30 mov      ecx, esi
009c4b32 mov      dword ptr [ebp - 4], 2
009c4b39 call     0x6724fc
009c4b3e fmul     qword ptr [0xaf0d48]
009c4b44 push     ecx
009c4b45 push     ecx
009c4b46 mov      ecx, esi
009c4b48 fstp     qword ptr [esp]
009c4b4b call     0x500d61
009c4b50 or       dword ptr [ebp - 4], 0xffffffff
009c4b54 cmp      dword ptr [ebp - 0x34], 0
009c4b58 je       0x9c4b68
009c4b5a push     0
009c4b5c lea      ecx, [ebp - 0x38]
009c4b5f call     0x500e1e
009c4b64 and      dword ptr [ebp - 0x34], 0
009c4b68 and      dword ptr [ebx + 0x264], 0
009c4b6f and      dword ptr [ebx + 0x268], 0
009c4b76 add      edi, dword ptr [ebx + 0x268]
009c4b7c cmp      edi, dword ptr [ebp - 0x10]
009c4b7f jge      0x9c4b8a
009c4b81 and      dword ptr [ebx + 0x26c], 0
009c4b88 jmp      0x9c4bd1
009c4b8a cmp      dword ptr [ebx + 0x25c], 0
009c4b91 jne      0x9c4bad
009c4b93 mov      eax, dword ptr [ebp - 0x14]
009c4b96 cmp      eax, dword ptr [ebx + 0x258]
009c4b9c jne      0x9c4ba3
009c4b9e cmp      edi, dword ptr [ebp - 0x10]
009c4ba1 jg       0x9c4bad
009c4ba3 mov      eax, dword ptr [ebp - 0x30]
009c4ba6 add      eax, 0x14
009c4ba9 cmp      edi, eax
009c4bab jle      0x9c4bd1
009c4bad push     dword ptr [ebx + 0x30]
009c4bb0 lea      eax, [ebx + 0x20]
009c4bb3 push     eax
009c4bb4 call     0x539338
009c4bb9 fcomp    qword ptr [ebp - 0x48]
009c4bbc pop      ecx
009c4bbd pop      ecx
009c4bbe fnstsw   ax
009c4bc0 sahf     
009c4bc1 jbe      0x9c4bc8
009c4bc3 or       eax, 0xffffffff
009c4bc6 jmp      0x9c4bcb
009c4bc8 push     1
009c4bca pop      eax
009c4bcb mov      dword ptr [ebx + 0x26c], eax
009c4bd1 mov      eax, dword ptr [0xbebf98]
009c4bd6 test     eax, eax
009c4bd8 je       0x9c4c85
009c4bde mov      ecx, dword ptr [eax + 0x570]
009c4be4 and      ecx, 0xfffffffe
009c4be7 cmp      ecx, 0x12
009c4bea je       0x9c4c85
009c4bf0 push     dword ptr [eax + 0x3100]
009c4bf6 add      eax, 0x30fc
009c4bfb push     eax
009c4bfc call     0x4e81db
009c4c01 cmp      dword ptr [ebx + 0x25c], 0
009c4c08 pop      ecx
009c4c09 pop      ecx
009c4c0a je       0x9c4c85
009c4c0c call     dword ptr [0xaf0344]
009c4c12 mov      ecx, dword ptr [ebx + 0x254]
009c4c18 add      ecx, 0x64
009c4c1b cmp      eax, ecx
009c4c1d jbe      0x9c4c85
009c4c1f mov      esi, dword ptr [ebp - 0x1c]
009c4c22 test     esi, esi
009c4c24 je       0x9c4c85
009c4c26 mov      ecx, esi
009c4c28 call     0x70445e
009c4c2d test     eax, eax
009c4c2f jne      0x9c4c85
009c4c31 push     dword ptr [esi + 0x160]
009c4c37 lea      eax, [esi + 0x158]
009c4c3d push     eax
009c4c3e call     0x508a95
009c4c43 test     eax, eax
009c4c45 pop      ecx
009c4c46 pop      ecx
009c4c47 je       0x9c4c85
009c4c49 push     dword ptr [esi + 0x13c]
009c4c4f lea      eax, [esi + 0x134]
009c4c55 push     eax
009c4c56 call     0x508a95
009c4c5b test     eax, eax
009c4c5d pop      ecx
009c4c5e pop      ecx
009c4c5f jne      0x9c4c79
009c4c61 push     dword ptr [esi + 0x130]
009c4c67 add      esi, 0x128
009c4c6d push     esi
009c4c6e call     0x508a95
009c4c73 test     eax, eax
009c4c75 pop      ecx
009c4c76 pop      ecx
009c4c77 je       0x9c4c85
009c4c79 mov      eax, dword ptr [ebx + 0x25c]
009c4c7f mov      dword ptr [ebx + 0x26c], eax
009c4c85 push     0
009c4c87 push     dword ptr [ebx + 0x26c]
009c4c8d mov      ecx, ebx
009c4c8f call     0x9b7b4a
009c4c94 mov      eax, dword ptr [ebp - 0x2c]
009c4c97 mov      dword ptr [ebx + 0x250], eax
009c4c9d mov      eax, dword ptr [ebp - 0x14]
009c4ca0 mov      dword ptr [ebx + 0x258], eax
009c4ca6 push     dword ptr [ebp + 8]
009c4ca9 mov      ecx, ebx
009c4cab call     0x9b19d0
009c4cb0 mov      ecx, dword ptr [ebp - 0xc]
009c4cb3 push     1
009c4cb5 pop      eax
009c4cb6 pop      edi
009c4cb7 pop      esi
009c4cb8 pop      ebx
009c4cb9 mov      dword ptr fs:[0], ecx
009c4cc0 leave    
009c4cc1 ret      4
SHA256 1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2
# function 0x70458c: 109 instructions, 0x70458c..0x7046a9
0070458c push     ebp
0070458d mov      ebp, esp
0070458f push     ebx
00704590 push     esi
00704591 mov      esi, dword ptr [ebp + 0x14]
00704594 push     edi
00704595 push     dword ptr [esi + 0x12c]
0070459b mov      edi, dword ptr [ebp + 0x10]
0070459e lea      eax, [esi + 0x124]
007045a4 push     eax
007045a5 mov      ebx, ecx
007045a7 and      edi, 1
007045aa call     0x4b1fe1
007045af test     eax, eax
007045b1 pop      ecx
007045b2 pop      ecx
007045b3 je       0x70460e
007045b5 mov      ecx, dword ptr [ebx + 0x90]
007045bb lea      eax, [ebp + 0x10]
007045be push     eax
007045bf call     0x4ad42b
007045c4 mov      esi, dword ptr [eax]
007045c6 lea      eax, [esi - 0xc]
007045c9 neg      esi
007045cb sbb      esi, esi
007045cd and      esi, eax
007045cf mov      eax, dword ptr [ebp + 0x10]
007045d2 test     eax, eax
007045d4 je       0x7045dc
007045d6 mov      ecx, dword ptr [eax]
007045d8 push     eax
007045d9 call     dword ptr [ecx + 8]
007045dc push     dword ptr [esi + 0x12c]
007045e2 add      esi, 0x124
007045e8 push     esi
007045e9 call     0x4b1fe1
007045ee test     eax, eax
007045f0 pop      ecx
007045f1 pop      ecx
007045f2 je       0x70460b
007045f4 mov      eax, dword ptr [ebx + 0x90]
007045fa mov      eax, dword ptr [eax + 0x570]
00704600 and      eax, 1
00704603 or       eax, 0x1e
00704606 jmp      0x7046a5
0070460b mov      esi, dword ptr [ebp + 0x14]
0070460e xor      eax, eax
00704610 cmp      dword ptr [esi + 0x110], eax
00704616 je       0x70467e
00704618 cmp      dword ptr [ebp + 8], eax
0070461b je       0x70462f
0070461d xor      ecx, ecx
0070461f cmp      dword ptr [ebp + 8], eax
00704622 setl     cl
00704625 and      ecx, 1
00704628 or       ecx, 2
0070462b mov      eax, ecx
0070462d jmp      0x7046a5
0070462f cmp      dword ptr [ebx + 0xa8], 0x32
00704636 jg       0x704642
00704638 mov      eax, edi
0070463a and      eax, 1
0070463d or       eax, 0x18
00704640 jmp      0x7046a5
00704642 mov      ebx, dword ptr [ebx + 0xf0]
00704648 dec      ebx
00704649 je       0x704674
0070464b dec      ebx
0070464c je       0x70466a
0070464e sub      ebx, 4
00704651 je       0x704660
00704653 dec      ebx
00704654 mov      eax, edi
00704656 jne      0x704676
00704658 and      eax, 1
0070465b or       eax, 0x1c
0070465e jmp      0x7046a5
00704660 mov      eax, edi
00704662 and      eax, 1
00704665 or       eax, 0x1a
00704668 jmp      0x7046a5
0070466a mov      eax, edi
0070466c and      eax, 1
0070466f or       eax, 0x16
00704672 jmp      0x7046a5
00704674 mov      eax, edi
00704676 and      eax, 1
00704679 or       eax, 4
0070467c jmp      0x7046a5
0070467e cmp      dword ptr [ebp + 8], eax
00704681 je       0x70468d
00704683 xor      ecx, ecx
00704685 cmp      dword ptr [ebp + 8], eax
00704688 setl     cl
0070468b mov      edi, ecx
0070468d mov      ecx, esi
0070468f call     0x7046ac
00704694 neg      eax
00704696 sbb      eax, eax
00704698 and      eax, 3
0070469b add      eax, 3
0070469e shl      eax, 1
007046a0 and      edi, 1
007046a3 or       eax, edi
007046a5 pop      edi
007046a6 pop      esi
007046a7 pop      ebx
007046a8 pop      ebp
007046a9 ret      0x10
SHA256 1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2
# function 0x703c36: 52 instructions, 0x703c36..0x703ca2
00703c36 mov      edx, dword ptr [esp + 4]
00703c3a test     edx, edx
00703c3c mov      eax, dword ptr [ecx + 0xf0]
00703c42 je       0x703c4d
00703c44 push     esi
00703c45 mov      esi, eax
00703c47 and      esi, 1
00703c4a mov      dword ptr [edx], esi
00703c4c pop      esi
00703c4d sar      eax, 1
00703c4f cmp      eax, 0xb
00703c52 jg       0x703c74
00703c54 je       0x703c70
00703c56 dec      eax
00703c57 je       0x703c82
00703c59 dec      eax
00703c5a je       0x703c6c
00703c5c dec      eax
00703c5d je       0x703c68
00703c5f sub      eax, 3
00703c62 jne      0x703c82
00703c64 push     4
00703c66 jmp      0x703c94
00703c68 push     3
00703c6a jmp      0x703c94
00703c6c push     1
00703c6e jmp      0x703c94
00703c70 push     2
00703c72 jmp      0x703c94
00703c74 sub      eax, 0xc
00703c77 je       0x703c92
00703c79 dec      eax
00703c7a je       0x703c8e
00703c7c dec      eax
00703c7d je       0x703c8a
00703c7f dec      eax
00703c80 je       0x703c86
00703c82 xor      eax, eax
00703c84 jmp      0x703c95
00703c86 push     8
00703c88 jmp      0x703c94
00703c8a push     7
00703c8c jmp      0x703c94
00703c8e push     6
00703c90 jmp      0x703c94
00703c92 push     5
00703c94 pop      eax
00703c95 mov      ecx, dword ptr [ecx + 0xf8]
00703c9b cmp      ecx, -1
00703c9e jle      0x703ca2
00703ca0 mov      eax, ecx
00703ca2 ret      4
SHA256 1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2
# function 0x4f0d83: 89 instructions, 0x4f0d83..0x4f0e47
004f0d83 push     ebp
004f0d84 mov      ebp, esp
004f0d86 mov      eax, dword ptr [ebp + 0xc]
004f0d89 mov      ecx, dword ptr [ebp + 8]
004f0d8c add      eax, 0xffe459e0
004f0d91 cmp      eax, 7
004f0d94 ja       0x4f0e3f
004f0d9a jmp      dword ptr [eax*4 + 0x4f0e48]
004f0da1 mov      eax, dword ptr [ebp + 8]
004f0da4 test     eax, eax
004f0da6 jne      0x4f0daf
004f0da8 push     0x16
004f0daa jmp      0x4f0e3c
004f0daf dec      eax
004f0db0 neg      eax
004f0db2 sbb      eax, eax
004f0db4 and      eax, 8
004f0db7 add      eax, 0x21
004f0dba pop      ebp
004f0dbb ret      
004f0dbc mov      eax, dword ptr [ebp + 8]
004f0dbf test     eax, eax
004f0dc1 jne      0x4f0dc7
004f0dc3 push     0x17
004f0dc5 jmp      0x4f0e3c
004f0dc7 dec      eax
004f0dc8 neg      eax
004f0dca sbb      eax, eax
004f0dcc and      eax, 8
004f0dcf add      eax, 0x22
004f0dd2 pop      ebp
004f0dd3 ret      
004f0dd4 push     0x18
004f0dd6 jmp      0x4f0e3c
004f0dd8 push     0x19
004f0dda jmp      0x4f0e3c
004f0ddc mov      eax, dword ptr [ebp + 8]
004f0ddf test     eax, eax
004f0de1 jne      0x4f0de7
004f0de3 push     0x1a
004f0de5 jmp      0x4f0e3c
004f0de7 dec      eax
004f0de8 neg      eax
004f0dea sbb      eax, eax
004f0dec and      eax, 8
004f0def add      eax, 0x23
004f0df2 pop      ebp
004f0df3 ret      
004f0df4 mov      eax, dword ptr [ebp + 8]
004f0df7 test     eax, eax
004f0df9 jne      0x4f0dff
004f0dfb push     0x1b
004f0dfd jmp      0x4f0e3c
004f0dff dec      eax
004f0e00 neg      eax
004f0e02 sbb      eax, eax
004f0e04 and      eax, 8
004f0e07 add      eax, 0x24
004f0e0a pop      ebp
004f0e0b ret      
004f0e0c mov      eax, dword ptr [ebp + 8]
004f0e0f test     eax, eax
004f0e11 jne      0x4f0e17
004f0e13 push     0x1c
004f0e15 jmp      0x4f0e3c
004f0e17 dec      eax
004f0e18 neg      eax
004f0e1a sbb      eax, eax
004f0e1c and      eax, 8
004f0e1f add      eax, 0x25
004f0e22 pop      ebp
004f0e23 ret      
004f0e24 test     ecx, ecx
004f0e26 jne      0x4f0e2c
004f0e28 push     0x2e
004f0e2a jmp      0x4f0e3c
004f0e2c cmp      ecx, 1
004f0e2f jne      0x4f0e35
004f0e31 push     0x2f
004f0e33 jmp      0x4f0e3c
004f0e35 cmp      ecx, 2
004f0e38 jne      0x4f0e3f
004f0e3a push     0x30
004f0e3c pop      eax
004f0e3d pop      ebp
004f0e3e ret      
004f0e3f mov      eax, dword ptr [ecx*4 + 0xbd85a0]
004f0e46 pop      ebp
004f0e47 ret      
SHA256 1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2
# linear sweep: instruction boundaries drift after a switch table; use --fn for large functions
0095acfd mov      eax, dword ptr [esi + 0x1edc]
0095ad03 cmp      dword ptr [eax + 0x14], ebx
0095ad06 je       0x95ad2f
0095ad08 mov      ecx, dword ptr [eax + 4]
0095ad0b push     5
0095ad0d call     0x704bd8
0095ad12 mov      eax, dword ptr [esi + 0x1edc]
0095ad18 mov      ecx, dword ptr [eax + 0xc]
0095ad1b push     3
0095ad1d call     0x704bd8
0095ad22 mov      eax, dword ptr [esi + 0x1edc]
0095ad28 mov      ecx, dword ptr [eax + 0x14]
0095ad2b push     4
0095ad2d jmp      0x95ad55
0095ad2f cmp      dword ptr [eax + 0xc], ebx
0095ad32 je       0x95ad4b
0095ad34 mov      ecx, dword ptr [eax + 4]
0095ad37 push     1
0095ad39 call     0x704bd8
0095ad3e mov      eax, dword ptr [esi + 0x1edc]
0095ad44 mov      ecx, dword ptr [eax + 0xc]
0095ad47 push     2
0095ad49 jmp      0x95ad55
0095ad4b mov      eax, dword ptr [eax + 4]
0095ad4e cmp      eax, ebx
0095ad50 je       0x95ad5a
0095ad52 push     ebx
0095ad53 mov      ecx, eax
0095ad55 call     0x704bd8
0095ad5a mov      ecx, dword ptr [ebp - 0xc]
0095ad5d pop      edi
0095ad5e pop      esi
0095ad5f mov      dword ptr fs:[0], ecx
0095ad66 pop      ebx
0095ad67 leave    
0095ad68 .byte    0xc2

; pet-warp.asm
SHA256 1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2
# function 0x70537d: 200 instructions, 0x70537d..0x7055d5
0070537d mov      eax, 0xaadb7c
00705382 call     0xa60b98
00705387 sub      esp, 0x24
0070538a push     ebx
0070538b push     esi
0070538c mov      esi, ecx
0070538e xor      ebx, ebx
00705390 push     edi
00705391 mov      dword ptr [ebp - 0x1c], esi
00705394 mov      dword ptr [ebp - 0x10], ebx
00705397 mov      dword ptr [ebp - 4], ebx
0070539a mov      dword ptr [ebp - 0x14], ebx
0070539d mov      edi, dword ptr [ebp + 0xc]
007053a0 cmp      edi, ebx
007053a2 mov      byte ptr [ebp - 4], 1
007053a6 jne      0x7053ae
007053a8 lea      edi, [esi + 0x1b8]
007053ae mov      eax, dword ptr [ebp + 8]
007053b1 sub      eax, ebx
007053b3 je       0x7054ed
007053b9 dec      eax
007053ba je       0x7054c2
007053c0 dec      eax
007053c1 je       0x70542f
007053c3 dec      eax
007053c4 jne      0x70555b
007053ca lea      eax, [ebp + 0xc]
007053cd push     0x699
007053d2 push     eax
007053d3 call     0x79e805
007053d8 mov      ecx, eax
007053da call     0x406276
007053df push     eax
007053e0 lea      ecx, [ebp - 0x10]
007053e3 mov      byte ptr [ebp - 4], 2
007053e7 call     0x41cf93
007053ec mov      eax, dword ptr [ebp + 0xc]
007053ef cmp      eax, ebx
007053f1 mov      byte ptr [ebp - 4], 1
007053f5 je       0x705401
007053f7 add      eax, -0xc
007053fa push     eax
007053fb call     0x4033a7
00705400 pop      ecx
00705401 lea      eax, [ebp + 8]
00705404 push     0x15ab
00705409 push     eax
0070540a call     0x79e805
0070540f mov      ecx, eax
00705411 call     0x406276
00705416 push     eax
00705417 lea      ecx, [ebp - 0x14]
0070541a mov      byte ptr [ebp - 4], 3
0070541e call     0x41cf93
00705423 mov      eax, dword ptr [ebp + 8]
00705426 mov      byte ptr [ebp - 4], 1
0070542a jmp      0x70554d
0070542f mov      eax, dword ptr [esi + 0x88]
00705435 mov      esi, dword ptr [eax]
00705437 lea      eax, [ebp - 0x18]
0070543a push     0x69a
0070543f push     eax
00705440 call     0x79e805
00705445 mov      ecx, eax
00705447 call     0x406276
0070544c mov      eax, dword ptr [eax]
0070544e push     esi
0070544f push     eax
00705450 lea      eax, [ebp - 0x10]
00705453 push     eax
00705454 mov      byte ptr [ebp - 4], 7
00705458 call     0x416821
0070545d mov      eax, dword ptr [ebp - 0x18]
00705460 add      esp, 0xc
00705463 cmp      eax, ebx
00705465 mov      byte ptr [ebp - 4], 1
00705469 je       0x705475
0070546b add      eax, -0xc
0070546e push     eax
0070546f call     0x4033a7
00705474 pop      ecx
00705475 push     0xc00614a4
0070547a push     ecx
0070547b mov      ecx, esp
0070547d mov      dword ptr [ebp - 0x2c], esp
00705480 push     ebx
00705481 call     0x432c10
00705486 push     dword ptr [edi + 4]
00705489 mov      byte ptr [ebp - 4], 8
0070548d push     dword ptr [edi]
0070548f push     ecx
00705490 mov      ecx, esp
00705492 mov      dword ptr [ebp - 0x30], esp
00705495 push     ebx
00705496 call     0x432bdd
0070549b mov      ecx, dword ptr [ebp - 0x1c]
0070549e mov      byte ptr [ebp - 4], 1
007054a2 call     0x7055d8
007054a7 push     eax
007054a8 lea      ecx, [ebp - 0x10]
007054ab call     0x41468c
007054b0 push     eax
007054b1 call     0x432cbc
007054b6 mov      ecx, eax
007054b8 call     0x439750
007054bd jmp      0x7055b0
007054c2 lea      eax, [ebp - 0x20]
007054c5 push     0x698
007054ca push     eax
007054cb call     0x79e805
007054d0 mov      ecx, eax
007054d2 call     0x406276
007054d7 push     eax
007054d8 lea      ecx, [ebp - 0x10]
007054db mov      byte ptr [ebp - 4], 6
007054df call     0x41cf93
007054e4 mov      eax, dword ptr [ebp - 0x20]
007054e7 mov      byte ptr [ebp - 4], 1
007054eb jmp      0x70554d
007054ed lea      eax, [ebp - 0x24]
007054f0 push     0x697
007054f5 push     eax
007054f6 call     0x79e805
007054fb mov      ecx, eax
007054fd call     0x406276
00705502 push     eax
00705503 lea      ecx, [ebp - 0x10]
00705506 mov      byte ptr [ebp - 4], 4
0070550a call     0x41cf93
0070550f mov      eax, dword ptr [ebp - 0x24]
00705512 cmp      eax, ebx
00705514 mov      byte ptr [ebp - 4], 1
00705518 je       0x705524
0070551a add      eax, -0xc
0070551d push     eax
0070551e call     0x4033a7
00705523 pop      ecx
00705524 lea      eax, [ebp - 0x28]
00705527 push     0x15ab
0070552c push     eax
0070552d call     0x79e805
00705532 mov      ecx, eax
00705534 call     0x406276
00705539 push     eax
0070553a lea      ecx, [ebp - 0x14]
0070553d mov      byte ptr [ebp - 4], 5
00705541 call     0x41cf93
00705546 mov      eax, dword ptr [ebp - 0x28]
00705549 mov      byte ptr [ebp - 4], 1
0070554d cmp      eax, ebx
0070554f je       0x70555b
00705551 add      eax, -0xc
00705554 push     eax
00705555 call     0x4033a7
0070555a pop      ecx
0070555b push     0xc00614a4
00705560 push     ecx
00705561 mov      ecx, esp
00705563 mov      dword ptr [ebp - 0x30], esp
00705566 push     ebx
00705567 call     0x432c10
0070556c push     ebx
0070556d push     ebx
0070556e push     ecx
0070556f add      esi, 0x90
00705575 mov      ecx, esp
00705577 mov      dword ptr [ebp - 0x2c], esp
0070557a push     esi
0070557b call     0x444efa
00705580 push     ebx
00705581 lea      ecx, [ebp - 0x10]
00705584 mov      byte ptr [ebp - 4], 1
00705588 call     0x41468c
0070558d push     eax
0070558e call     0x432cbc
00705593 mov      ecx, eax
00705595 call     0x439750
0070559a mov      eax, dword ptr [ebp - 0x14]
0070559d cmp      eax, ebx
0070559f je       0x7055b0
007055a1 cmp      word ptr [eax], bx
007055a4 je       0x7055b0
007055a6 push     0x64
007055a8 push     eax
007055a9 call     0x9899b1
007055ae pop      ecx
007055af pop      ecx
007055b0 lea      ecx, [ebp - 0x14]
007055b3 mov      byte ptr [ebp - 4], bl
007055b6 call     0x40265e
007055bb or       dword ptr [ebp - 4], 0xffffffff
007055bf lea      ecx, [ebp - 0x10]
007055c2 call     0x40265e
007055c7 mov      ecx, dword ptr [ebp - 0xc]
007055ca pop      edi
007055cb pop      esi
007055cc mov      dword ptr fs:[0], ecx
007055d3 pop      ebx
007055d4 leave    
007055d5 ret      0xc

; pet-loot.asm
SHA256 1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2
# function 0x705c5d: 14 instructions, 0x705c5d..0x705c7b
00705c5d push     esi
00705c5e mov      esi, ecx
00705c60 mov      eax, dword ptr [esi + 4]
00705c63 lea      ecx, [esi + 4]
00705c66 push     edi
00705c67 mov      edi, dword ptr [0xbed6ac]
00705c6d call     dword ptr [eax + 0x10]
00705c70 push     eax
00705c71 push     esi
00705c72 mov      ecx, edi
00705c74 call     0x50483a
00705c79 pop      edi
00705c7a pop      esi
00705c7b ret      

; pet-loot-pool.asm
SHA256 1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2
# function 0x50483a: 185 instructions, 0x50483a..0x504a60
0050483a mov      eax, 0xa87c30
0050483f call     0xa60b98
00504844 sub      esp, 0x28
00504847 push     ebx
00504848 push     esi
00504849 mov      esi, ecx
0050484b mov      ecx, dword ptr [ebp + 0xc]
0050484e mov      eax, dword ptr [ecx]
00504850 mov      ecx, dword ptr [ecx + 4]
00504853 lea      edx, [eax - 0x19]
00504856 mov      dword ptr [ebp - 0x34], edx
00504859 lea      edx, [ecx - 0x32]
0050485c push     edi
0050485d mov      edi, dword ptr [0xbe7918]
00504863 add      eax, 0x19
00504866 add      ecx, 0xa
00504869 mov      dword ptr [ebp - 0x1c], esi
0050486c mov      dword ptr [ebp - 0x18], edi
0050486f mov      dword ptr [ebp - 0x30], edx
00504872 mov      dword ptr [ebp - 0x2c], eax
00504875 mov      dword ptr [ebp - 0x28], ecx
00504878 call     0x987257
0050487d mov      ecx, dword ptr [esi + 0x2c]
00504880 xor      ebx, ebx
00504882 cmp      ecx, ebx
00504884 mov      dword ptr [ebp - 0x10], eax
00504887 je       0x504a52
0050488d jmp      0x504895
0050488f mov      ecx, dword ptr [ebp - 0x14]
00504892 mov      edi, dword ptr [ebp - 0x18]
00504895 mov      eax, ecx
00504897 neg      eax
00504899 sbb      eax, eax
0050489b lea      edx, [ecx - 0x10]
0050489e and      eax, edx
005048a0 mov      eax, dword ptr [eax + 4]
005048a3 mov      edx, eax
005048a5 add      eax, 0x10
005048a8 neg      edx
005048aa sbb      edx, edx
005048ac and      edx, eax
005048ae mov      eax, dword ptr [ecx + 4]
005048b1 lea      ecx, [ebp - 0x24]
005048b4 mov      dword ptr [ebp - 0x14], edx
005048b7 mov      dword ptr [ebp - 0x20], eax
005048ba call     0x5088fa
005048bf mov      eax, dword ptr [ebp - 0x20]
005048c2 cmp      dword ptr [eax + 0x88], ebx
005048c8 mov      dword ptr [ebp - 4], ebx
005048cb jne      0x5048de
005048cd or       dword ptr [ebp - 4], 0xffffffff
005048d1 lea      ecx, [ebp - 0x24]
005048d4 call     0x508917
005048d9 jmp      0x504a00
005048de mov      esi, dword ptr [ebp + 8]
005048e1 cmp      esi, ebx
005048e3 je       0x5048cd
005048e5 cmp      dword ptr [eax + 0x30], ebx
005048e8 jne      0x50491f
005048ea push     dword ptr [esi + 0x13c]
005048f0 lea      eax, [esi + 0x134]
005048f6 push     eax
005048f7 call     0x508a95
005048fc cmp      eax, ebx
005048fe pop      ecx
005048ff pop      ecx
00504900 je       0x5048cd
00504902 mov      eax, dword ptr [ebp - 0x20]
00504905 push     dword ptr [eax + 0x34]
00504908 mov      ecx, dword ptr [0xbe78d8]
0050490e call     0x5d5224
00504913 test     eax, eax
00504915 jne      0x5048cd
00504917 mov      eax, dword ptr [ebp - 0x20]
0050491a cmp      dword ptr [eax + 0x30], ebx
0050491d je       0x504937
0050491f push     dword ptr [esi + 0x130]
00504925 lea      eax, [esi + 0x128]
0050492b push     eax
0050492c call     0x508a95
00504931 cmp      eax, ebx
00504933 pop      ecx
00504934 pop      ecx
00504935 je       0x5048cd
00504937 push     dword ptr [esi + 0x148]
0050493d add      esi, 0x140
00504943 push     esi
00504944 call     0x508a95
00504949 pop      ecx
0050494a cmp      eax, ebx
0050494c mov      eax, dword ptr [ebp - 0x20]
0050494f pop      ecx
00504950 je       0x504960
00504952 mov      ecx, dword ptr [ebp - 0x10]
00504955 sub      ecx, dword ptr [eax + 0x40]
00504958 cmp      ecx, 0x3a98
0050495e jg       0x504987
00504960 cmp      dword ptr [eax + 0x28], ebx
00504963 je       0x504987
00504965 mov      edx, dword ptr [eax + 0x2c]
00504968 cmp      edx, ebx
0050496a jne      0x504977
0050496c mov      ecx, dword ptr [edi + 0x20a0]
00504972 cmp      dword ptr [eax + 0x24], ecx
00504975 jne      0x5049ec
00504977 cmp      edx, 1
0050497a jne      0x504987
0050497c mov      ecx, dword ptr [edi + 0x2ed4]
00504982 cmp      dword ptr [eax + 0x24], ecx
00504985 jne      0x5049ec
00504987 cmp      byte ptr [eax + 0x1d], bl
0050498a je       0x5049ec
0050498c cmp      dword ptr [eax + 0x48], 3
00504990 jne      0x5049ec
00504992 mov      ecx, dword ptr [ebp - 0x10]
00504995 sub      ecx, dword ptr [eax + 0x54]
00504998 cmp      ecx, 0xbb8
0050499e jl       0x5049ec
005049a0 lea      esi, [eax + 0x68]
005049a3 lea      ecx, [esi + 0xc]
005049a6 call     0x42873d
005049ab mov      ecx, esi
005049ad mov      edi, eax
005049af call     0x42873d
005049b4 push     eax
005049b5 push     edi
005049b6 lea      eax, [ebp - 0x34]
005049b9 push     eax
005049ba call     dword ptr [0xbf0484]
005049c0 test     eax, eax
005049c2 je       0x5049e3
005049c4 mov      eax, dword ptr [ebp - 0x20]
005049c7 cmp      dword ptr [eax + 0x30], ebx
005049ca je       0x5049d3
005049cc mov      eax, 0x7fffffff
005049d1 jmp      0x5049d6
005049d3 mov      eax, dword ptr [eax + 0x34]
005049d6 mov      ecx, dword ptr [ebp - 0x1c]
005049d9 push     eax
005049da call     0x508679
005049df test     eax, eax
005049e1 je       0x504a0b
005049e3 or       dword ptr [ebp - 4], 0xffffffff
005049e7 cmp      dword ptr [ebp - 0x20], ebx
005049ea jmp      0x5049f2
005049ec or       dword ptr [ebp - 4], 0xffffffff
005049f0 cmp      eax, ebx
005049f2 je       0x504a00
005049f4 push     ebx
005049f5 lea      ecx, [ebp - 0x24]
005049f8 call     0x5089ff
005049fd mov      dword ptr [ebp - 0x20], ebx
00504a00 cmp      dword ptr [ebp - 0x14], ebx
00504a03 jne      0x50488f
00504a09 jmp      0x504a52
00504a0b mov      ecx, dword ptr [ebp - 0x20]
00504a0e cmp      dword ptr [ecx + 0x30], ebx
00504a11 jne      0x504a26
00504a13 push     dword ptr [ecx + 0x34]
00504a16 mov      ecx, dword ptr [0xbe78d8]
00504a1c call     0x5dacd3
00504a21 mov      ecx, dword ptr [ebp - 0x20]
00504a24 jmp      0x504a28
00504a26 xor      eax, eax
00504a28 push     eax
00504a29 push     dword ptr [ecx + 0x20]
00504a2c mov      ecx, dword ptr [ebp + 8]
00504a2f push     dword ptr [ebp + 0xc]
00504a32 call     0x705c7c
00504a37 mov      eax, dword ptr [ebp - 0x20]
00504a3a mov      ecx, dword ptr [ebp - 0x10]
00504a3d or       dword ptr [ebp - 4], 0xffffffff
00504a41 mov      dword ptr [eax + 0x54], ecx
00504a44 cmp      dword ptr [ebp - 0x20], ebx
00504a47 je       0x504a52
00504a49 push     ebx
00504a4a lea      ecx, [ebp - 0x24]
00504a4d call     0x5089ff
00504a52 mov      ecx, dword ptr [ebp - 0xc]
00504a55 pop      edi
00504a56 pop      esi
00504a57 pop      ebx
00504a58 mov      dword ptr fs:[0], ecx
00504a5f leave    
00504a60 ret      8

SHA256 1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2
# function 0x9c4ed0: 170 instructions, 0x9c4ed0..0x9c50be
009c4ed0 push     ebp
009c4ed1 mov      ebp, esp
009c4ed3 sub      esp, 0x10
009c4ed6 push     ebx
009c4ed7 push     esi
009c4ed8 mov      esi, dword ptr [0xbebfa0]
009c4ede mov      ebx, ecx
009c4ee0 lea      eax, [ebx + 0xe0]
009c4ee6 push     edi
009c4ee7 push     dword ptr [eax + 0x10]
009c4eea push     eax
009c4eeb call     0x539338
009c4ef0 fstp     qword ptr [ebp - 0x10]
009c4ef3 mov      eax, dword ptr [ebp + 8]
009c4ef6 push     dword ptr [eax + 0x10]
009c4ef9 push     eax
009c4efa call     0x539338
009c4eff fsubr    qword ptr [ebp - 0x10]
009c4f02 add      esp, 0x10
009c4f05 fst      qword ptr [ebp - 0x10]
009c4f08 fcomp    qword ptr [0xaf0de8]
009c4f0e fnstsw   ax
009c4f10 sahf     
009c4f11 je       0x9c50ad
009c4f17 mov      eax, dword ptr [ebx + 0x14]
009c4f1a test     eax, eax
009c4f1c je       0x9c4f23
009c4f1e add      eax, -4
009c4f21 jmp      0x9c4f25
009c4f23 xor      eax, eax
009c4f25 mov      edi, dword ptr [eax + 0x94]
009c4f2b mov      eax, dword ptr [ebx + 0x110]
009c4f31 mov      ecx, dword ptr [eax + 0x20]
009c4f34 mov      edx, dword ptr [esi + 0x34]
009c4f37 mov      eax, dword ptr [edx + ecx*8]
009c4f3a mov      ecx, dword ptr [edx + ecx*8 + 4]
009c4f3e mov      edx, ecx
009c4f40 sub      edx, eax
009c4f42 cmp      edx, 0x28
009c4f45 mov      dword ptr [ebp - 4], eax
009c4f48 mov      dword ptr [ebp - 8], ecx
009c4f4b jle      0x9c4f59
009c4f4d add      eax, 0x14
009c4f50 add      ecx, -0x14
009c4f53 mov      dword ptr [ebp - 4], eax
009c4f56 mov      dword ptr [ebp - 8], ecx
009c4f59 push     dword ptr [ebx + 0xf0]
009c4f5f lea      esi, [ebx + 0xe0]
009c4f65 push     esi
009c4f66 call     0x539338
009c4f6b fstp     st(0)
009c4f6d fld      qword ptr [ebp - 0x10]
009c4f70 add      esp, 8
009c4f73 fcomp    qword ptr [0xaf0de8]
009c4f79 fnstsw   ax
009c4f7b sahf     
009c4f7c jae      0x9c4fb3
009c4f7e push     dword ptr [esi + 0x10]
009c4f81 push     esi
009c4f82 call     0x539338
009c4f87 fcomp    qword ptr [0xaf0de8]
009c4f8d pop      ecx
009c4f8e pop      ecx
009c4f8f fnstsw   ax
009c4f91 sahf     
009c4f92 jae      0x9c4fb3
009c4f94 mov      eax, dword ptr [ebx + 0x110]
009c4f9a mov      ecx, dword ptr [eax + 0x4c]
009c4f9d test     ecx, ecx
009c4f9f je       0x9c505d
009c4fa5 fld      qword ptr [ecx + 0x30]
009c4fa8 fcomp    qword ptr [0xaf0de8]
009c4fae fnstsw   ax
009c4fb0 sahf     
009c4fb1 jbe      0x9c5001
009c4fb3 fld      qword ptr [ebp - 0x10]
009c4fb6 fcomp    qword ptr [0xaf0de8]
009c4fbc fnstsw   ax
009c4fbe sahf     
009c4fbf jbe      0x9c50ad
009c4fc5 push     dword ptr [esi + 0x10]
009c4fc8 push     esi
009c4fc9 call     0x539338
009c4fce pop      ecx
009c4fcf pop      ecx
009c4fd0 mov      ecx, dword ptr [ebx + 0x110]
009c4fd6 fld      qword ptr [ecx + 0x40]
009c4fd9 fld      st(1)
009c4fdb fcompp   
009c4fdd fnstsw   ax
009c4fdf fstp     st(0)
009c4fe1 sahf     
009c4fe2 jbe      0x9c50ad
009c4fe8 mov      ecx, dword ptr [ecx + 0x50]
009c4feb test     ecx, ecx
009c4fed je       0x9c505d
009c4fef fld      qword ptr [ecx + 0x30]
009c4ff2 fcomp    qword ptr [0xaf0de8]
009c4ff8 fnstsw   ax
009c4ffa sahf     
009c4ffb ja       0x9c50ad
009c5001 fld      qword ptr [ecx + 0x38]
009c5004 fmul     qword ptr [ebp - 0x10]
009c5007 fcomp    qword ptr [0xaf0de8]
009c500d fnstsw   ax
009c500f sahf     
009c5010 jbe      0x9c505d
009c5012 mov      eax, dword ptr [edi + 4]
009c5015 mov      esi, dword ptr [ebx + 0x134]
009c501b lea      ecx, [edi + 4]
009c501e call     dword ptr [eax + 0x18]
009c5021 cmp      eax, esi
009c5023 jne      0x9c505d
009c5025 fld      qword ptr [ebp - 0x10]
009c5028 fcomp    qword ptr [0xaf0de8]
009c502e fnstsw   ax
009c5030 sahf     
009c5031 jae      0x9c5041
009c5033 mov      eax, dword ptr [ebx + 0x110]
009c5039 mov      ecx, dword ptr [ebp - 4]
009c503c cmp      ecx, dword ptr [eax + 0xc]
009c503f jl       0x9c50ad
009c5041 fld      qword ptr [ebp - 0x10]
009c5044 fcomp    qword ptr [0xaf0de8]
009c504a fnstsw   ax
009c504c sahf     
009c504d jbe      0x9c505d
009c504f mov      eax, dword ptr [ebx + 0x110]
009c5055 mov      ecx, dword ptr [ebp - 8]
009c5058 cmp      ecx, dword ptr [eax + 0x14]
009c505b jg       0x9c50ad
009c505d push     dword ptr [ebx + 0x48]
009c5060 lea      eax, [ebx + 0x38]
009c5063 push     eax
009c5064 call     0x539338
009c5069 fstp     qword ptr [ebp - 0x10]
009c506c mov      eax, dword ptr [edi + 4]
009c506f pop      ecx
009c5070 pop      ecx
009c5071 lea      ecx, [edi + 4]
009c5074 call     dword ptr [eax + 0x10]
009c5077 fild     dword ptr [eax + 4]
009c507a fcomp    qword ptr [ebp - 0x10]
009c507d fnstsw   ax
009c507f sahf     
009c5080 jbe      0x9c5091
009c5082 call     0xa61c6d
009c5087 push     2
009c5089 cdq      
009c508a pop      ecx
009c508b idiv     ecx
009c508d test     edx, edx
009c508f jne      0x9c50ad
009c5091 mov      esi, dword ptr [ebp + 8]
009c5094 push     0xc
009c5096 pop      ecx
009c5097 lea      edi, [ebx + 0xe0]
009c509d rep movsd dword ptr es:[edi], dword ptr [esi]
009c509f mov      dword ptr [ebx + 0x168], 1
009c50a9 xor      eax, eax
009c50ab jmp      0x9c50ba
009c50ad push     dword ptr [ebp + 0xc]
009c50b0 mov      ecx, ebx
009c50b2 push     dword ptr [ebp + 8]
009c50b5 call     0x9b3fd1
009c50ba pop      edi
009c50bb pop      esi
009c50bc pop      ebx
009c50bd leave    
009c50be ret      8
