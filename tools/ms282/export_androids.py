#!/usr/bin/env python3
"""Export TMS 282 android (機器人) gameplay data.

Sources:
  - Character/Android/01662xxx.img  : android equips (info.android = template id, info.grade = android grade)
  - Character/Android/01672xxx.img  : mechanical hearts (stats, info.grade = heart grade)
  - Etc/Android/NNNN.img            : appearance templates (costume pools, basic outfit, action chats)
  - String.wz/Eqp.img.xml           : item names

Outputs TSVs: android-items.tsv, android-hearts.tsv, android-templates.tsv
"""
import argparse
import os
import re
import sys

sys.path.insert(0, '/Users/lizixian/Documents/mxd/Beidou-Server/tool/wz-python')
from wzpy import WzImage, WzKey  # noqa: E402

KEY = WzKey.for_region('BMS')
CLIENT = os.path.expanduser('~/Documents/mxd/MapleStory/Data')
STRING_EQP = os.path.expanduser(
    '~/Documents/mxd/TMS282.5/data/wz282/String.wz/Eqp.img.xml')

STAT_FIELDS = ['incSTR', 'incDEX', 'incINT', 'incLUK', 'incPAD', 'incMAD',
               'incMHP', 'incMMP', 'imdR', 'incAllStat', 'incCrD']


def load_img(path, name):
    img = WzImage.from_bytes(open(path, 'rb').read(), key=KEY, name=name)
    img.parse()
    return img


def info_map(img):
    """Flatten Character/Android/<id>.img/info into a dict."""
    out = {}
    for sub in img.root.children():
        if str(sub.name) != 'info':
            continue
        for c in sub.children():
            v = getattr(c, 'value', None)
            if v is not None:
                out[str(c.name)] = v
    return out


def load_names():
    names = {}
    txt = open(STRING_EQP, encoding='utf-8').read()
    for m in re.finditer(r'<imgdir name="(\d+)">', txt):
        seg = txt[m.end():m.end() + 600]
        nm = re.search(r'<string name="name" value="([^"]*)"', seg)
        if nm:
            names[int(m.group(1))] = nm.group(1)
    return names


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--client', default=CLIENT)
    ap.add_argument('--out', required=True)
    args = ap.parse_args()

    names = load_names()
    android_dir = os.path.join(args.client, 'Character', 'Android')
    etc_dir = os.path.join(args.client, 'Etc', 'Android')

    items, hearts = [], []
    for fn in sorted(os.listdir(android_dir)):
        if not fn.endswith('.img') or fn.startswith('_'):
            continue
        iid = int(fn.replace('.img', ''))
        info = info_map(load_img(os.path.join(android_dir, fn), fn))
        base = {'itemId': iid, 'name': names.get(iid, ''),
                'reqLevel': info.get('reqLevel', ''),
                'grade': info.get('grade', ''), 'price': info.get('price', ''),
                'cash': info.get('cash', 0), 'tuc': info.get('tuc', ''),
                'tradeBlock': info.get('tradeBlock', 0),
                'equipTradeBlock': info.get('equipTradeBlock', 0),
                'notSale': info.get('notSale', 0),
                'charmEXP': info.get('charmEXP', '')}
        if 1662000 <= iid < 1669999:
            base['androidTemplate'] = info.get('android', '')
            items.append(base)
        elif 1672000 <= iid < 1672999:
            for f in STAT_FIELDS:
                base[f] = info.get(f, '')
            hearts.append(base)

    templates = []
    for fn in sorted(os.listdir(etc_dir)):
        if not fn.endswith('.img') or fn.startswith('_') or fn.endswith('.ini'):
            continue
        img = load_img(os.path.join(etc_dir, fn), fn)
        t = {'template': int(fn.replace('.img', ''))}
        for sub in img.root.children():
            nm = str(sub.name)
            if nm == 'info':
                for c in sub.children():
                    t['info_' + str(c.name)] = getattr(c, 'value', '')
            elif nm == 'basic':
                for c in sub.children():
                    t['basic_' + str(c.name)] = getattr(c, 'value', '')
            elif nm == 'costume':
                for c in sub.children():
                    t['pool_' + str(c.name)] = len(list(c.children()))
            elif nm == 'action':
                chats = []
                for ev in sub.children():
                    for i, var in enumerate(list(ev.children())):
                        for c in var.children():
                            if str(c.name) == 'chat':
                                chats.append(str(getattr(c, 'value', '')))
                t['actions'] = ','.join(sorted({str(e.name) for e in sub.children()}))
                t['chatLines'] = len(chats)
                t['chatSample'] = chats[0] if chats else ''
        templates.append(t)

    def dump(rows, header, path):
        with open(path, 'w', encoding='utf-8') as f:
            f.write('\t'.join(header) + '\n')
            for r in rows:
                f.write('\t'.join(str(r.get(k, '')) for k in header) + '\n')
        print(path, len(rows))

    dump(items, ['itemId', 'name', 'androidTemplate', 'reqLevel', 'grade',
                 'cash', 'price', 'tuc', 'tradeBlock', 'equipTradeBlock',
                 'notSale', 'charmEXP'],
         args.out + '/android-items.tsv')
    dump(hearts, ['itemId', 'name', 'reqLevel', 'grade', 'tuc', 'cash',
                  'price', 'tradeBlock', 'equipTradeBlock', 'notSale']
         + STAT_FIELDS,
         args.out + '/android-hearts.tsv')
    dump(templates, ['template', 'info_gender', 'info_chatBalloon',
                     'info_nameTag', 'basic_accessory', 'basic_longcoat',
                     'basic_shoes', 'pool_skin', 'pool_hair', 'pool_face',
                     'actions', 'chatLines', 'chatSample'],
         args.out + '/android-templates.tsv')


if __name__ == '__main__':
    main()
