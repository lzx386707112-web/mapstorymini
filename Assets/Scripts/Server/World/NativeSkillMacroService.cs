// 技能宏服务 —— 源端 SkillMacroHandler.java + Character.java(skillMacros) 的直译。
// 源端语义：
//   1. 一次包最多 5 条宏（num > 5 直接丢弃整包）；
//   2. 宏名 ≤12 字符，超长视为改包（源端 AutobanFactory + 断线，这里整包拒绝）；
//   3. position = 包内序号 i（0..num-1）—— 客户端固定 5 个宏位全发，序号即槽位；
//   4. 服务端不校验 skill id（原端客户端只发已学技能，服务端照单全收）；
//   5. 持久化 = skillmacros 表，saveCharToDB 时 DELETE+INSERT（CharService.Save）。
// 存档镜像：客户端 GameSession.Character.macros 与 DB 同时更新，
//   否则后续 SaveCharacter 会用旧镜像把这里写好的行覆盖回去。
using System.Collections.Generic;

// 源端 SkillMacroHandler 读包序：num(1B) → 每条 { name(str), shout(1B), skill1..3(4B×3) }
public sealed class MacroRequest
{
    public int characterId;
    public List<MacroEntry> macros = new List<MacroEntry>();
}

public sealed class MacroResponse
{
    public bool ok;
    public string error;
}

public static class NativeSkillMacroService
{
    public const int MaxMacros = 5;
    public const int MaxNameLength = 12;

    public static MacroResponse Handle(DbStore db, MacroRequest r)
    {
        if (r == null || r.macros == null) return new MacroResponse { ok = false, error = "无效请求" };
        // 源端 SkillMacroHandler：num > 5 直接 return（不响应）
        if (r.macros.Count > MaxMacros) return new MacroResponse { ok = false, error = "宏数量超限" };
        for (int i = 0; i < r.macros.Count; i++)
        {
            var m = r.macros[i];
            if (m == null) return new MacroResponse { ok = false, error = "无效宏" };
            // 源端：名字超长 = 改包，断线；这里整包拒绝
            if (m.name != null && m.name.Length > MaxNameLength)
                return new MacroResponse { ok = false, error = "宏名过长" };
        }

        var cid = r.characterId;
        var table = db.Table("skillmacros");
        table.Delete(DbStore.Eq("characterid", cid));
        for (int i = 0; i < r.macros.Count; i++)
        {
            var m = r.macros[i];
            if (m.IsEmpty) continue; // 空宏不落库（与 CharService.Save 同口径）
            table.Insert(new DbRow
            {
                ["characterid"] = cid,
                ["position"] = i,
                ["skill1"] = m.skill1, ["skill2"] = m.skill2, ["skill3"] = m.skill3,
                ["name"] = m.name ?? "", ["shout"] = m.shout,
            });
        }
        db.Save();
        return new MacroResponse { ok = true };
    }
}
