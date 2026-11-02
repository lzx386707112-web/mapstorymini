// 技能宏客户端镜像 —— 源端 MACRO_SYS_DATA_INIT / CUser::SetSkillMacro 的项目等价物。
// 数据权威在服务端（skillmacros 表 → GameSession.Character.macros），这里只是 5 槽的可写镜像：
//   · 进角色（切角）必须 Reset() 重读 —— 源端 Character.java:8739「Always send the macro packet
//     to fix a client side bug when switching characters」，同一口径；
//   · 确认（宏窗 OK）时全 5 槽整包上送（源端 SkillMacroHandler 的 position = 包内序号，
//     只有全发才能保住槽位号），空宏由服务端跳过不落库；
//   · SP 洗点把来源技能归 0 时清理宏内引用（源端 UseCashItemHandler:199-227）并重新上送。
// 快捷格绑定用负数标记（不与任何道具/技能 id 冲突）：-1000000 - 宏位。
using System.Collections.Generic;

public static class NativeSkillMacros
{
    public const int Slots = 5, Chain = 3;

    static readonly MacroEntry[] slots = new MacroEntry[Slots];
    static bool loaded;

    public static int Marker(int index) => -1000000 - index;
    public static int MarkerIndex(int marker) => -1000000 - marker;
    public static bool IsMacro(int id) => id <= Marker(Slots - 1) && id >= Marker(0);
    // 宏位上有没有可施放内容（绑快捷格 / 画图标都用）
    public static bool HasContent(int index)
    {
        EnsureLoaded();
        var m = slots[index];
        return m != null && !m.IsEmpty;
    }

    public static void Reset()
    {
        loaded = false;
        for (int i = 0; i < Slots; i++) slots[i] = null;
    }

    static void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true;
        var list = GameSession.Character?.macros;
        if (list == null) return;
        foreach (var m in list)
        {
            if (m == null || m.position < 0 || m.position >= Slots) continue;
            slots[m.position] = new MacroEntry
            {
                position = m.position, name = m.name ?? "", shout = m.shout,
                skill1 = m.skill1, skill2 = m.skill2, skill3 = m.skill3,
            };
        }
    }

    // —— UI 读 ——
    public static string Name(int index) { EnsureLoaded(); return slots[index]?.name ?? ""; }
    public static bool ShowName(int index) { EnsureLoaded(); return slots[index] != null && slots[index].shout != 0; }
    public static int Skill(int index, int chain)
    {
        EnsureLoaded();
        var m = slots[index];
        if (m == null) return 0;
        return chain == 0 ? m.skill1 : chain == 1 ? m.skill2 : m.skill3;
    }

    // —— UI 写（宏窗内，等 OK 才提交）——
    public static void SetName(int index, string name)
    {
        EnsureLoaded();
        if (slots[index] == null) slots[index] = new MacroEntry { position = index };
        slots[index].name = name ?? "";
    }
    public static void SetSkill(int index, int chain, int skillId)
    {
        EnsureLoaded();
        if (slots[index] == null) slots[index] = new MacroEntry { position = index };
        if (chain == 0) slots[index].skill1 = skillId;
        else if (chain == 1) slots[index].skill2 = skillId;
        else slots[index].skill3 = skillId;
    }
    public static void ToggleShow(int index)
    {
        EnsureLoaded();
        if (slots[index] == null) slots[index] = new MacroEntry { position = index };
        slots[index].shout = slots[index].shout == 0 ? 1 : 0;
    }

    // 宏位 → 快捷格图标：取链上第一个已学技能（原端画 Macroicon/<i>/icon）
    public static int FirstSkill(int index)
    {
        for (int j = 0; j < Chain; j++)
        {
            int id = Skill(index, j);
            if (id != 0 && SkillProgress.LevelOf(id) > 0) return id;
        }
        for (int j = 0; j < Chain; j++)
            if (Skill(index, j) != 0) return Skill(index, j);
        return 0;
    }

    // 确认上送：全 5 槽整包（保持槽位号），服务端跳过空宏
    public static string Commit()
    {
        EnsureLoaded();
        if (GameSession.Character == null) return "未进入游戏";
        var list = new List<MacroEntry>(Slots);
        for (int i = 0; i < Slots; i++)
        {
            var m = slots[i] ?? new MacroEntry { position = i };
            m.position = i;
            if (m.name != null && m.name.Length > 12) return "宏名过长";
            list.Add(m);
        }
        var res = GameSession.Net.Macro(new MacroRequest { characterId = GameSession.CharacterId, macros = list });
        if (!res.ok) return res.error;
        // 服务端接受了才更新镜像，避免 SaveCharacter 用旧快照回写
        GameSession.Character.macros = list;
        return null;
    }

    // SP 洗点：来源技能归 0 时清掉宏内引用并重新上送（源端 UseCashItemHandler:199-227）
    public static void ClearSkillRefs(int skillId)
    {
        EnsureLoaded();
        bool changed = false;
        for (int i = 0; i < Slots; i++)
        {
            var m = slots[i];
            if (m == null) continue;
            if (m.skill1 == skillId) { m.skill1 = 0; changed = true; }
            if (m.skill2 == skillId) { m.skill2 = 0; changed = true; }
            if (m.skill3 == skillId) { m.skill3 = 0; changed = true; }
        }
        if (changed) Commit();
    }

    // —— 施放：按链顺序逐个施放（源端客户端每段独立发技能包），shout 时头顶喊出宏名 ——
    public static void Cast(int index)
    {
        EnsureLoaded();
        if (!HasContent(index)) return;
        var m = slots[index];
        int cast = 0;
        for (int j = 0; j < Chain; j++)
        {
            int id = j == 0 ? m.skill1 : j == 1 ? m.skill2 : m.skill3;
            if (id == 0) continue;
            if (SkillProgress.LevelOf(id) <= 0) continue;
            if (SkillCast.TryCast(id, out _)) cast++;
        }
        if (m.shout != 0 && !string.IsNullOrEmpty(m.name) && GameLoop.I?.field != null)
            (GameLoop.I.field.GetComponent<BotField>() ?? GameLoop.I.field.gameObject.AddComponent<BotField>())
                .ShowLocalBalloon(m.name);
        if (cast == 0) BottomBar.Toast("宏「" + m.name + "」里的技能都还不能施放");
    }
}
