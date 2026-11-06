using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 282「按住结束 → 收尾子技能」：商业端 Skill/<img>.img/skill/<id>/keydownFinish。
//
// 全库只有 4 条（400011068 槍刺旋風 / 400041059 暗影投擲 / 400051046 小萌新吉祥物 /
// 400051078 破碎連拳），子技能一律 invisible=1，自己的 common 里带完整
// damage / attackCount / mobCount / lt / rb，String/Skill.img 里只有名字没有 h ——
// 也就是「收尾攻击的数值与表现全在子技能」，母技能只给「几次、间隔多少」。
// 上一轮把 400011069 记成「psdSkill 仍缺」，其实是这条线没接。
//
// 波数/间隔由 tools/ms282/export_skill_finishers.py 从母技能 common + h 导出到
// Assets/Resources/NativeSkillFinishers.json；子技能数值运行时走 SkillBook.Native()
// 的 IMG 兜底展开，本文件不复制任何伤害常量。
public partial class SkillSystem
{
    static Dictionary<int, Dictionary<string, object>> finisherTable;

    static Dictionary<string, object> FinisherSpec(int parentId)
    {
        if (finisherTable == null)
        {
            finisherTable = new Dictionary<int, Dictionary<string, object>>();
            var text = Resources.Load<TextAsset>("NativeSkillFinishers")?.text;
            if (!string.IsNullOrEmpty(text))
            {
                var root = (Dictionary<string, object>)MiniJson.Parse(text);
                if (root.TryGetValue("finishers", out var raw) && raw is List<object> rows)
                    foreach (var item in rows)
                    {
                        if (!(item is Dictionary<string, object> row)) continue;
                        int parent = Art.Id(row, "parent");
                        if (parent > 0) finisherTable[parent] = row;
                    }
            }
        }
        return finisherTable.TryGetValue(parentId, out var spec) ? spec : null;
    }

    // 由 SkillHeldInput.StopHeld(true) 调用：按键松开，母技能的连射结束，
    // 这时候才生成收尾冲击波；按下期间绝不触发。
    public void StartFinisher(SkillDef parent)
    {
        if (parent == null || parent.native == null || p == null) return;
        var spec = FinisherSpec(parent.native.Id);
        if (spec == null) return;
        int childId = Art.Id(spec, "child");
        if (childId <= 0) return;
        int level = parent.native.Level;
        var definition = SkillBook.Native(childId, level);
        if (definition == null) return;
        int waves = Mathf.Max(1, Art.Int(spec, "waves", 1));
        float interval = Mathf.Max(.04f, Art.Int(spec, "intervalMs", 120) / 1000f);
        // visualKeyOverride：伤害/判定框用子技能定义，取层也用子技能的 key，
        // 否则 Impact 会拿母技能的 hit/0，收尾那一下就只有伤害没有爆点。
        string key = NativeSkillVfx.Key(childId, level);
        var body = new SkillDef(new ClassSkill { name = parent.name, art = "", icon = "" }, definition, key);
        StartCoroutine(FinisherWaves(body, waves, interval));
    }

    IEnumerator FinisherWaves(SkillDef body, int waves, float interval)
    {
        var field = FindObjectOfType<FieldDirector>();
        var map = field != null ? field.Map : null;
        for (int i = 0; i < waves; i++)
        {
            if (!ReplayAlive(field, map)) yield break;
            Vector2 origin = p.FeetPos;
            int facing = p.facing;
            BeginVisuals(body, true);
            HitAt(body, body.native, origin, facing);
            if (PlayerPrefs.GetInt("debug_hitlog", 0) != 0)
                Debug.Log("[Finisher] wave=" + (i + 1) + "/" + waves + " child=" + body.native.Id +
                          " vfx=" + body.vfx);
            if (i + 1 < waves) yield return new WaitForSeconds(interval);
        }
    }
}
