#!/usr/bin/env python3
"""把 V 矩阵的 18 个特殊核心（type==2）整理成运行时可用的效果表。

数据来源只有两处，都在仓库里：
  Assets/Resources/UI/VMatrix.json     ← 282 Etc/VCore.img 的 CoreData 转储
                                         （special.cond / special.effect 是源端结构化字段）
  String/Skill.img/400007xxx/h         ← 已在 VMatrix.json 的 desc 里展开成中文

VMatrix.json 的 special.effect 只给了「施加哪个 buff 技能」（400007xxx），
而 400007xxx 在客户端 **没有 Skill 定义**（40000.img 里只有 400000xxx-400004xxx，
服务端 TMS282.5 也完全没有 SpecialCore 实现），所以弹 buff 的数值只能从 desc 取。
这个脚本把 desc 解析成 {stat, value, duration}，解析不出来的直接抛错而不是猜。

用法：python3 tools/ms282/export_special_cores.py
输出：Assets/Resources/UI/SpecialCores.json
"""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]   # MapleDash/（脚本在 MapleDash/tools/ms282/）
VMATRIX = ROOT / 'Assets/Resources/UI/VMatrix.json'
OUTPUT = ROOT / 'Assets/Resources/UI/SpecialCores.json'

# StatEffect 的 stat 键与 CombatRules / AdminSetItem 认的键一致：
#   Damage / BossDamage / IgnoreDefense 进 setItemBonus，Critical 进 criticalChance，
#   Exp 走 AdminSetItem.ApplyRate("Exp")，Invincible 走受击否决。
STAT_RULES = (
    ('invincible', ('无敌', '無敵')),
    ('bossDamage', ('Boss', 'BOSS')),
    ('critical', ('爆击', '爆擊', '暴击')),
    ('ignoreDefense', ('无视防御', '無視防禦')),
    ('exp', ('经验值', '經驗值')),
    ('damage', ('伤害', '傷害')),
)

DURATION = re.compile(r'(\d+)\s*秒\s*[内內间間]')
# 数值优先取「增加 N%」：魔王杀手I 的 desc 前面还有「0.1% 几率」，直接抓第一个百分比会抓成 0。
PERCENT = re.compile(r'增加\s*(\d+(?:\.\d+)?)\s*%')
PERCENT_ANY = re.compile(r'(\d+(?:\.\d+)?)\s*%')


def classify(text):
    for stat, words in STAT_RULES:
        if any(word in text for word in words):
            return stat
    raise SystemExit(f'无法判定效果属性：{text}')


def parse_buff(desc):
    """selfbuff 的核心：desc 里必须能同时拿到持续时长与数值。"""
    duration = DURATION.search(desc)
    if duration is None:
        raise SystemExit(f'desc 里找不到「N秒内」：{desc}')
    stat = classify(desc)
    if stat == 'invincible':
        # 无敌没有百分比，源端就是「N 秒内无敌」。
        return {'stat': stat, 'value': 100, 'duration': int(duration.group(1)) * 1000}
    percent = PERCENT.search(desc) or PERCENT_ANY.search(desc)
    if percent is None:
        raise SystemExit(f'desc 里找不到百分比：{desc}')
    return {
        'stat': stat,
        'value': int(round(float(percent.group(1)))),
        'duration': int(duration.group(1)) * 1000,
    }


def main():
    if not VMATRIX.is_file():
        raise SystemExit(f'缺少 {VMATRIX}')
    data = json.loads(VMATRIX.read_text(encoding='utf-8'))
    cores = {}
    for key, core in data['cores'].items():
        if core.get('type') != 2:
            continue
        special = core.get('special') or {}
        condition = special.get('cond') or {}
        effect = special.get('effect') or {}
        kind = effect.get('type', '')
        entry = {
            'id': core['id'],
            'name': core['name'],
            'desc': core['desc'],
            'cond': condition,
            'effectType': kind,
        }
        if kind == 'selfbuff':
            entry['effect'] = parse_buff(core['desc'])
            entry['skill'] = effect.get('skill_id', 0)
        elif kind == 'reduceCooltime':
            entry['effect'] = {'stat': 'cooltime', 'value': int(effect.get('reducePercent', 0))}
            entry['skill'] = effect.get('skill_id', 0)
        elif kind == 'heal':
            entry['effect'] = {'stat': 'heal', 'value': int(effect.get('heal_percent', 0))}
            entry['skill'] = effect.get('skill_id', 0)
        else:
            raise SystemExit(f'{core["name"]} 的 effect.type 未知：{kind}')
        cores[str(core['id'])] = entry

    payload = {
        'source': 'VMatrix.json cores[type=2] 的 special.cond/effect + desc 文案数值'
                  '（400007xxx 在客户端没有 Skill 定义，数值只能来自 desc）',
        'cores': dict(sorted(cores.items())),
    }
    OUTPUT.write_text(json.dumps(payload, ensure_ascii=False, indent=1) + '\n', encoding='utf-8')
    print(f'特殊核心 {len(cores)} 条 -> {OUTPUT.relative_to(ROOT)}')
    for key, entry in payload['cores'].items():
        eff = entry['effect']
        print(f"  {key} {entry['name']:<12} {entry['cond']['type']:<15}"
              f" {eff['stat']}={eff['value']} {eff.get('duration', 0)}ms")


if __name__ == '__main__':
    main()
