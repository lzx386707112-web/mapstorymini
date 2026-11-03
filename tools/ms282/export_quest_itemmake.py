#!/usr/bin/env python3
"""扫描 282 客户端 QuestData(25033 个任务) 的 Act 奖励与 ItemMake(装备制作)。

  python3 export_quest_itemmake.py --out /tmp/quest-make.json

输出 JSON：
  quest_rewards: [{questId, questName, rewardItem, count, npcAct}]
  itemmake:      [{product, group, reqLevel, reqSkillLevel, meso, recipe:[{item,count}], tuc}]
只保留产物/奖励落在武器 ID 空间 1200000..1699999 的条目。
"""
import argparse
import json
import os
import sys

WZPY = '/Users/lizixian/Documents/mxd/Beidou-Server/tool/wz-python'
sys.path.insert(0, WZPY)

from wzpy import WzImage, WzKey  # noqa: E402

KEY = WzKey.for_region('BMS')
QUEST_DIR = '/Users/lizixian/Documents/mxd/MapleStory/Data/Quest/QuestData'
MAKE_PATH = '/Users/lizixian/Documents/mxd/MapleStory/Data/Etc/ItemMake.img'


def load(path, name):
    img = WzImage.from_bytes(open(path, 'rb').read(), key=KEY, name=name)
    img.parse()
    return img


def val(node):
    return getattr(node, 'value', None)


def children(node):
    return list(node.children())


def quest_name(root):
    try:
        qi = next(c for c in children(root) if str(c.name) == 'QuestInfo')
        for c in children(qi):
            if str(c.name) == 'name':
                return str(val(c))
    except StopIteration:
        pass
    return ''


def scan_quests():
    rewards = []
    files = sorted(f for f in os.listdir(QUEST_DIR) if f.endswith('.img'))
    for idx, fn in enumerate(files):
        try:
            img = load(os.path.join(QUEST_DIR, fn), fn)
        except Exception:  # noqa: BLE001
            continue
        act = next((c for c in children(img.root) if str(c.name) == 'Act'), None)
        if act is None:
            continue
        qid = int(fn.replace('.img', ''))
        name = quest_name(img.root)
        for step in children(act):
            items = next((c for c in children(step) if str(c.name) == 'item'), None)
            if items is None:
                continue
            for slot in children(items):  # slot = 序号节点
                d = {str(c.name): val(c) for c in children(slot)}
                iid = d.get('id') if isinstance(d.get('id'), int) else val(slot)
                if isinstance(iid, int) and 1200000 <= iid < 1700000:
                    rewards.append({
                        'questId': qid, 'questName': name,
                        'itemId': iid, 'count': d.get('count', 1),
                    })
        if idx % 5000 == 0:
            print('  quest %d/%d' % (idx, len(files)), file=sys.stderr)
    return rewards


def scan_make():
    out = []
    img = load(MAKE_PATH, 'ItemMake.img')
    for group in children(img.root):
        for prod in children(group):
            pid = int(str(prod.name))  # 节点名是十进制 itemId（补前导 0）
            if not (1200000 <= pid < 1700000):
                continue
            d = {str(c.name): val(c) for c in children(prod)}
            recipe = []
            rec = next((c for c in children(prod) if str(c.name) == 'recipe'), None)
            if rec is not None:
                for r in children(rec):
                    rd = {str(c.name): val(c) for c in children(r)}
                    recipe.append({'item': rd.get('item'), 'count': rd.get('count')})
            out.append({
                'product': pid, 'group': str(group.name),
                'reqLevel': d.get('reqLevel'), 'reqSkillLevel': d.get('reqSkillLevel'),
                'meso': d.get('meso'), 'tuc': d.get('tuc'), 'recipe': recipe,
            })
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', required=True)
    args = ap.parse_args()
    rewards = scan_quests()
    makes = scan_make()
    data = {'quest_rewards': rewards, 'itemmake': makes}
    with open(args.out, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False)
    print('quest_rewards=%d itemmake=%d -> %s' % (len(rewards), len(makes), args.out))


if __name__ == '__main__':
    main()
