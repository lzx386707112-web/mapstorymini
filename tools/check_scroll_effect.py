#!/usr/bin/env python3
"""砸卷成功/失败表现链静态校验。

砸卷结果在源端是 SHOW_SCROLL_EFFECT → CUserLocal::ShowScrollEffect：
角色脚下播 `Effect/BasicEff.img/Enchant/{Success|Failure}`，音效取
`Sound/Game.img/Enchant{Success|Failure}`。本项目由
`NativeItemUseEffects.PlayScrollEffect` 复刻，`NativeScrollService` 回传
`success / fail / curse` 三个字符串（诅咒沿用失败表现）。

真机上「砸完没反应」有三种死法，本机可以先挡掉：
  1. 特效节点名写错（`Enchant/Success` vs `enchant/success`），Spawn 拿不到帧会静默返回 null；
  2. 音效没进资源包（SoundIndex 有键、Data 里没有、或没登记 NativeAssetIndex），
     AudioManager.Play 找不到索引就直接放弃；
  3. 客户端改了分支却只播一种表现（比如诅咒也走 Success）。

用法：python3 tools/check_scroll_effect.py [-v]
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
INDEX = ROOT / "Assets/Resources/NativeAssetIndex.json"
SOUND_INDEX = ROOT / "Assets/Resources/SoundIndex.json"
RESOURCES = ROOT.parent / "MapleDashResources/Data"
CLIENT = ROOT / "Assets/Scripts/NativeItemUseEffects.cs"

DATA = Path(os.path.expanduser("~/Documents/mxd/BeiDou-Server/clien/Data"))
WZPY = os.path.expanduser("~/Documents/mxd/BeiDou-Server/tool/wz-python")

# Success = 21 帧、Failure = 16 帧（GMS083）。诅咒没有独立节点，与失败同帧。
FRAME_COUNT = {"Success": 21, "Failure": 16}
# 帧原点是相对角色脚底的像素坐标；y 落在 120~165 说明特效是套在身上的。
ORIGIN_Y_RANGE = (100, 200)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("-v", "--verbose", action="store_true")
    args = ap.parse_args()

    ok = True
    def check(cond: bool, label: str, detail: str = "") -> bool:
        nonlocal ok
        ok = ok and cond
        print(("  ✓ " if cond else "  ✗ ") + label + (f"（{detail}）" if detail else ""))
        return cond

    # ---------------- 1. WZ：两段特效节点与帧 ----------------
    print("[1] Effect/BasicEff.img/Enchant")
    index = json.loads(INDEX.read_text(encoding="utf-8"))
    packed = {entry["path"]: entry["sha256"] for entry in index["files"]}
    check("Effect/BasicEff.img" in packed, "Effect/BasicEff.img 已登记 NativeAssetIndex")
    if not (DATA / "Effect/BasicEff.img").exists():
        print("  ✗ 源端缺少 Effect/BasicEff.img")
        return 1
    sys.path.insert(0, WZPY)
    from wzpy import WzImage, WzKey  # noqa: E402

    img = WzImage.from_bytes((DATA / "Effect/BasicEff.img").read_bytes(),
                             key=WzKey.for_region("GMS"), name="BasicEff.img")
    img.parse()
    for name, count in FRAME_COUNT.items():
        node = img.get(f"Enchant/{name}")
        if not check(node is not None, f"Enchant/{name} 存在"):
            continue
        frames = list(node.children())
        check(len(frames) == count, f"Enchant/{name} 帧数 = {count}", f"实际 {len(frames)}")
        # 源端规则：缺 delay 继承上一帧，缺 origin 才是 (0,0)。本项目 NativeImgAnimation
        # 用的是 Int("delay",100)，所以「缺 delay」不是错误，只是那一帧会慢 50ms。
        runtime, bad_origin, missing = 0, [], []
        for frame in frames:
            fdelay = frame.get("delay")
            origin = frame.get("origin")
            if origin is None:
                bad_origin.append(str(frame.name))
            else:
                y = origin.value[1] if isinstance(origin.value, tuple) else 0
                if not (ORIGIN_Y_RANGE[0] <= y <= ORIGIN_Y_RANGE[1]):
                    bad_origin.append(f"{frame.name}:y={y}")
            if fdelay is None:
                missing.append(str(frame.name))
                runtime += 100
            else:
                ms = max(1, abs(int(fdelay.value)))
                runtime += ms
        check(not bad_origin, f"Enchant/{name} 每帧原点都在角色身上", ",".join(bad_origin[:4]))
        check(len(missing) <= 1, f"Enchant/{name} 缺 delay 的帧 ≤1", ",".join(missing))
        if args.verbose:
            print(f"      运行时总时长 {runtime} ms（缺 delay 的帧按 100ms 兜底）")

    # ---------------- 2. 音效：索引 / 文件 / 资源包 ----------------
    print("[2] 音效 Game/Enchant{Success,Failure}")
    sounds = json.loads(SOUND_INDEX.read_text(encoding="utf-8"))
    for name in FRAME_COUNT:
        key = "Game/Enchant" + name
        entry = sounds.get(key)
        if not check(entry is not None, f"SoundIndex 有 {key}"):
            continue
        rel = entry["path"]
        disk = RESOURCES / rel
        if not check(disk.exists(), f"{rel} 已导出到资源目录"):
            continue
        check(rel in packed, f"{rel} 已登记 NativeAssetIndex")
        if rel in packed:
            sha = hashlib.sha256(disk.read_bytes()).hexdigest()
            check(sha == packed[rel], f"{rel} sha256 与索引一致")
        if args.verbose:
            print(f"      {entry['ms']} ms, {disk.stat().st_size} B")

    # ---------------- 3. 客户端分支 ----------------
    print("[3] 客户端分支 NativeItemUseEffects.PlayScrollEffect")
    source = CLIENT.read_text(encoding="utf-8")
    check('"Effect/BasicEff.img/Enchant/"' in source, "特效路径指向 Enchant")
    check('"Game/Enchant"' in source, "音效键用 Game/Enchant 前缀")
    check('result=="success" ? "Success" : "Failure"' in source,
          "success→Success，fail/curse→Failure")
    check("PlayScrollEffect(result.scrollResult)" in source,
          "ApplyScroll 用服务端 scrollResult 驱动表现（不是本地掷骰）")

    # ---------------- 4. 服务端结果字符串 ----------------
    print("[4] 服务端 scrollResult 取值")
    service = (ROOT / "Assets/Scripts/Server/World/NativeScrollService.cs").read_text(encoding="utf-8")
    for token in ('"curse"', '"success"', '"fail"'):
        check(token in service, f"NativeScrollService 会返回 {token}")

    print()
    print("全部通过" if ok else "存在失败项")
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
