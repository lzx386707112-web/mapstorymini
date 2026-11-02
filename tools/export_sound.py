#!/usr/bin/env python3
"""Export BGM and SFX from the original Sound WZ into MapleDashResources/Data/Sound.

Outputs MP3 (MPEG ADTS, identical payload bytes as stored in the WZ) plus
Assets/Resources/SoundIndex.json mapping a logical key to
{path, ms} so the runtime can load clips via UnityWebRequest.

Extraction scope:
  Bgm     every bgm referenced by Field/FieldIndex.json (all 6337 maps, 264 songs)
  Mob     Damage/Die/Attack1/Skill1/CharDam1 for every mob used by the game
  Weapon  Attack for each weapon family
  Skill   Use for every skill in GameplaySkills.json
  Game    pickup / jump / levelup / drop / portal / quest / job
  UI      click / menu hover etc.

The BGM set used to come from FieldMaps.json (37 Maple Island maps only), which
left every map off-island silent. Run this script again whenever FieldIndex.json
is regenerated, then re-register the resulting MP3s in NativeAssetIndex.json —
sync_android_data.py / build_resource_pack.py only ship files listed there.

Usage:
  python3 tools/export_sound.py             # export BGM + SFX, write SoundIndex.json
  python3 tools/export_sound.py --register  # re-register every MP3 into NativeAssetIndex.json
"""
import hashlib
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
_img_lower = None
_trimmed_files = 0
_trimmed_bytes = 0


def trim_to_mp3(data):
    """Strip the non-MPEG prefix some WZ Sound properties carry.

    52 of the extracted clips (mostly BgmCN / BgmSG / BgmJp and a few Skill Use
    sounds) start with 80 or 82 bytes of WZ metadata before the first MPEG frame.
    ffprobe still recognises them, but Unity's AudioType.MPEG decoder is not
    guaranteed to skip leading garbage, so normalise to a plain MP3 stream.
    ID3-tagged clips are left intact.
    """
    if data[:3] == b"ID3":
        return data
    for i in range(min(len(data) - 3, 512)):
        if data[i] != 0xFF or (data[i + 1] & 0xE0) != 0xE0:
            continue
        version = (data[i + 1] >> 3) & 3      # 1 is reserved
        layer = (data[i + 1] >> 1) & 3        # 0 is reserved
        bitrate = (data[i + 2] >> 4) & 15     # 0 free / 15 invalid
        samplerate = (data[i + 2] >> 2) & 3   # 3 is reserved
        if version != 1 and layer != 0 and bitrate not in (0, 15) and samplerate != 3:
            return data[i:]
    return data


def image_name(name):
    """Resolve a Sound IMG stem against the source tree, case-insensitively.

    The shipped field data contains one typo: `BGM06/FinalFight`, while the source
    file is `Bgm06.img` (which does contain FinalFight). Keep the logical key as
    the map wrote it, but resolve the stem here so a single bad entry cannot abort
    the whole 264-song batch.
    """
    global _img_lower
    if (BEIDOU / (name + ".img")).is_file():
        return name
    if _img_lower is None:
        _img_lower = {p.stem.lower(): p.stem for p in BEIDOU.glob("*.img")}
    return _img_lower.get(name.lower())


def image(name):
    if name not in _images:
        path = BEIDOU / (name + ".img")
        if not path.is_file():
            raise FileNotFoundError("Sound IMG 不存在：" + str(path))
        _images[name] = WzImage.from_bytes(
            path.read_bytes(), key=WzKey.for_region("GMS")
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
    """source = 'Bgm00/FloralLife' style path inside Sound/<img>.

    Missing IMGs / nodes are reported and skipped rather than raised: one bad
    entry in a 264-song catalog must not abort the whole export.
    """
    global _trimmed_files, _trimmed_bytes
    img_name, node_path = source.split("/", 1)
    stem = image_name(img_name)
    if stem is None:
        print(f"  MISS {key} <- {source} (源端无 {img_name}.img)")
        return None
    img = image(stem)
    node = resolve(lookup(img.root, node_path)) if img.root is not None else None
    data, ms = sound_bytes(node)
    if not data:
        print(f"  MISS {key} <- {source}")
        return None
    payload = trim_to_mp3(data)
    if len(payload) != len(data):
        _trimmed_files += 1
        _trimmed_bytes += len(data) - len(payload)
    out = RES / out_rel
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_bytes(payload)
    entry = {"path": "Sound/" + out_rel, "ms": ms}
    print(f"  ok   {key:28s} {len(payload)/1024:8.1f} KB  {ms/1000:6.1f}s")
    return entry


def main():
    index = {}
    if INDEX.is_file():
        index = json.loads(INDEX.read_text())

    # --register: sync_android_data.py and build_resource_pack.py only ship files
    # listed in NativeAssetIndex.json["files"]. MP3s produced above live in the
    # runtime Data tree, not in the source WZ tree that originally built that
    # index, so they must be registered explicitly or the device stays silent.
    # Re-scan the whole Sound tree (not just BGM) so the Direction/Field sets are
    # covered too; safe to re-run, sha256 is refreshed when bytes change.
    if "--register" in sys.argv:
        idx_path = ROOT / "Assets/Resources/NativeAssetIndex.json"
        native = json.loads(idx_path.read_bytes())
        # The checked-in index is indent=1 (219k lines). Always write that
        # format: package_native_assets.py emits compact JSON, and re-serialising
        # compact would bury the real 290-entry change in a 200k-line diff.
        files = native["files"]
        by_path = {e["path"]: e for e in files}
        added = updated = 0
        live = set()
        for p in sorted(RES.rglob("*.mp3")):
            rel = "Sound/" + p.relative_to(RES).as_posix()
            live.add(rel)
            digest = hashlib.sha256(p.read_bytes()).hexdigest()
            cur = by_path.get(rel)
            if cur is None:
                by_path[rel] = {"path": rel, "sha256": digest}
                files.append(by_path[rel])
                added += 1
            elif cur.get("sha256") != digest:
                cur["sha256"] = digest
                updated += 1
        # A stale MP3 entry makes sync_android_data.py abort with FileNotFoundError
        # before pushing anything, so prune disappeared files in the same pass.
        stale = {p for p in by_path
                 if p.startswith("Sound/") and p.endswith(".mp3") and p not in live}
        if stale:
            files[:] = [e for e in files if e["path"] not in stale]
        raw = json.dumps(native, ensure_ascii=False, indent=1)
        idx_path.write_text(raw)
        manifest = ROOT.parent / "MapleDashResources/resource-manifest.json"
        if manifest.is_file():
            manifest.write_text(raw)
        sound_n = sum(1 for e in files if e["path"].startswith("Sound/"))
        print(f"NativeAssetIndex: 新增 {added} 条 / 刷新 sha256 {updated} 条 / "
              f"清理失效 {len(stale)} 条，Sound 共 {sound_n} 条，索引总计 {len(files)} 条")
        return

    # FIELD_EFFECT mode=4 paths used by original harp NPC scripts.
    if "--npc-field-only" in sys.argv:
        for key in ["orbis/" + note for note in ("do", "re", "mi", "pa", "sol", "la", "si")] + ["Party1/Clear", "Party1/Failed"]:
            entry = export(key, "Field/" + key, "Field/" + key + ".mp3")
            if entry is None:
                raise RuntimeError("Missing original NPC field sound: " + key)
            index[key] = entry
        key = "Bgm04/Shinin'Harbor"
        entry = export(key, key, "Bgm/" + key + ".mp3")
        if entry is None:
            raise RuntimeError("Missing original credits BGM: " + key)
        index[key] = entry
        INDEX.write_text(json.dumps(index, ensure_ascii=False, indent=1, sort_keys=True))
        return

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
    bgm_miss = []
    for b in bgms:
        file, song = b.split("/", 1)
        # 用归一化后的真实 IMG 名做文件名（BGM06 -> Bgm06），逻辑 key 保持地图原样。
        stem = image_name(file) or file
        e = export(b, b, f"Bgm/{stem}_{song}.mp3")
        if e:
            index[b] = e
        else:
            bgm_miss.append(b)
    print(f"== BGM 导出完成 {len(bgms) - len(bgm_miss)}/{len(bgms)}，缺失 {len(bgm_miss)} ==")
    for b in bgm_miss:
        print("   MISS BGM:", b)

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

    jukebox = export("Jukebox/Congratulation", "Jukebox/Congratulation", "Jukebox/Congratulation.mp3")
    if jukebox:
        index["Jukebox/Congratulation"] = jukebox

    # ---- UI 音效 ----
    print("== UI ==")
    for u in ["BtMouseClick", "MenuUp", "MenuDown", "BtMouseOver",
              "DragStart", "DragEnd", "Tab", "VegaSuccess", "VegaFail", "VegaTwinkling"]:
        e = export(f"UI/{u}", f"UI/{u}", f"UI/{u}.mp3")
        if e:
            index[f"UI/{u}"] = e

    INDEX.write_text(json.dumps(index, ensure_ascii=False, indent=1, sort_keys=True))
    def size_of(e):
        p = RES / e["path"].split("Sound/", 1)[1]
        return p.stat().st_size if p.is_file() else 0
    total = sum(size_of(e) for e in index.values())
    bgm_entries = {k: v for k, v in index.items()
                   if v["path"].startswith("Sound/Bgm/") and not k.startswith("BgmUI/")}
    bgm_size = sum(size_of(e) for e in bgm_entries.values())
    print(f"\n索引 {INDEX.name}: {len(index)} 条，音频总大小 {total/1024/1024:.2f} MiB")
    print(f"BGM 已登记 {len(bgm_entries)} 首 / {bgm_size/1024/1024:.2f} MiB；"
          f"FieldIndex 目标 {len(bgms)} 首，缺失 {len(bgm_miss)} 首")
    print(f"剥离非 MPEG 前缀：{_trimmed_files} 个文件 / {_trimmed_bytes} 字节")


if __name__ == "__main__":
    main()
