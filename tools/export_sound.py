#!/usr/bin/env python3
"""Export BGM and SFX from the original Sound WZ into MapleDashResources/Data/Sound.

Outputs MP3 (MPEG ADTS, identical payload bytes as stored in the WZ) plus
Assets/Resources/SoundIndex.json mapping a logical key to
{path, ms} so the runtime can load clips via UnityWebRequest.

Extraction scope (kept deliberately small — ~7 MB total):
  Bgm     every bgm referenced by FieldMaps.json
  Mob     Damage/Die/Attack1/Skill1/CharDam1 for every mob used by the game
  Weapon  Attack for each weapon family
  Skill   Use for every skill in GameplaySkills.json
  Game    pickup / jump / levelup / drop / portal / quest / job
  UI      click / menu hover etc.
"""
import json
import os
import sys
from pathlib import Path

sys.path.insert(0, "/Users/lizixian/Documents/mxd/BeiDou-Server/tool/wz-python")
from wzpy import WzImage, WzKey, WzUolProperty

BEIDOU = Path("/Users/lizixian/Documents/mxd/BeiDou-Server/clien/Data/Sound")
ROOT = Path(__file__).resolve().parents[1]
RES = ROOT.parent / "MapleDashResources/Data/Sound"
INDEX = ROOT / "Assets/Resources/SoundIndex.json"

_images = {}


def image(name):
    if name not in _images:
        _images[name] = WzImage.from_bytes(
            (BEIDOU / (name + ".img")).read_bytes(), key=WzKey.for_region("GMS")
        )
    return _images[name]


def root_of(n):
    while getattr(n, "parent", None) is not None:
        n = n.parent
    return n


def lookup(root, path):
    cur = root
    for p in path.split("/"):
        if p == "":
            continue
        if p == "..":
            cur = cur.parent if cur.parent else cur
            continue
        cur = cur.child(p)
        if cur is None:
            return None
    return cur


def resolve(n):
    i = 0
    while isinstance(n, WzUolProperty):
        t = n.value
        if not isinstance(t, str):
            return None
        n = lookup(root_of(n), t)
        i += 1
        if n is None or i > 30:
            return None
    return n


def sound_bytes(node):
    """Raw MP3 payload of a Sound property (wzpy keeps it lazy)."""
    if node is None or node.type_name != "Sound":
        return None, 0
    sp = node
    if getattr(sp, "_data", None) is not None:
        data = sp._data
    else:
        wz_file = sp._wz_image.wz_file
        with wz_file.reader_lock:
            reader = wz_file.reader
            prev = reader.position
            reader.seek(sp._data_offset)
            data = reader.read(sp._data_length)
            reader.seek(prev)
        sp._data = data
    return data, getattr(sp, "length_ms", 0)


def export(key, source, out_rel):
    """source = 'Bgm00/FloralLife' style path inside Sound/<img>."""
    img_name, node_path = source.split("/", 1)
    img = image(img_name)
    node = resolve(lookup(img.root, node_path))
    data, ms = sound_bytes(node)
    if not data:
        print(f"  MISS {key} <- {source}")
        return None
    out = RES / out_rel
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_bytes(data)
    entry = {"path": "Sound/" + out_rel, "ms": ms}
    print(f"  ok   {key:28s} {len(data)/1024:8.1f} KB  {ms/1000:6.1f}s")
    return entry


def main():
    index = {}
    if INDEX.is_file():
        index = json.loads(INDEX.read_text())

    # Direction scenes reference Sound properties outside Sound.wz by their full
    # IMG UOL. Keep that key and export the original MPEG payload without changes.
    if "--direction-only" in sys.argv:
        def visit(node, key):
            data, ms = sound_bytes(node)
            if data:
                relative = "Direction/" + key.removeprefix("Effect/").replace(".img/", "/") + ".mp3"
                out = RES / relative
                out.parent.mkdir(parents=True, exist_ok=True)
                out.write_bytes(data)
                index[key] = {"path": "Sound/" + relative, "ms": ms}
                print(key, len(data), ms)
                return
            for child in node.children():
                visit(child, key + "/" + child.name)
        for file in sorted((BEIDOU.parent / "Effect").glob("Direction*.img")):
            img = WzImage.from_bytes(file.read_bytes(), key=WzKey.for_region("GMS"))
            visit(img.root, "Effect/" + file.name)
        INDEX.write_text(json.dumps(index, ensure_ascii=False, indent=1, sort_keys=True))
        return

    # ---- BGM：全量地图目录里出现过的每一首 + 大厅/关卡主题 ----
    # FieldIndex.json 覆盖资源包里全部 6337 张地图（tools/export_field_index.py 生成）；
    # 旧版只读 FieldMaps.json 的 37 张彩虹岛图，出岛后全是静音。
    catalog = ROOT / "Assets/Resources/Field/FieldIndex.json"
    if catalog.is_file():
        bgms = sorted(json.loads(catalog.read_text())["bgms"])
    else:
        maps = json.loads((ROOT / "Assets/Resources/Field/FieldMaps.json").read_text())["maps"]
        bgms = sorted({m["bgm"] for m in maps if m.get("bgm")})
    bgms += ["BgmUI/Title",        # 大厅（原版标题界面）
             "Bgm00/SleepyWood"]   # 关卡 cliff 主题
    bgms = sorted(set(bgms))
    print(f"== BGM ({len(bgms)}) ==")
    for b in bgms:
        file, song = b.split("/", 1)
        e = export(b, b, f"Bgm/{file}_{song}.mp3")
        if e:
            index[b] = e

    # ---- 怪物音效 ----
    # Mob.img 的键是 7 位带前导零的怪物 ID（蜗牛 = 0100100）
    mob_ids = ["100100", "100101", "120100", "130100", "130101", "210100",
               "1210102", "9300018", "9409000", "9409001", "9500102",
               "9300003", "6130101"]
    mob_img = image("Mob")
    print(f"== Mob ({len(mob_ids)}) ==")
    def has_sound(group, action):
        """该动作是否真的有可解码的 Sound（空 UOL 的 resolve 会跑丢到 root，不能算有）。"""
        ch = group.child(action)
        if ch is None: return False
        r = resolve(ch)
        return r is not None and r.type_name == "Sound"

    for mid in mob_ids:
        key7 = "%07d" % int(mid)          # Sound/Mob.img 用 7 位 ID
        node = resolve(lookup(mob_img.root, key7))
        if node is None:
            print(f"  MISS mob {mid} (no sound group)")
            continue
        # GMS083 本地化把不少小怪的 Damage/Die 清成了空 UOL（源端即静默）。
        # 手感上「打蓝蜗牛没声」不像Bug 也像 Bug，这里对这两个通用动作
        # 兜底用蜗牛（0100100）的音效；Boss 专属的 Attack1/Skill1 不兜底。
        have = {a for a in ("Damage", "Die", "Attack1", "Skill1", "CharDam1")
                if has_sound(node, a)}
        for action in ("Damage", "Die", "Attack1", "Skill1", "CharDam1"):
            if action not in have and action in ("Damage", "Die"):
                src = f"Mob/0100100/{action}"
                note = " (fallback snail)"
            else:
                src = f"Mob/{key7}/{action}"
                note = ""
            e = export(f"Mob/{mid}/{action}", src, f"Mob/{mid}_{action}.mp3")
            if e:
                index[f"Mob/{mid}/{action}"] = e
                if note: print(f"       ^--{note}")

    # ---- 武器音效 ----
    print("== Weapon ==")
    for w in ["swordL", "swordS", "mace", "poleArm", "spear", "bow", "cBow",
              "tGlove", "knuckle", "barehands", "gun"]:
        e = export(f"Weapon/{w}", f"Weapon/{w}/Attack", f"Weapon/{w}_Attack.mp3")
        if e:
            index[f"Weapon/{w}/Attack"] = e

    # ---- 技能音效 ----
    skills = json.loads((ROOT / "Assets/Resources/GameplaySkills.json").read_text())["skills"]
    print(f"== Skill ({len(skills)}) ==")
    for s in skills:
        sid = str(s["id"])
        e = export(f"Skill/{sid}", f"Skill/{sid}/Use", f"Skill/{sid}_Use.mp3")
        if e:
            index[f"Skill/{sid}/Use"] = e

    # ---- 游戏动作音效 ----
    print("== Game ==")
    for g in ["PickUpItem", "Jump", "LevelUp", "DropItem", "GameIn",
              "Portal", "Portal2", "QuestClear", "JobChanged", "Buff",
              "EnchantSuccess", "EnchantFailure"]:
        e = export(f"Game/{g}", f"Game/{g}", f"Game/{g}.mp3")
        if e:
            index[f"Game/{g}"] = e

    # ---- UI 音效 ----
    print("== UI ==")
    for u in ["BtMouseClick", "MenuUp", "MenuDown", "BtMouseOver",
              "DragStart", "DragEnd", "Tab"]:
        e = export(f"UI/{u}", f"UI/{u}", f"UI/{u}.mp3")
        if e:
            index[f"UI/{u}"] = e

    INDEX.write_text(json.dumps(index, ensure_ascii=False, indent=1, sort_keys=True))
    total = sum(
        (RES / e["path"].split("Sound/", 1)[1]).stat().st_size
        for e in index.values()
        if (RES / e["path"].split("Sound/", 1)[1]).is_file()
    )
    print(f"\n索引 {INDEX.name}: {len(index)} 条，音频总大小 {total/1024/1024:.2f} MiB")


if __name__ == "__main__":
    main()
