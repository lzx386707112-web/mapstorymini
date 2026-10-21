#!/usr/bin/env python3
"""Re-export native character poses selected by weapon info.walk / info.stand.
Preserves existing non-character manifest sections. Client data is read-only.
"""
from __future__ import annotations
import os, sys, json
from pathlib import Path
from typing import Dict, List, Tuple
import wzmap
from wzpy import WzImage, WzKey, open_wz
from wzpy.character import CharacterRenderer, DEFAULT_EAR_TYPE
from wzpy.properties import WzSubProperty
DATA = wzmap.DATA
REGION = "GMS"
ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Library/WzActions"
UNITY = ROOT / "Assets/Resources/Art"
BODY, HEAD, FACE = "00002000", "00012000", "00020000"
CHARS: Dict[str, dict] = {
    "knight": {  # 主角·剑士
        "name": "剑士（主角）",
        "ids": [BODY, HEAD, FACE, "00030000", "01040002", "01060000", "01070000", "01302000"],
        "atk": "swingO1",
    },
    "warrior": {  # 英雄·重装战士
        "name": "战士（英雄）",
        "ids": [BODY, HEAD, FACE, "00030120", "01040010", "01061000", "01071000", "01402000"],
        "atk": "swingT1",
    },
    "mage": {  # 英雄·法师
        "name": "法师（英雄）",
        "ids": [BODY, HEAD, FACE, "00030250", "01040032", "01060030", "01070030", "01382000"],
        "atk": "magic1",
    },
    "archer": {  # 英雄·弓手
        "name": "弓手（英雄）",
        "ids": [BODY, HEAD, FACE, "00030300", "01040055", "01060050", "01070050", "01452000"],
        "atk": "shoot1",
    },
    "priest": {  # 英雄·牧师
        "name": "牧师（英雄）",
        "ids": [BODY, HEAD, FACE, "00030410", "01040060", "01060060", "01070060", "01322000"],
        "atk": "heal",
    },
}

# 头发固定在 ids[3]（0003xxxx，对应客户端 Character/Hair）。
# 装扮模块要提供几种「真实发型」：默认发型之外，再给几个同系列的不同剪裁。
# 抽之前会在 extract_char 里逐个验证，合不成的回退到默认发型，保证不缺档。
HAIR_SLOT = 3
HAIR_EXTRA = ["00030010", "00030020", "00030040", "00030060", "00030080"]


def ids_for_hair(base_ids: list, hair_id: str) -> list:
    ids = list(base_ids)
    ids[HAIR_SLOT] = hair_id
    return ids



def load_img(path: str) -> WzImage:
    data = open(path, "rb").read()
    img = WzImage.from_bytes(data, key=WzKey.for_region(REGION), name=os.path.basename(path))
    img.parse()
    return img

def num(node, key: str, default: int = 0) -> int:
    v = node.get(key)
    return int(getattr(v, "value", default)) if v is not None else default

def ensure_dir(p: str) -> str:
    os.makedirs(p, exist_ok=True)
    return p

SKILL_KEYS = {
    "knight": ["crush","dash","roar","illusion"],
    "warrior": ["rage","rupture","magnet","berserk"],
    "mage": ["firearrow","doom","chain","starfall"],
    "archer": ["firebolt","arrowrain","dragon","storm"],
    "priest": ["holyarrow","holy","ray","libra"],
}
SKILL_ACTIONS = {}

def char_anims(cfg):
    info = load_img(os.path.join(DATA, "Character", "Weapon", cfg["ids"][-1]+".img")).get("info")
    result = {"stand": ["stand"+str(num(info,"stand",1))],
            "walk": ["walk"+str(num(info,"walk",1))], "jump": ["jump"],
            "rope": ["rope"], "ladder": ["ladder"], "atk": [cfg["atk"]], "hit": ["alert"], "die": ["dead"]}
    key = next(key for key, value in CHARS.items() if value is cfg)
    for skill in SKILL_KEYS[key]:
        if SKILL_ACTIONS.get(skill): result["skill_"+skill] = SKILL_ACTIONS[skill][:1]
        if SKILL_ACTIONS.get(skill+"_prepare"): result["skill_"+skill+"_prepare"] = SKILL_ACTIONS[skill+"_prepare"][:1]
    return result

def extract_char(key: str, cfg: dict, out_root: str, renderer: CharacterRenderer,
                 hair_ids: List[str]) -> Dict[str, dict]:
    """抽一个职业的全部帧，但发型做成「多版本」：每个版本只是把 ids[HAIR_SLOT]
    换成不同的真实发型 id，其余（身体/头/脸/衣/裤/鞋/武器）完全一致。

    所有发型版本共用同一张世界画布（union bbox）和同一个轴心，所以它们在 Unity
    里严丝合缝对齐 —— 这正是「直接拿原游戏的头发资源来用」而不是手搓染色。
    """
    base_ids = list(cfg["ids"])
    default_hair = base_ids[HAIR_SLOT]

    # 发型清单：默认 + 额外几种，去重
    variants: List[str] = []
    seen = set()
    for hid in [default_hair] + list(hair_ids):
        if hid in seen:
            continue
        seen.add(hid)
        variants.append(hid)

    body = load_img(os.path.join(DATA, "Character", BODY + ".img"))
    anims_src = char_anims(cfg)

    # 帧结构（和发型无关，从身体帧数推出）
    frames_meta = []          # (anim, pose, frame, delay)
    for anim, poses in anims_src.items():
        for pose in poses:
            node = body.get(pose)
            n = len([c for c in node.children()]) if isinstance(node, WzSubProperty) else 1
            for f in range(max(1, n)):
                dly = 120
                if isinstance(node, WzSubProperty):
                    dly = num(node.get(str(f)), "delay", 120)
                frames_meta.append((anim, pose, f, max(1, abs(dly))))

    def resolve_frame(pose, frame):
        # Body actions such as brandish1/alert4 redirect to real action/frame pairs.
        seen=set()
        while (pose,frame) not in seen:
            seen.add((pose,frame))
            node=body.get(f"{pose}/{frame}")
            action=node.get("action") if node else None
            if action is None: return pose,frame
            pose=str(action.value); frame=num(node,"frame",0)
        raise ValueError("Cyclic character action")

    def compose(ids, pose, frame, bbox):
        # CharacterRenderer puts navel at (0,0); the client positions the body
        # canvas origin at the foothold. Restore that frame's native map/navel.
        pose,frame = resolve_frame(pose,frame)
        # Forced F attacks are omitted from the renderer UI pose whitelist.
        # Resolve the weapon's one/two-handed swing variant, never fall back to stand.
        weapon=wzmap._wz("Character/Weapon/"+ids[-1])
        if weapon.get(pose) is None and pose.startswith("swingO") and weapon.get(pose.replace("swingO","swingT",1)) is not None:
            pose=pose.replace("swingO","swingT",1)
        hidden, slots, tokens = renderer._cap_hair_filter(ids)
        placements = renderer._build_placements(ids, pose, DEFAULT_EAR_TYPE, hidden, slots, tokens, frame)
        base = next((p for p in placements if p.category == "Body" and p.name == "body"), None)
        dx, dy = base.map_anchors.get("navel", (0, 0)) if base else (0, 0)
        shifted = (bbox[0]-dx, bbox[1]-dy, bbox[2]-dx, bbox[3]-dy)
        return renderer._render_placements(placements, bbox=shifted)

    # 第一遍：把每个发型都合一遍，求所有帧在世界坐标里的并集画布，
    # 避免长发/宽发被裁掉。
    BIG = (-240, -240, 240, 240)
    union = None
    for hid in variants:
        ids = ids_for_hair(base_ids, hid)
        for anim, pose, f, _ in frames_meta:
            try:
                im = compose(ids, pose=pose, frame=f, bbox=BIG)
            except Exception:
                continue
            bb = im.getbbox()
            if bb:
                wb = (bb[0] + BIG[0], bb[1] + BIG[1], bb[2] + BIG[0], bb[3] + BIG[1])
                union = wb if union is None else (min(union[0], wb[0]), min(union[1], wb[1]),
                                                 max(union[2], wb[2]), max(union[3], wb[3]))

    if union is None:
        raise RuntimeError(f"{key} 没渲染出任何帧")

    # 统一画布：脚底留 6px、头顶留 8px
    bbox = (union[0] - 6, union[1] - 6, union[2] + 6, union[3] + 8)
    w, h = bbox[2] - bbox[0], bbox[3] - bbox[1]

    # 原生身体 origin 是运动坐标，不再从武器/发型不透明边界猜脚底。
    pivot = (-bbox[0], -bbox[1])

    out: Dict[str, dict] = {}
    for i, hid in enumerate(variants):
        ids = ids_for_hair(base_ids, hid)
        d = ensure_dir(os.path.join(out_root, "chars", f"{key}_h{i}"))
        anims: Dict[str, dict] = {}
        counters: Dict[str, int] = {}
        ok = 0
        for anim, pose, f, dly in frames_meta:
            try:
                im = compose(ids, pose=pose, frame=f, bbox=bbox)
            except Exception:
                # 这个发型某一帧合不出 → 跳过该帧（通常整发型都合不出会在外部回退）
                continue
            k = counters.get(anim, 0)
            counters[anim] = k + 1
            name = f"{anim}_{k}.png"
            im.save(os.path.join(d, name))
            entry = anims.setdefault(anim, {"frames": [], "delays": []})
            entry["frames"].append(f"chars/{key}_h{i}/{name}")
            entry["delays"].append(dly)
            ok += 1
        if ok == 0:
            # 整发型合不出（id 在 WZ 里不存在/不支持）→ 回退到默认发型
            print(f"  ! {key} 发型 {hid} 无法合成，回退默认")
            continue
        print(f"  角色 {key}_h{i:<3} 发型 {hid:<9} {w}x{h}  帧 {sum(len(v['frames']) for v in anims.values())}")
        out[f"{key}_h{i}"] = {"name": cfg["name"], "size": [w, h], "pivot": list(pivot), "anims": anims}

    # 兜底：至少有一个默认发型版本
    if f"{key}_h0" not in out:
        out[f"{key}_h0"] = out[next(iter(out))]
    return out

def main():
    physics = {c.name: c.value for c in wzmap._wz("Map/Physics")._root.children()}
    (UNITY.parent / "Physics.json").write_text(json.dumps(physics, indent=2))
    manifest = json.loads((UNITY/"manifest.json").read_text())
    body = wzmap._wz("Character/00002000")
    for key, skill in manifest["skills"].items():
        sid = skill["id"]
        node = wzmap._wz("Skill/"+str(int(sid)//10000).zfill(3)).get("skill/"+sid)
        action = node.get("action") if node else None
        poses = [str(c.value) for c in action.children()] if action else []
        SKILL_ACTIONS[key] = [pose for pose in poses if body.get(pose) is not None]
        prepare=node.get("prepare/action") if node else None
        if prepare is not None and body.get(str(prepare.value)) is not None:
            SKILL_ACTIONS[key+"_prepare"]=[str(prepare.value)]
        skill["actions"] = poses
    renderer = CharacterRenderer(open_wz(os.path.join(DATA,"Character"),region=REGION))
    for key, cfg in CHARS.items():
        entries = extract_char(key,cfg,str(OUT),renderer,HAIR_EXTRA)
        for entry in entries.values():
            entry["sourcePoses"] = char_anims(cfg)
            for clip in entry["anims"].values():
                for rel in clip["frames"]:
                    dst = UNITY / Path(rel).with_suffix(".bytes")
                    dst.parent.mkdir(parents=True,exist_ok=True)
                    dst.write_bytes((OUT/rel).read_bytes())
        manifest["chars"].update(entries)
    (UNITY/"manifest.json").write_text(json.dumps(manifest,ensure_ascii=False,indent=1))
if __name__ == "__main__": main()
