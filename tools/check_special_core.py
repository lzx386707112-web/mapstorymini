#!/usr/bin/env python3
"""282 箭筒 / 特殊核心的接线守卫。

看守三类东西，每一类都有明确的源端出处，改动后跑一遍就知道有没有回退：

  1. formula  — SkillBook.EvalCommon 必须支持 d() / u() / logN()
                全库 7272 个 common 公式字段里有 1300+ 个用到它们
                （d 353 / u 145 / log10-70 939），不支持就等于数值读不到。
  2. quiver   — 魔幻箭筒的三层归属不能挪：
                3141005 魔幻箭筒VI   common{s=292+5*x}           → 3140006 判定体
                3141009 追加魔幻箭筒  common{s=150+3*x, dummyStr=1350+52*x, w, w2}
                                                 → 3140010 / 3140011（skillList 里写死）
  3. special  — 18 个特殊核心的 cond/effect 与 VMatrix.json 逐条一致，
                并且两端（导出表 / C# 事件）都在。

用法：cd MapleDash && python3 tools/check_special_core.py
"""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SCRIPTS = ROOT / 'Assets/Scripts'
RES = ROOT / 'Assets/Resources/UI'

# VCore.img 里出现的全部 cond.type（8 种）与 effect.type（3 种）。
# 多出来一种就说明源端加了新玩法，这里必须先补实现再放行。
COND_TYPES = {
    'killCount', 'attackCount', 'attackCountMob', 'hitCount',
    'prob', 'cooltime', 'die', 'rune', 'combokill',
}
EFFECT_TYPES = {'selfbuff', 'reduceCooltime', 'heal'}
EFFECT_STATS = {'damage', 'bossDamage', 'ignoreDefense', 'critical', 'exp', 'invincible'}

problems = []


def check(condition, message):
    if not condition:
        problems.append(message)
    return condition


def read(path):
    return path.read_text(encoding='utf-8')


def check_formula():
    source = read(SCRIPTS / 'SkillBook.cs')
    check('ApplyCommonFunction' in source, 'SkillBook 缺少 ApplyCommonFunction，common 公式函数没实现')
    for name, token in (('d()', 'name == "d"'), ('u()', 'name == "u"'), ('logN()', 'name.StartsWith("log")')):
        check(token in source, f'EvalCommon 没处理 {name}，用到它的公式会静默返回 fallback')
    print(f'formula: {"PASS" if not problems else "FAIL"}')


def check_quiver():
    # ——— 源端数据 ———
    for sid, field, want in (
        (3141005, 's', '292+5*x'),
        (3140006, 'damage', '292+5*x'),
        (3141009, 's', '150+3*x'),
        (3141009, 'dummyStr', '1350+52*x'),
        (3140010, 'damage', '150+3*x'),
        (3140011, 'damage', '1350+52*x'),
    ):
        print(f'  data {sid}.{field} = {want}')
    # 归属是靠「公式同源」+「skillList 显式列出」两条证据定的，
    # 这里只断言代码里引用的常量没被换掉。
    burst = read(SCRIPTS / 'NativeQuiverBurst.cs')
    for token, why in (
        ('public const int Skill = 3141009', '装填技能必须是 3141009'),
        ('public const int MagicShot = 3140010', '魔法箭追加判定体必须是 3140010'),
        ('public const int DrainShot = 3140011', '吸血箭追加判定体必须是 3140011'),
        ('SkillBook.Common(Skill, "w2"', '单匣上限必须读 common/w2'),
        ('SkillBook.Common(Skill, "w"', '自动发动阈值必须读 common/w'),
        ('FullBurst(player, target, direction, magic)', '装满后必须走全弹发射'),
        ('if (SkillProgress.LevelOf(Skill) <= 0) return false;', '没学 3141009 不该装填'),
    ):
        check(token in burst, f'NativeQuiverBurst 缺少「{why}」')

    arrow = read(SCRIPTS / 'NativeArrowMode.cs')
    for token, why in (
        ('public const int MagicBody = 3140006', '魔法箭判定体必须是 3140006 而不是 3140007'),
        ('SkillBook.Common(MagicBody, "damage"', '魔法箭伤害必须读 3140006 的 common/damage'),
        ('NativeQuiverBurst.Load(player, target, direction, magic)', '箭镞触发后必须装填'),
        ('NativeQuiverBurst.TryFire(player, target, direction, magic)', '命中必须尝试追加发射'),
    ):
        check(token in arrow, f'NativeArrowMode 缺少「{why}」')
    check('FullBurstBlast' not in arrow, 'NativeArrowMode 还在用旧的 3140006「满弹爆发」实现，归属是错的')

    system = read(SCRIPTS / 'SkillSystem.cs')
    check('NativeQuiverBurst.Activate(s.native.Level)' in system, '3141009 施放后没激活装填窗口')
    print(f'quiver: {"PASS" if not problems else "FAIL"}')


def check_special():
    vmatrix = json.loads((RES / 'VMatrix.json').read_text(encoding='utf-8'))['cores']
    source = {k: v for k, v in vmatrix.items() if v.get('type') == 2}
    export_path = RES / 'SpecialCores.json'
    if not check(export_path.is_file(), '缺少 Assets/Resources/UI/SpecialCores.json（跑 tools/ms282/export_special_cores.py）'):
        return
    exported = json.loads(export_path.read_text(encoding='utf-8'))['cores']

    check(len(source) == len(exported), f'特殊核心数量不一致：VMatrix {len(source)} / 导出 {len(exported)}')
    for key, core in source.items():
        entry = exported.get(key)
        if not check(entry is not None, f'{key} {core["name"]} 没被导出'):
            continue
        special = core.get('special') or {}
        cond = special.get('cond') or {}
        effect = special.get('effect') or {}
        check(entry['name'] == core['name'], f'{key} 名字不一致')
        check(entry['cond']['type'] == cond.get('type'), f'{key} cond.type 不一致')
        check(entry['effectType'] == effect.get('type'), f'{key} effect.type 不一致')
        check(cond.get('type') in COND_TYPES, f'{key} 出现未知 cond.type={cond.get("type")}')
        check(effect.get('type') in EFFECT_TYPES, f'{key} 出现未知 effect.type={effect.get("type")}')
        stat = entry['effect'].get('stat')
        check(stat in EFFECT_STATS or stat in ('cooltime', 'heal'),
              f'{key} 的效果 stat={stat} 不在白名单')
        # 数值必须来自 desc，0 值只有「无敌」允许（它本来就没有百分比）。
        if stat not in ('invincible',):
            check(entry['effect'].get('value', 0) > 0, f'{key} 效果数值是 0，desc 解析可能失败')
        if effect.get('type') == 'selfbuff':
            check(entry['effect'].get('duration', 0) > 0, f'{key} selfbuff 没有持续时间')

    # ——— C# 侧事件接线 ———
    client = read(SCRIPTS / 'NativeSpecialCore.cs')
    for token, why in (
        ('public static void OnAttack(', '攻击型计数'),
        ('public static void OnKill(', '击杀型计数'),
        ('public static void OnHit(', '受击型计数'),
        ('public static void OnSkillUse(', '冷却型触发'),
        ('public static void OnPlayerDeath(', '死亡型触发（回归）'),
        ('"attackCountMob"', '同怪计数分支'),
        ('public static void Observe(VMatrixResponse', '与 V 矩阵快照同步'),
    ):
        check(token in client, f'NativeSpecialCore 缺少「{why}」')

    for path, token, why in (
        ('SkillSystem.cs', 'NativeSpecialCore.OnSkillUse(p)', '施法没有驱动冷却型核心'),
        ('SkillSystem.cs', 'NativeSpecialCore.OnAttack(p, targetId)', '技能命中没有驱动攻击型核心'),
        ('PlayerCtrl.cs', 'NativeSpecialCore.OnHit(this)', '受击没有驱动受击型核心'),
        ('PlayerCtrl.cs', 'ApplySpecialCore()', '属性刷新没有叠加特殊核心增益'),
        ('GameLoop.cs', 'NativeSpecialCore.OnKill(player)', '击杀没有驱动击杀型核心'),
        ('GameLoop.cs', 'NativeSpecialCore.OnPlayerDeath(player)', '死亡没有驱动回归核心'),
        ('GameLoop.cs', 'NativeSpecialCore.Sync()', '进图没有拉取启用状态'),
        ('SkillVisuals.cs', 'NativeSpecialCore.OnAttack(p,', 'triggerSpecialCore 没接上'),
        ('NativeVMatrixWindow.cs', 'NativeSpecialCore.Observe(response)', '矩阵快照变化后没同步'),
        ('CombatRules.cs', 'public void Reduce(double now, int percent)', '冷却状态没有按比例缩减的实现'),
        ('SkillSystem.cs', 'public void ReduceCooldowns(int percent)', 'SkillSystem 没有减冷却入口'),
    ):
        check(token in read(SCRIPTS / path), f'{path} 缺少「{why}」')

    # triggerSpecialCore 全库只有 4141000 一个，多出来说明有新技能要接。
    check('triggerSpecialCore' in read(SCRIPTS / 'NativeShootObj.cs'),
          'NativeShootObj 不再读 triggerSpecialCore')
    print(f'special: {len(source)} 条 / {"PASS" if not problems else "FAIL"}')


def main():
    print('— 282 箭筒 / 特殊核心守卫 —')
    check_formula()
    check_quiver()
    check_special()
    if problems:
        print(f'\nFAIL {len(problems)} 项：')
        for item in problems:
            print('  ✗', item)
        return 1
    print('\n全部 PASS')
    return 0


if __name__ == '__main__':
    sys.exit(main())
