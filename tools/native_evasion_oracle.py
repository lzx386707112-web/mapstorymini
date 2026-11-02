#!/usr/bin/env python3
"""Execute BeiDou 79286E physical evasion, stubbing only secured stat accessors."""
import argparse,json,struct,math
from unicorn.x86_const import UC_X86_REG_ESP,UC_X86_REG_EAX
from native_hidden_contact_oracle import machine
from native_movement_oracle import SHA

def run(u,avoid,accuracy,player_level,mob_level,job,roll):
    frame=0x1008000; mob=0x1001000; character=0x1002000; temporary=0x1003000
    def put(a,v):u.mem_write(a,struct.pack('<I',v))
    for a,v in [(mob,mob_level),(mob+0x64,accuracy),(mob+0x68,0),(character+0xc,player_level),
                (character+0x18,job),(temporary+0xf0,avoid)]:put(a,v)
    u.mem_write(frame,struct.pack('<IIIIII',0x100f000,mob,character,character,temporary,roll))
    u.reg_write(UC_X86_REG_ESP,frame)
    u.emu_start(0x79286e,0x100f000,count=1000)
    return bool(u.reg_read(UC_X86_REG_EAX))

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('exe');p.add_argument('--output',required=True);a=p.parse_args()
    u=machine(a.exe)
    u.mem_write(0x416563,bytes.fromhex('8b4424048b00c3'))
    u.mem_write(0x77deb2,bytes.fromhex('31c0c20400')) # base contribution supplied in total avoid
    cases=[]
    for avoid in (0,1,3,40,999,1500):
      for accuracy in (0,1,10,999,1500):
       for player_level,mob_level in ((1,1),(1,30),(30,1)):
        for job in (0,112,412,1412):
         for roll in (0,199999,200000,499999,500000,7999999,8000000,9499999,9500000,9999999):
          result=run(u,avoid,accuracy,player_level,mob_level,job,roll)
          total=max(0,min(999,avoid)-max(0,mob_level-player_level)//2)
          acc=max(0,min(999,accuracy)); thief=job%1000//100==4
          raw=total/(acc*4.5)*100 if acc else (math.inf if total else math.nan)
          # Native unordered x87 comparisons preserve NaN (0 / 0 => no dodge).
          chance=math.nan if math.isnan(raw) else min(95 if thief else 80,max(5 if thief else 2,raw))
          expected=chance>roll*(100/9999999)
          assert result==expected,(avoid,accuracy,player_level,mob_level,job,roll,result,chance)
          cases.append(dict(avoid=avoid,accuracy=accuracy,level=player_level,mobLevel=mob_level,job=job,roll=roll,miss=result))
    with open(a.output,'w') as f:json.dump(dict(sha256=SHA,cases=cases),f,separators=(',',':'))
    print(len(cases),'original x86 physical evasion cases passed')
