#!/usr/bin/env python3
"""Copy source nxcoupons migration rows without inventing schedules/rates."""
import argparse
import hashlib
import json
import re
from pathlib import Path

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--source',type=Path,default=Path.home()/'Documents/mxd/Beidou-Server/gms-server/src/main/resources/db/migration')
args=parser.parse_args()
names=['V1.0.54__nxcoupons_insert_data.sql','V1.8.8__update_nxcoupons.sql']
rows=[]
sources={}
for name in names:
    data=(args.source/name).read_bytes()
    sources[name]=hashlib.sha256(data).hexdigest()
    for values in re.findall(r'\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)',data.decode()):
        _,item,rate,days,start,end=map(int,values)
        rows.append(dict(item=item,rate=rate,days=days,start=start,end=end))
out=Path(__file__).resolve().parents[1]/'Assets/Resources/ItemCouponSchedules.json'
out.write_text(json.dumps(dict(sources=sources,rules=rows),ensure_ascii=False,indent=2)+'\n')
print(f'{len(rows)} original coupon rules -> {out}')
