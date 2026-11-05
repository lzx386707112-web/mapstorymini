#!/usr/bin/env python3
"""按 docs/legacy-advanced-deletion.json 从运行时 Skill/<job>.img 里物理移除
旧 TMS 五/六转技能节点。

约束（全部内置校验，任一失败即不落盘）：
  1. 只动 MapleDashResources/Data/Skill/<job>.img（北斗源端 clien/Data 只读）。
  2. 删除前检查「保留节点 -> 被删节点」的 UOL 引用（_inlink/_outlink），
     以及跨 img 引用目标是否仍然可解析。孤儿引用直接报错。
  3. 重新序列化后：
       - RuntimeImgMirror（img_bake.py，ImgFile.cs 的逐字节复刻）必须能解析；
       - wzpy 重新解析后，保留节点的树结构/值/画布像素与原文逐一相等；
       - 被删 id 必须消失，其余 id 顺序不变。

用法：
    python3 tools/prune_legacy_skills.py --check   # 只校验，不写
    python3 tools/prune_legacy_skills.py           # 写回运行时 img
"""
from __future__ import annotations

import argparse
import hashlib
import io
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]          # MapleDash/
REPO = ROOT.parent                                   # mxd-android/
RES = REPO / 'MapleDashResources/Data/Skill'
sys.path.insert(0, '/Users/lizixian/Documents/mxd/BeiDou-Server/tool/wz-python')
sys.path.insert(0, str(ROOT / 'tools/ms282'))

from wzpy import WzImage, WzKey  # noqa: E402
from wzpy.canvas import _decompress  # noqa: E402
from wzpy.properties import (  # noqa: E402
    WzCanvasProperty, WzProperty, WzUolProperty,
)
from wzpy.reader import WzBinaryReader  # noqa: E402
from wzpy.writer import encode_image_body  # noqa: E402
from img_bake import RuntimeImgMirror  # noqa: E402

GMS = WzKey.for_region('GMS')
DELETION = REPO / 'docs/legacy-advanced-deletion.json'


def load_jobs() -> dict[int, set[int]]:
    data = json.loads(DELETION.read_text())
    out: dict[int, set[int]] = {}
    for e in data['delete']:
        out.setdefault(e['job'], set()).add(e['id'])
    if data['review']:
        raise SystemExit(f"删除清单仍有 {len(data['review'])} 条未判定，先跑 audit")
    return out


def signature(node: WzProperty) -> tuple:
    """节点指纹：类型/值/画布尺寸与像素哈希，用于证明保留节点未被改写。"""
    if isinstance(node, WzCanvasProperty):
        raw = _decompress(node, GMS)
        return ('canvas', node.width, node.height, node.format, node.format2,
                hashlib.sha256(raw).hexdigest())
    if isinstance(node, WzUolProperty):
        return ('uol', str(node.value))
    value = getattr(node, 'value', None)
    if isinstance(value, tuple):
        value = tuple(value)
    return (type(node).__name__, repr(value))


def tree_sig(node: WzProperty) -> tuple:
    return (node.name, signature(node), tuple(tree_sig(c) for c in node.children()))


def collect_links(node: WzProperty, base: str = 'skill') -> list[tuple[str, str]]:
    out = []
    for c in node.children():
        path = f'{base}/{c.name}'
        if isinstance(c, WzUolProperty):
            out.append((path, str(c.value)))
        out.extend(collect_links(c, path))
    return out


def check_links(img: WzImage, job: int, deleted: set[int],
                deleted_by_img: dict[str, set[int]],
                cache: dict[str, WzImage], problems: list[str]) -> None:
    """保留子树里的 UOL 必须仍能解析（不指向被删 id，跨 img 目标存在）。"""
    for path, value in collect_links(img.get('skill')):
        first = path.split('/')[1]
        if first.isdigit() and int(first) in deleted:
            continue  # 被删子树内部的引用，跟着一起消失
        if '.img/' in value:
            img_rel, _, rest = value.partition('.img/')
            img_rel += '.img'
            if img_rel not in cache:
                p = RES / img_rel
                if not p.exists():
                    problems.append(f'{job}: {path} -> 缺失 {img_rel}')
                    continue
                cache[img_rel] = WzImage.from_bytes(p.read_bytes(), key=GMS, name=img_rel)
            target = cache[img_rel]
            target_deleted = deleted_by_img.get(img_rel, set())
            head = rest.split('/')[0]
            node = target.get(head)
            if node is None:
                problems.append(f'{job}: {path} -> {value} 目标不存在')
            elif head.isdigit() and int(head) in target_deleted:
                problems.append(f'{job}: {path} -> {value} 指向被删技能')
            continue
        # 同 img 的 ../ 引用
        parts = value.split('/')
        depth = 0
        while parts and parts[0] == '..':
            parts.pop(0)
            depth += 1
        if depth > 1:
            problems.append(f'{job}: {path} -> {value} 引用超出 skill 根')
            continue
        if parts and parts[0].isdigit() and int(parts[0]) in deleted:
            problems.append(f'{job}: {path} -> {value} 指向被删技能')


def prune(job: int, deleted: set[int], deleted_by_img: dict[str, set[int]],
          write: bool) -> dict:
    path = RES / f'{job}.img'
    original = path.read_bytes()
    img = WzImage.from_bytes(original, key=GMS, name=f'{job}.img')
    img.parse()
    root = img.get('skill')
    before_ids = [int(n.name) for n in root.children() if n.name.isdigit()]
    missing = sorted(deleted - set(before_ids))
    if missing:
        raise SystemExit(f'{job}: 清单里的 {missing} 不在 img 中')

    problems: list[str] = []
    check_links(img, job, deleted, deleted_by_img, {}, problems)
    if problems:
        raise SystemExit('引用校验失败:\n  ' + '\n  '.join(problems))

    kept_sigs = {sid: tree_sig(root.get(str(sid)))
                 for sid in before_ids if sid not in deleted}
    for sid in deleted:
        del root._children[str(sid)]

    data = encode_image_body(img, WzBinaryReader(io.BytesIO(), GMS))
    RuntimeImgMirror(data, GMS)          # ImgFile.cs 口径
    back = WzImage.from_bytes(data, key=GMS, name=f'{job}.img')
    back.parse()
    root2 = back.get('skill')
    after_ids = [int(n.name) for n in root2.children() if n.name.isdigit()]
    if after_ids != [i for i in before_ids if i not in deleted]:
        raise SystemExit(f'{job}: id 序列不一致 {after_ids[:8]}')
    for sid in after_ids:
        if tree_sig(root2.get(str(sid))) != kept_sigs[sid]:
            raise SystemExit(f'{job}: 保留技能 {sid} 内容被改写')

    if write:
        path.write_bytes(data)
    return {'job': job, 'deleted': len(deleted), 'kept': len(after_ids),
            'before': len(original), 'after': len(data)}


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--check', action='store_true')
    args = ap.parse_args()

    jobs = load_jobs()
    deleted_by_img = {f'{job}.img': ids for job, ids in jobs.items()}
    total_b = total_a = 0
    for job in sorted(jobs):
        r = prune(job, jobs[job], deleted_by_img, write=not args.check)
        total_b += r['before']
        total_a += r['after']
        print(f"{job}: -{r['deleted']} -> keep {r['kept']}  "
              f"{r['before']/1048576:.1f}MB -> {r['after']/1048576:.1f}MB")
    print(f'total {total_b/1048576:.1f}MB -> {total_a/1048576:.1f}MB '
          f'(-{(total_b-total_a)/1048576:.1f}MB)  '
          f'mode={"check" if args.check else "write"}')


if __name__ == '__main__':
    main()
