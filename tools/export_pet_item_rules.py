#!/usr/bin/env python3
"""Export original cash pet food specs and ExpTable tameness, without changing sources."""
import hashlib
import json
import re
import sys
from pathlib import Path

project=Path(__file__).resolve().parents[1]
source=project.parents[1]/"Beidou-Server"
sys.path.insert(0,str(source/"tool/wz-python"))
from wzpy import WzImage,WzKey
img_path=project.parent/"MapleDashResources/Data/Item/Cash/0524.img"
img=WzImage.from_bytes(img_path.read_bytes(),key=WzKey.for_region("GMS"))
img.parse()
if img.truncated or img.parse_warnings:
    raise ValueError("Incomplete original pet food IMG")
foods={}
for node in img.root.children():
    spec=node.get("spec")
    if spec: foods[str(int(node.name))]={child.name:child.value for child in spec.children()}
java=source/"gms-server/src/main/java/org/gms/constants/game/ExpTable.java"
thresholds=[int(x.strip()) for x in re.search(r"int\[\] pet = \{([^}]+)",java.read_text()).group(1).split(",")]
pets={}
pet_paths=sorted((project.parent/"MapleDashResources/Data/Item/Pet").glob("*.img"))
for path in pet_paths:
    image=WzImage.from_bytes(path.read_bytes(),key=WzKey.for_region("GMS"));image.parse()
    if image.truncated or image.parse_warnings: raise ValueError("Incomplete pet IMG: "+str(path))
    info=image.root.get("info")
    if info:
        pets[str(int(path.stem))]={key:getattr(info.get(key),"value",default) for key,default in (("hungry",1),("evol1",0))}
result={"sources":{str(path):hashlib.sha256(path.read_bytes()).hexdigest() for path in (img_path,java,*pet_paths)},"tameness":thresholds,"foods":foods,"pets":pets}
(project/"Assets/Resources/PetItemRules.json").write_text(json.dumps(result,ensure_ascii=False,indent=2)+"\n")
print(f"foods={len(foods)} tameness={len(thresholds)} pets={len(pets)}")
