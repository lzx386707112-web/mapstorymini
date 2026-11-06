using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 282 技能的「自动追加攻击」（`addAttack{skill, isAuto=1}`）。
//
// 有的技能放一次会再补一段攻击，那一段是**独立技能节点**（`invisible=1`，
// 不在技能窗里），由主技能指过来：
//
//     14141000 五连投掷VI  common{bulletCount=4, damage=276+4*x}
//       addAttack{ skill=14141001, isAuto=1 }
//     14141001 五连投掷VI  invisible=1  common{damage=1104+16*x, ball/...}
//
// 主技能自己只有 4 发；「五连」的第五发、以及 1104% 那段伤害和它的弹道特效
// 全在隐藏技能里。项目此前完全没读 `addAttack`（全代码搜不到），
// 所以这些技能少一整段 —— 表现是「打完就没了」+ 少一大截输出。
//
// 边界（不猜）：
//   · 只接 `isAuto=1`（语义确定：主技能结算后自动补一段）。另一种写法
//     `reqCount/pattern/skipActionFrame/skillPlus` 是「第 N 段 / 指定前置技能」的
//     取消链，语义未逆向，导出器把它们记进 `deferred`，这里不触发。
//   · 追加段**不再付费**：MP / 冷却 / 弹药都由主技能付过（源端这一段没有 mpCon）。
//     用 `BeginCast(def, continuousShot: true)` 走同一条施法管线但跳过付费校验。
//   · 追加段自身也可以有 `addAttack`（400021020 → 021 → 022 是三段链），
//     因为它同样走 `Cast`，链条会自然接下去。
public partial class SkillSystem
{
    sealed class AddAttackLink
    {
        public int Next;
        public float Delay;
    }

    static Dictionary<int, AddAttackLink> addAttacks;
    static bool addAttacksLoaded;

    static Dictionary<int, AddAttackLink> AddAttacks
    {
        get
        {
            if (addAttacksLoaded) return addAttacks;
            addAttacksLoaded = true;
            addAttacks = new Dictionary<int, AddAttackLink>();
            var asset = Resources.Load<TextAsset>("NativeSkillAddAttacks");
            if (asset == null)
            {
                Debug.LogWarning("[AddAttack] 缺 Assets/Resources/NativeSkillAddAttacks.json，追加攻击不会补");
                return addAttacks;
            }
            if (!(MiniJson.Parse(asset.text) is Dictionary<string, object> root) ||
                !root.TryGetValue("skills", out var raw) ||
                !(raw is Dictionary<string, object> skills))
                return addAttacks;
            foreach (var pair in skills)
            {
                // 技能 id 超过 2^28 时 float 会把尾数抹掉（IRON-RULES §7.1），一律走 long。
                if (!long.TryParse(pair.Key, out long id) || id <= 0 || id > int.MaxValue) continue;
                if (!(pair.Value is Dictionary<string, object> rec)) continue;
                if (!rec.TryGetValue("next", out var rawNext)) continue;
                int next = Convert.ToInt32(rawNext);
                if (next <= 0 || next == id) continue;
                addAttacks[(int)id] = new AddAttackLink
                {
                    Next = next,
                    Delay = Art.Num(rec, "delay") / 1000f,
                };
            }
            if (PlayerPrefs.GetInt("debug_hitlog", 0) != 0)
                Debug.Log("[AddAttack] 载入 " + addAttacks.Count + " 条自动追加链");
            return addAttacks;
        }
    }

    /// <summary>主技能结算完之后调；没有追加链就什么也不做。</summary>
    void PlayAddAttack(SkillDef cast, float extraDelay = 0f)
    {
        if (cast?.native == null) return;
        if (!AddAttacks.TryGetValue(cast.native.Id, out var link)) return;
        StartCoroutine(AddAttackAfter(cast, link, extraDelay));
    }

    // 追加段至少晚 0.3 秒：主技能自己的多段判定/起手动作要先把伤害打完。
    // 源端 `addAttack/delay` 全为 0，但那是「第 N 段结束」的相对时刻，不是同帧。
    const float DefaultFollowupDelay = .3f;

    IEnumerator AddAttackAfter(SkillDef parent, AddAttackLink link, float extraDelay, int depth = 0)
    {
        bool debug = PlayerPrefs.GetInt("debug_hitlog", 0) != 0;
        float delay = Mathf.Max(DefaultFollowupDelay, link.Delay) + Mathf.Max(0f, extraDelay);
        if (delay > 0f) yield return new WaitForSeconds(delay);
        // 换图 / 死亡 / 已经按住别的技能：不补那一段，否则会打在下一个场景里。
        if (p == null || p.hp <= 0 || GameLoop.I == null ||
            GameLoop.I.state != GameLoop.GameState.Playing || heldSkill != null)
        {
            if (debug) Debug.Log("[AddAttackDbg] stop: 玩家/场景状态 parent=" + parent.native.Id +
                                 " held=" + (heldSkill != null));
            yield break;
        }
        int level = SkillProgress.LevelOf(link.Next);
        // 隐藏技能不在技能窗里，`LevelOf` 常常是 0：直接继承主技能等级（同一 job、同一转数）。
        if (level <= 0) level = Mathf.Max(1, parent.native.Level);
        var native = SkillBook.Native(link.Next, level);
        if (native == null)
        {
            if (debug) Debug.Log("[AddAttackDbg] stop: native=null next=" + link.Next + " lv=" + level);
            yield break;
        }
        var data = SkillBook.Get(link.Next);
        string art = link.Next.ToString();
        // 表现键沿用 `WzAssets.Index` 里登记过的 art（和 TryCastSkill 同一口径）。
        if (WzAssets.Index != null && WzAssets.Index["skills"] is Dictionary<string, object> indexMap)
            foreach (var pair in indexMap)
                if (Convert.ToInt32(pair.Value) == link.Next) { art = pair.Key; break; }
        var def = new SkillDef(new ClassSkill
        {
            name = data?.name ?? link.Next.ToString(),
            art = art,
            icon = data?.icon,
        }, native);
        if (PlayerPrefs.GetInt("debug_hitlog", 0) != 0)
            Debug.Log("[AddAttack] " + parent.native.Id + " → " + link.Next + " lv=" + level);
        if (!BeginCast(def, true)) yield break;
        // 追加段自身还能有 `addAttack`（400021020 → 021 → 022 是三段链）。
        // 但追加段走 `BeginCast(def, true)` 的连射捷径：SkillSystem.cs 307-312 在
        // `HitAt` 之后就 `return true`，**到不了 368 行的 PlayAddAttack 钩子** ——
        // 第一版就是这样只补一段、第三段永远不出（实测 `[AddAttack]` 只有 1 条）。
        // 所以链由这里显式接续；不能把钩子挪进 continuousShot 分支，那会让
        // 连射技能的「每一发」都补一段追加攻击。
        // depth 上限只是防御：源端 11 条链无环，但别让表里意外成环时无限递归。
        if (depth < 8 && AddAttacks.TryGetValue(link.Next, out var nextLink))
            StartCoroutine(AddAttackAfter(def, nextLink, 0f, depth + 1));
    }

    /// <summary>探针用：该技能有没有自动追加链。</summary>
    public static bool AddAttackOf(int skillId, out int next, out float delay)
    {
        next = 0; delay = 0f;
        if (!AddAttacks.TryGetValue(skillId, out var link)) return false;
        next = link.Next; delay = link.Delay;
        return true;
    }

    public static int AddAttackCount => AddAttacks.Count;
}
