#!/usr/bin/env python3
"""导出 TMS 282 全量武器（Character.wz/Weapon）的 info 字段 + String/Eqp 名称。

  python3 export_weapons.py --weapon-dir <Data/Character/Weapon> \
      --eqp-xml <wz282/String.wz/Eqp.img.xml> --out ../../docs/tms282-skill/data/weapons.tsv

节点口径：
- 每个 weapon img 的根下只有 `info`（属性）与攻击动作帧（walk1/stand1/attack1…），
  本脚本只读 `info`，帧动画交给 04-vfx-playback.md。
- `_outlink` / `_inlink` 是 282 的像素外链（画布在 Data/Character/Weapon/_Canvas），
  与属性无关，不解析。

字段缺失一律写空串，不做默认值填充，避免把「未声明」误当成 0。
"""
import argparse
import os
import re
import sys
from xml.etree import ElementTree as ET

WZPY = '/Users/lizixian/Documents/mxd/Beidou-Server/tool/wz-python'
sys.path.insert(0, WZPY)

from wzpy import WzImage, WzKey  # noqa: E402

# info 下按需取出的字段，顺序即 TSV 列序
FIELDS = [
    'islot', 'vslot', 'reqJob', 'reqLevel', 'reqSpecJob', 'reqSTR', 'reqDEX',
    'reqINT', 'reqLUK', 'reqPOP', 'incPAD', 'incMAD', 'incSTR', 'incDEX',
    'incINT', 'incLUK', 'incACC', 'incEVA', 'incSpeed', 'incJump', 'tuc',
    'attackSpeed', 'price', 'cash', 'tradeBlock', 'equipTradeBlock',
    'notSale', 'only', 'questEx', 'timeLimited', 'bossReward', 'afterImage',
    'sfx', 'walk', 'stand', 'attack', 'effect', 'noMoveToLocker',
    'exItem', 'exGrade', 'exGradeOption', 'setItemID', 'charmEXP',
    'willEXP', 'dropRate', 'mesoRate', 'bdR', 'imdR', 'padRate', 'madRate',
    'damageSkin', 'sample', 'tradeAvailable', 'uncraftable', 'noPotential',
]

# WZ 目录号 -> 武器类型（282 客户端 TMS 口径，由 itemid 前 4 位判定）
CATEGORY = {
    '121': '单手剑', '122': '单手斧', '123': '单手钝器', '124': '短剑',
    '125': '短杖', '126': '长杖', '127': '双手剑', '128': '双手斧',
    '129': '双手钝器', '130': '单手剑(旧段)', '131': '单手斧(旧段)',
    '132': '单手钝器(旧段)', '133': '短剑(旧段)', '134': '短刀',
    '135': '指虎', '136': '短刀(旧段)', '137': '短杖(旧段)',
    '138': '长杖(旧段)', '139': '武士刀', '140': '双手剑(旧段)',
    '141': '双手斧(旧段)', '142': '双手钝器(旧段)', '143': '矛',
    '144': '枪', '145': '弓', '146': '弩', '147': '拳套',
    '148': '指虎(旧段)', '149': '短刀(旧段2)', '152': '双弩枪',
    '153': '手炮', '154': '武士刀(旧段)', '155': '扇子', '156': '扇子(旧段)',
    '157': '双手剑(旧段2)', '158': '手炮(旧段)', '159': '太刀', '160': '锁链',
    '169': '能量剑', '170': '魔法书', '172': '长枪',
}


def load_names(eqp_xml):
    """String/Eqp.img.xml -> {itemId: name}，只取 Weapon 段。"""
    names = {}
    if not eqp_xml or not os.path.exists(eqp_xml):
        return names
    txt = open(eqp_xml, encoding='utf-8').read()
    i = txt.index('<imgdir name="Weapon">')
    seg = txt[i:]
    for m in re.finditer(
            r'<imgdir name="(\d+)">\s*<string name="name" value="([^"]*)"', seg):
        names[int(m.group(1))] = m.group(2)
    return names


def info_dict(node):
    out = {}
    for c in node.children():
        v = getattr(c, 'value', None)
        if isinstance(v, (int, float, str)):
            out[str(c.name)] = v
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--weapon-dir',
                    default='/Users/lizixian/Documents/mxd/MapleStory/Data/Character/Weapon')
    ap.add_argument('--eqp-xml',
                    default='/Users/lizixian/Documents/mxd/TMS282.5/data/wz282/String.wz/Eqp.img.xml')
    ap.add_argument('--out', required=True)
    args = ap.parse_args()

    names = load_names(args.eqp_xml)
    print('names loaded: %d' % len(names), file=sys.stderr)

    rows = []
    errs = []
    files = sorted(f for f in os.listdir(args.weapon_dir)
                   if f.endswith('.img') and f[:-4].isdigit())
    for idx, fn in enumerate(files):
        item_id = int(fn.replace('.img', ''))
        path = os.path.join(args.weapon_dir, fn)
        try:
            img = WzImage.from_bytes(open(path, 'rb').read(),
                                     key=WzKey.for_region('BMS'),
                                     name=fn)
            img.parse()
        except Exception as e:  # noqa: BLE001
            errs.append('%s: %s' % (fn, e))
            continue
        info = next((c for c in img.root.children() if str(c.name) == 'info'), None)
        if info is None:
            errs.append('%s: no info' % fn)
            continue
        d = info_dict(info)
        rows.append({
            'itemId': item_id,
            'name': names.get(item_id, ''),
            'cat': CATEGORY.get(str(item_id)[:4], ''),
            **{k: d.get(k, '') for k in FIELDS},
        })
        if idx % 1000 == 0:
            print('  %d/%d' % (idx, len(files)), file=sys.stderr)

    cols = ['itemId', 'name', 'cat'] + FIELDS
    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    with open(args.out, 'w', encoding='utf-8') as f:
        f.write('\t'.join(cols) + '\n')
        for r in rows:
            f.write('\t'.join(str(r[c]) for c in cols) + '\n')
    print('rows=%d errors=%d -> %s' % (len(rows), len(errs), args.out))
    for e in errs[:10]:
        print('  ERR ' + e, file=sys.stderr)


if __name__ == '__main__':
    main()
