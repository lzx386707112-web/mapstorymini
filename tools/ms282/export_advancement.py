#!/usr/bin/env python3
"""Export TMS 282 fifth/sixth job advancement chains, V matrix and HEXA matrix data.

Outputs a single JSON used by docs/tms282-skill/07-job-advancement.md.

Sources (client side, authoritative for structure):
  Data/Quest/QuestData/<id>.img          quest chain, prerequisites, NPC, level gate
  Data/Etc/VCore.img                     V matrix: CoreData / coreGroup / coreGemstone / vPoint / info
  Data/Etc/VMatrixOption.img             V matrix UI limits + craft costs
  Data/Etc/HexaCore.img                  HEXA: hexaSkill/{coreData,jobCore}, hexaStat, solErda
  Data/String/Npc.img                    NPC display names

Server side (`~/Documents/mxd/TMS282.5`) is only used for the Sol Erda cost tables that are
NOT stored in the client WZ (they are sent to the client by the server packet).

Usage:
  python3 tools/ms282/export_advancement.py \
      --out ../docs/tms282-skill/data/advancement.json
"""

import argparse
import json
import os
import sys

WZPY = os.path.expanduser('~/Documents/mxd/BeiDou-Server/tool/wz-python')
if WZPY not in sys.path:
    sys.path.insert(0, WZPY)

from wzpy import WzImage, WzKey  # noqa: E402

DATA = os.path.expanduser('~/Documents/mxd/MapleStory/Data')

# Fifth job chain (quest id -> role). Verified by `Check/0/quest` prerequisites.
FIFTH_CHAIN = [
    ('1460', '领取五转任务（艾爾達斯的呼喚）'),
    ('1461', '女神祝福 / 觉醒前置'),
    ('1465', '紀錄力量，完成覺醒（jobLevel -> 5）'),
    ('1478', 'V矩陣（解锁矩阵）'),
]
FIFTH_SIDE = ['1479', '1481', '1483', '1487']

# Sixth job chain, in prerequisite order.
SIXTH_CHAIN = [
    ('38601', '統合的次元'),
    ('38602', '未曾存在的艾爾達'),
    ('38603', '艾爾達中的女神(1)'),
    ('38604', '微弱的聲音'),
    ('38605', '艾爾達中的女神(2)'),
    ('38606', '聲音的真面目'),
    ('38607', '艾爾達中的女神(3)'),
    ('38608', '前往靈魂艾爾達之內'),
    ('38609', '全新的時代'),
    ('38612', '轉職完成'),
    ('38611', '繼續進行轉職'),
    ('38624', '新的力量'),
    ('38625', 'HEXA矩陣（解锁矩阵）'),
]
SIXTH_SIDE = ['38618', '38619', '38620', '38621', '38622', '38623']  # 六个区域符文回收

NPCB = None


def _load(path, name):
    img = WzImage.from_bytes(open(path, 'rb').read(), key=WzKey.for_region('BMS'), name=name)
    img.parse()
    return img


def get(node, name):
    if node is None:
        return None
    for c in node.children():
        if c.name == name:
            return c
    return None


def walk(node, path):
    cur = node
    for seg in path.split('/'):
        cur = get(cur, seg)
        if cur is None:
            return None
    return cur


def val(node, name, default=None):
    c = get(node, name)
    return default if c is None else getattr(c, 'value', default)


def leaves(node):
    """Flatten ``{a:{b:id}}`` int containers into a list of ints."""
    out = []

    def rec(n):
        for k in n.children():
            if hasattr(k, 'children') and k.children():
                rec(k)
            else:
                v = getattr(k, 'value', None)
                if v is not None:
                    out.append(v)

    if node is not None:
        rec(node)
    return out


def flat_map(node):
    """``{level: cost}`` containers (no nested grouping)."""
    out = {}
    if node is None:
        return out
    for k in node.children():
        v = getattr(k, 'value', None)
        if v is not None:
            out[k.name] = v
    return out


def npc_name(nid):
    global NPCB
    if NPCB is None:
        NPCB = _load(os.path.join(DATA, 'String', 'Npc.img'), 'Npc.img')
    e = get(NPCB.root, str(nid))
    return val(e, 'name', '') if e is not None else ''


def quest_info(qid):
    p = os.path.join(DATA, 'Quest', 'QuestData', '%s.img' % qid)
    if not os.path.exists(p):
        return None
    img = _load(p, '%s.img' % qid)
    qi = get(img.root, 'QuestInfo')
    chk = get(img.root, 'Check')
    step = chk.children()[0] if chk is not None and chk.children() else None
    npc = val(step, 'npc')
    rec = {
        'id': int(qid),
        'name': val(qi, 'name', ''),
        'area': val(qi, 'area'),
        'type': val(qi, 'type', ''),
        'autoStart': val(qi, 'autoStart'),
        'placeSummary': val(qi, 'placeSummary', ''),
        'demandSummary': val(qi, 'demandSummary', ''),
        'npc': npc,
        'npcName': npc_name(npc) if npc else '',
        'lvmin': val(step, 'lvmin'),
        'subJobFlags': val(step, 'subJobFlags'),
        'startscript': val(step, 'startscript', ''),
        'endscript': val(step, 'endscript', ''),
        'prereq': leaves(get(step, 'quest')),
        'starForce': val(step, 'totalStarForce'),
        'weaponStarForce': val(step, 'weaponStarForce'),
    }
    jobs = get(step, 'job')
    if jobs is not None:
        rec['jobCount'] = len(jobs.children())
    return rec


def export_v():
    img = _load(os.path.join(DATA, 'Etc', 'VCore.img'), 'VCore.img')
    opt = _load(os.path.join(DATA, 'Etc', 'VMatrixOption.img'), 'VMatrixOption.img')

    cores = {}
    for c in walk(img.root, 'vSkill/CoreData').children():
        cores[c.name] = {
            'type': val(c, 'type'),
            'maxLevel': val(c, 'maxLevel'),
            'name': val(c, 'name', ''),
            'desc': val(c, 'desc', ''),
            'job': leaves(get(c, 'job')),
            'connectSkill': leaves(get(c, 'connectSkill')),
            'subSkill': leaves(get(c, 'subSkill')),
            'checkCooltimeSkill': leaves(get(c, 'checkCooltimeSkill')),
            'checkRemoveSkill': leaves(get(c, 'checkRemoveSkill')),
            'notAbleSelfOpen': val(c, 'notAbleSelfOpen'),
            'notAbleCraft': val(c, 'notAbleCraft'),
            'nobAbleGemStone': val(c, 'nobAbleGemStone'),
            'noDisassemble': val(c, 'noDisassemble'),
            'expireAfter': val(c, 'expireAfter'),
        }
        sp = get(c, 'spCoreOption')
        if sp is not None:
            cond, eff = get(sp, 'cond'), get(sp, 'effect')
            cores[c.name]['spCoreOption'] = {
                'cond': {k.name: getattr(k, 'value', None) for k in cond.children()} if cond else {},
                'effect': {k.name: getattr(k, 'value', None) for k in eff.children()} if eff else {},
            }

    groups = {}
    for j in walk(img.root, 'vSkill/coreGroup').children():
        groups[j.name] = {k.name: leaves(k) for k in j.children()}

    gems = []
    for g in walk(img.root, 'coreGemstone').children():
        gems.append({
            'group': g.name,
            'itemIds': leaves(get(g, 'id')),
            'vPoint': val(g, 'vPoint'),
            'openCore': val(g, 'openCore', 0),
            'reqQuest': val(g, 'reqQuest', 0),
            'questAlert': val(g, 'questAlert', ''),
            'animationID': val(g, 'animationID', 0),
        })

    info = walk(img.root, 'info')
    optinfo = walk(opt.root, 'info')
    return {
        'info': {k.name: getattr(k, 'value', None) for k in info.children()},
        'vPoint': {
            'skill': flat_map(walk(img.root, 'vPoint/skill')),
            'enforce': flat_map(walk(img.root, 'vPoint/enforce')),
        },
        'option': {k.name: getattr(k, 'value', None) for k in optinfo.children()},
        'slotExpansionMeso': {
            lv.name: {k.name: getattr(k, 'value', None) for k in lv.children()}
            for lv in walk(opt.root, 'slotExpansionMeso').children()
        },
        'coreGemstone': gems,
        'coreGroup': groups,
        'coreData': cores,
    }


def export_hexa():
    img = _load(os.path.join(DATA, 'Etc', 'HexaCore.img'), 'HexaCore.img')
    cores = {}
    for c in walk(img.root, 'hexaSkill/coreData').children():
        rec = {
            'type': val(c, 'type'),
            'maxLevel': val(c, 'maxLevel'),
            'name': val(c, 'name', ''),
            'desc': val(c, 'desc', ''),
            'connectSkill': leaves(get(c, 'connectSkill')),
            'connectSkillForDesc': leaves(get(c, 'connectSkillForDesc')),
            'reqForActivation': flat_map(get(c, 'reqForActivation')),
        }
        cores[c.name] = rec

    groups = {}
    for j in walk(img.root, 'hexaSkill/jobCore').children():
        groups[j.name] = {k.name: leaves(k) for k in j.children()}

    tiers = []
    se = walk(img.root, 'solErda')
    for t in se.children():
        if not t.name.isdigit():
            continue
        tiers.append({
            'tier': int(t.name),
            'solErdaExp': val(t, 'solErdaExp'),
            'itemIds': leaves(get(t, 'id')),
        })

    stats = {}
    for c in walk(img.root, 'hexaStat/coreData').children():
        stats[c.name] = {
            'name': val(c, 'name', ''),
            'desc': val(c, 'desc', ''),
            'reqLevel': val(c, 'reqLevel'),
            'maxLevel': val(c, 'maxLevel'),
        }
    return {
        'solErda': {
            'gaugeMax': val(se, 'gaugeMax'),
            'slotMax': val(se, 'slotMax'),
            'tiers': sorted(tiers, key=lambda x: x['tier']),
        },
        'statCoreOrder': leaves(walk(img.root, 'hexaStat/statCore')),
        'statCoreData': stats,
        'jobCore': groups,
        'coreData': cores,
    }


# Sol Erda cost tables live on the server (sent to the client in the HEXA matrix packet),
# not in the client WZ. Copied from TMS282.5 HexaMatrixConstants.java.
SOL_ERDA_COSTS = {
    'activate': {'SKILL_CORE': 5, 'MASTERY_CORE': 3, 'BOOST_CORE': 4, 'COMMON_CORE': 7, 'HEXA_STAT': 5},
    'activateFragment': {'SKILL_CORE': 100, 'MASTERY_CORE': 50, 'BOOST_CORE': 70, 'COMMON_CORE': 150,
                         'HEXA_STAT': 10},
    'upgrade': {
        'SKILL_CORE': [1, 1, 1, 2, 2, 2, 3, 3, 10, 3, 3, 4, 4, 4, 4, 4, 4, 5, 15, 5, 5, 5, 5, 5, 6, 6, 6, 7, 20],
        'MASTERY_CORE': [1, 1, 1, 1, 1, 1, 2, 2, 5, 2, 2, 2, 2, 2, 2, 2, 2, 3, 8, 3, 3, 3, 3, 3, 3, 3, 3, 4, 10],
        'BOOST_CORE': [1, 1, 1, 2, 2, 2, 3, 3, 8, 3, 3, 3, 3, 3, 3, 3, 3, 4, 12, 4, 4, 4, 4, 4, 5, 5, 5, 6, 15],
        'COMMON_CORE': [2, 2, 2, 3, 3, 3, 5, 5, 14, 5, 5, 6, 6, 6, 6, 6, 6, 7, 17, 7, 7, 7, 7, 7, 9, 9, 9, 10, 20],
    },
    'upgradeFragment': {
        'SKILL_CORE': [30, 35, 40, 45, 50, 55, 60, 65, 200, 80, 90, 100, 110, 120, 130, 140, 150, 160, 350,
                       170, 180, 190, 200, 210, 220, 230, 240, 250, 500],
        'MASTERY_CORE': [15, 18, 20, 23, 25, 28, 30, 33, 100, 40, 45, 50, 55, 60, 65, 70, 75, 80, 175, 85, 90,
                         95, 100, 105, 110, 115, 120, 125, 250],
        'BOOST_CORE': [23, 27, 30, 34, 38, 42, 45, 49, 150, 60, 68, 75, 83, 90, 98, 105, 113, 120, 263, 128,
                       135, 143, 150, 158, 165, 173, 180, 188, 375],
        'COMMON_CORE': [38, 44, 50, 57, 63, 69, 75, 82, 300, 110, 124, 138, 152, 165, 179, 193, 207, 220, 525,
                        234, 248, 262, 275, 289, 303, 317, 330, 344, 750],
    },
}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', required=True)
    args = ap.parse_args()

    doc = {
        'verified': '2026-11-03',
        'fifth': {
            'chain': [quest_info(q) for q, _ in FIFTH_CHAIN],
            'side': [quest_info(q) for q in FIFTH_SIDE],
            'roles': {q: r for q, r in FIFTH_CHAIN},
        },
        'sixth': {
            'chain': [quest_info(q) for q, _ in SIXTH_CHAIN],
            'side': [quest_info(q) for q in SIXTH_SIDE],
            'roles': {q: r for q, r in SIXTH_CHAIN},
        },
        'vMatrix': export_v(),
        'hexaMatrix': export_hexa(),
        'solErdaCosts': SOL_ERDA_COSTS,
    }
    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    with open(args.out, 'w') as f:
        json.dump(doc, f, ensure_ascii=False, indent=1)
    v = doc['vMatrix']
    h = doc['hexaMatrix']
    print('quests 5th=%d 6th=%d' % (len(doc['fifth']['chain']), len(doc['sixth']['chain'])))
    print('V: cores=%d groups=%d gems=%d' % (len(v['coreData']), len(v['coreGroup']), len(v['coreGemstone'])))
    print('HEXA: cores=%d jobCore=%d tiers=%d' % (len(h['coreData']), len(h['jobCore']), len(h['solErda']['tiers'])))
    print('wrote', args.out, os.path.getsize(args.out), 'bytes')


if __name__ == '__main__':
    main()
