#!/usr/bin/env python3
"""导出仓库 NPC 的存取费用到 Assets/Resources/Storage/TrunkFees.txt。

源端 Storage.getStoreFee/getTakeOutFee：NPC.img/<id>/info/trunkPut|trunkGet，
读不到时默认 put=100 / get=0。只有部分 NPC 带这两个节点（9030100 自由市场仓库
是 500/1000），所以全量扫一遍 Npc/*.img 把有值的导出来。
"""
import os
import sys
from pathlib import Path

WZPY = Path('/Users/lizixian/Documents/mxd/Beidou-Server/tool/wz-python')
DATA = Path('/Users/lizixian/Documents/mxd/mxd-android/MapleDashResources/Data')
OUT = Path('/Users/lizixian/Documents/mxd/mxd-android/MapleDash/Assets/Resources/Storage/TrunkFees.txt')

sys.path.insert(0, str(WZPY))
from wzpy import WzImage, WzKey  # noqa: E402


def main():
    lines = []
    files = sorted((DATA / 'Npc').glob('*.img'))
    for p in files:
        npc_id = p.stem
        try:
            img = WzImage.from_bytes(p.read_bytes(), key=WzKey.for_region('GMS'), name=npc_id)
            img.parse()
            info = img.get('info')
            if info is None:
                continue
            tp, tg = info.get('trunkPut'), info.get('trunkGet')
            put = getattr(tp, 'value', None) if tp is not None else None
            get = getattr(tg, 'value', None) if tg is not None else None
            if put is None and get is None:
                continue
            lines.append('F|%s|%d|%d' % (npc_id, put if put is not None else 100, get if get is not None else 0))
        except Exception as e:  # 个别 img 解析失败不影响整体
            print('skip', npc_id, e, file=sys.stderr)
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text('\n'.join(lines) + '\n', encoding='utf-8')
    print('wrote %d fee rows -> %s' % (len(lines), OUT))


if __name__ == '__main__':
    main()
