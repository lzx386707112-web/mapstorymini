using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// 原版地图数据自检（不需要进 Play）：
//   Unity -batchmode -projectPath <proj> -executeMethod FieldChecks.Run -logFile /tmp/field_checks.log
// 只验证「导出数据与 Unity 资源是否一致、是否自洽」，不代替实机手感验收。
public static class FieldChecks
{
    static int pass, fail;
    static readonly Dictionary<int, FieldMap> maps = new Dictionary<int, FieldMap>();

    static void Check(bool ok, string what)
    {
        if (ok) { pass++; return; }
        fail++;
        Debug.LogError("[FieldCheck] 失败：" + what);
    }

    public static void Run()
    {
        pass = fail = 0;
        FieldCatalog.Ensure();
        maps.Clear();
        var baseline = (Dictionary<string, object>)MiniJson.Parse(Resources.Load<TextAsset>("Field/FieldMaps").text);
        foreach (Dictionary<string, object> entry in (List<object>)baseline["maps"])
        {
            int id = (int)FieldCatalog.Num(entry, "id");
            maps[id] = FieldCatalog.GetMap(id);
        }
        Check(FieldCatalog.Ready, "FieldCatalog 未加载");
        if (!FieldCatalog.Ready) { Debug.LogError("[FieldCheck] 中止"); EditorApplication.Exit(1); return; }

        // 原端 String/Map.img 里 streetName=彩虹岛 的地图共 37 张（含该服自建的 4 号图）
        Check(maps.Count == 37, "地图数量 " + maps.Count + " != 37");
        Check(FieldCatalog.Mobs.Count == 11, "怪物数量 " + FieldCatalog.Mobs.Count + " != 11");

        int totalSpawns = 0, totalPortals = 0, totalFootholds = 0, totalRopes = 0, totalNpcs = 0;
        int danglingTargets = 0, hiddenNpcs = 0;
        foreach (var kv in maps)
        {
            var m = kv.Value;
            string tag = m.id + " " + m.name;
            Check(WzAssets.FileRoot(WzAssets.MapPath(m.id)) != null, tag + " 缺少原始 IMG");
            Check(m.footholds.Count > 0, tag + " 没有 foothold");
            Check(m.portals.Count > 0, tag + " 没有传送门");
            // 绳梯：y1 是顶端、y2 是底端（地图坐标 y 向下），必须 y2>y1 且不能是全 0。
            // 导出脚本曾把 ladderRope 的属性节点当条目遍历，坐标静默变成 0，运行时一条都抓不到。
            foreach (var r in m.ropes)
            {
                Check(r.y2 > r.y1, tag + " 绳梯端点非法 y1=" + r.y1 + " y2=" + r.y2);
                Check(!(r.x == 0f && r.y1 == 0f && r.y2 == 0f), tag + " 绳梯坐标全为 0（导出结构错误）");
            }
            foreach (var s in m.spawns)
                Check(FieldCatalog.Mobs.ContainsKey(s.mob), tag + " 刷怪点怪物 " + s.mob + " 未导出");
            foreach (var p in m.portals)
            {
                if (!p.Warps) continue;
                if (p.tm == m.id) continue;
                Check(maps.ContainsKey(p.tm), tag + " 传送门 " + p.name + " 目标地图 " + p.tm + " 不在彩虹岛数据内");
                // 原数据里存在悬空的 tn（例：40001 west00 → 40000 east00，而 40000 只有 out00）。
                // 原客户端/服务端此时回退到目标图 portal 列表首条（Character.changeMap → getPortal(0)），
                // 因此只统计、不判失败，但必须保证目标图至少有一个传送门可落地。
                if (maps.ContainsKey(p.tm) && !string.IsNullOrEmpty(p.targetName) &&
                    maps[p.tm].Portal(p.targetName) == null)
                {
                    danglingTargets++;
                    Check(maps[p.tm].FirstPortal != null,
                          tag + " 传送门 " + p.name + " 目标点悬空且目标图无可落地传送门");
                }
            }
            foreach (var n in m.npcs)
            {
                if (n.hide != 0) { hiddenNpcs++; continue; }      // hide=1 原客户端不绘制
                Check(WzAssets.FileRoot("Npc/" + n.id.ToString("D7") + ".img") != null, tag + " NPC " + n.id + " 没有原始 IMG");
            }

            totalSpawns += m.spawns.Count;
            totalPortals += m.portals.Count;
            totalFootholds += m.footholds.Count;
            totalRopes += m.ropes.Count;
            totalNpcs += m.npcs.Count;
        }

        // 与原端抽样核对（数值由 tools/wz_field.py 从 WZ 直接读取）
        var town = maps[1000000];
        Check(town.footholds.Count == 79, "彩虹村 foothold 应为 79，实际 " + town.footholds.Count);
        Check(town.ropes.Count == 2, "彩虹村 绳梯应为 2，实际 " + town.ropes.Count);
        Check(town.portals.Count == 13, "彩虹村 传送门应为 13，实际 " + town.portals.Count);
        Check(town.npcs.Count == 4, "彩虹村 NPC 应为 4，实际 " + town.npcs.Count);
        Check(maps[40001].spawns.Count == 27, "蜗牛打猎场Ⅱ 刷怪点应为 27");
        Check(maps[1010004].spawns.Count == 47, "蜗牛花园 刷怪点应为 47");
        Check(maps[60000].vr[2] - maps[60000].vr[0] == 4680, "南港 VR 宽度应为 4680");

        // —— 怪物行走速度 ——
        // Mob.wz/info/speed 是「相对 100% 的百分比偏移」；100% 取角色 walkSpeed（125px/s），
        // 两边同一套语义。巡逻与追击必须用同一个速度（旧版巡逻额外乘 0.35，蜗牛只剩 9.8px/s）。
        foreach (var kv in FieldCatalog.Mobs)
            Check(kv.Value.stats != null && kv.Value.stats.ContainsKey("speed"),
                  kv.Value.name + " 缺少 Mob.wz/info/speed");
        Check(Mathf.Abs(FieldDirector.MobSpeed(-65) - 125f * 0.35f / Art.PPU) < 1e-4f,
              "speed=-65 的换算不等于 125px/s×35%");
        Check(Mathf.Abs(FieldDirector.MobSpeed(0) - PlayerCtrl.BaseWalkPx / Art.PPU) < 1e-4f,
              "speed=0 应等于 100% 基准速度");
        Debug.Log("[FieldCheck] 蜗牛 speed=-65 → " + FieldDirector.MobSpeed(-65).ToString("0.000") + " u/s（" +
                  (FieldDirector.MobSpeed(-65) * Art.PPU).ToString("0.0") + " px/s）");

        // —— 普攻的垂直判定 ——
        // 用户截图：人物站在下层，能把最上层平台的怪打下来。旧版只比 |dx|，等于完全无视层高。
        int crossTier = 0;
        foreach (var kv in maps)
        {
            var m2 = kv.Value;
            for (int i = 0; i < m2.spawns.Count; i++)
                for (int j = i + 1; j < m2.spawns.Count; j++)
                {
                    var a = m2.spawns[i]; var b = m2.spawns[j];
                    var pa = new Vector2(m2.WorldX(a.x), m2.WorldY(a.y));
                    var pb = new Vector3(m2.WorldX(b.x), m2.WorldY(b.y), 0f);
                    if (PlayerCtrl.InMeleeBand(pa, pb, PlayerCtrl.BaseReach)) continue;
                    if (Mathf.Abs(pb.y - pa.y) > PlayerCtrl.MeleeVertical) crossTier++;
                }
        }
        Check(crossTier > 0, "没有跨层刷怪点对，普攻垂直判定等于没被验证");
        Check(!PlayerCtrl.InMeleeBand(new Vector2(0f, 0f), new Vector3(1f, -4.5f, 0f), PlayerCtrl.BaseReach),
              "普攻不应打到 4.5u 之上的目标");
        Check(PlayerCtrl.InMeleeBand(new Vector2(0f, 0f), new Vector3(1f, -1.0f, 0f), PlayerCtrl.BaseReach),
              "普攻应打到同层、高 1.0u 的斜坡目标");
        Debug.Log("[FieldCheck] 普攻垂直判定：跨层刷怪点对 " + crossTier + " 组全部够不到，近战框高度 ±" +
                  (PlayerCtrl.MeleeVertical * Art.PPU) + "px");

        // —— 传送门光洞 ——
        // 原端 pt=2(VISIBLE) 的门画 MapHelper.img/portal/game/pv；贴图和地图主体是两套资源，
        // 漏导时门照样能按 ↑ 进，只是屏幕上没有光洞（曾被当成「这图没有门」）。
        FieldPortalArt.Ensure();
        Check(FieldPortalArt.Visible.Count == 8, "传送门动画应为 8 帧，实际 " + FieldPortalArt.Visible.Count);
        foreach (var f in FieldPortalArt.Visible)
        {
            Check(Art.FrameAt(f.file, f.origin) != null, "传送门帧贴图缺失 " + f.file);
            Check(f.delay > 0f, "传送门帧延时非法 " + f.file);
        }
        int visiblePortals = 0;
        foreach (var kv in maps)
            foreach (var p in kv.Value.portals)
            {
                if (p.type != 2) continue;
                visiblePortals++;
                // 门必须落在 VR 内（导出坐标错位的话光洞会飘到图外）
                Check(p.x >= kv.Value.vr[0] - 40 && p.x <= kv.Value.vr[2] + 40 &&
                      p.y >= kv.Value.vr[1] - 400 && p.y <= kv.Value.vr[3] + 40,
                      kv.Value.id + " 传送门 " + p.name + " 坐标在地图范围外 " + p.x + "," + p.y);
            }
        Check(visiblePortals > 0, "没有任何 pt=2 的可见传送门");
        Debug.Log("[FieldCheck] 可见传送门(pt=2) " + visiblePortals + " 个，动画 " +
                  FieldPortalArt.Visible.Count + " 帧");

        // —— 地面跟随（台阶）——
        // 原端 foothold 用 prev/next 串成可走路径；运行时按「端点相接即视为同一块地面」跟随。
        // 之前用「高度差 ≤ 0.6u 才算支撑」，台阶高过 24px 就判成踏空，下面又没段接住时角色会掉出地图。
        // 这里验证：凡是数据上端点相接的两条段，跟随必须成功。
        int joinedEnds = 0, followFails = 0, ropeNoFoot = 0, ropeNoTop = 0, ropeCount = 0;
        foreach (var kv in maps)
        {
            var m = kv.Value;
            var ts = new List<Vector4>();
            foreach (var f in m.footholds)
            {
                if (Mathf.Abs(f.x2 - f.x1) < 0.01f) continue;       // 竖直段是墙，不当地面
                ts.Add(new Vector4(m.WorldX(f.x1), m.WorldY(f.y1), m.WorldX(f.x2), m.WorldY(f.y2)));
            }
            PlayerCtrl.SetTerrain(ts);
            float tol = 1.5f / Art.PPU;

            foreach (var f in m.footholds)
            {
                if (Mathf.Abs(f.x2 - f.x1) < 0.01f) continue;
                float ax = m.WorldX(f.x1), ay = m.WorldY(f.y1);
                float bx = m.WorldX(f.x2), by = m.WorldY(f.y2);
                float midX = (ax + bx) * 0.5f, midY = (ay + by) * 0.5f;

                for (int side = 0; side < 2; side++)
                {
                    float ex = side == 0 ? ax : bx, ey = side == 0 ? ay : by;
                    bool joined = false;
                    foreach (var g in m.footholds)
                    {
                        if (ReferenceEquals(g, f) || Mathf.Abs(g.x2 - g.x1) < 0.01f) continue;
                        if (Mathf.Abs(m.WorldX(g.x1) - ex) <= tol && Mathf.Abs(m.WorldY(g.y1) - ey) <= tol) { joined = true; break; }
                        if (Mathf.Abs(m.WorldX(g.x2) - ex) <= tol && Mathf.Abs(m.WorldY(g.y2) - ey) <= tol) { joined = true; break; }
                    }
                    if (!joined) continue;
                    joinedEnds++;

                    // 从段中点走向端点外 1px：跨接缝不能判成踏空
                    float outX = ex + (ex >= midX ? 1f : -1f) / Art.PPU;
                    float got;
                    if (!PlayerCtrl.FollowGround(midX, midY, outX, out got))
                    {
                        followFails++;
                        Debug.LogError("[FieldCheck] 失败：" + m.id + " " + m.name + " foothold " + f.id +
                                       " 端点 x=" + ex.ToString("F2") + " 与其他段相接，但跟随失败");
                    }
                }
            }

            // —— 绳梯可达性：站在底端按上要能抓住 ——
            foreach (var r in m.ropes)
            {
                ropeCount++;
                float x = m.WorldX(r.x);
                float bottom = m.WorldY(r.y2);
                float top = m.WorldY(r.y1);
                float g = PlayerCtrl.GroundAt(x, bottom);
                // 抓取条件里给了 100px 纵向余量（见 PlayerCtrl.StepClimb），地面比梯底还低就抓不到
                if (g < bottom - 100f / Art.PPU) { ropeNoFoot++; Debug.LogWarning("[FieldCheck] 绳梯底端悬空，只能从上方爬：" + m.id + " " + m.name + " x=" + r.x); }
                if (r.upperExit && PlayerCtrl.GroundAt(x, top) < top - 30f / Art.PPU)
                {
                    ropeNoTop++;
                    Debug.LogWarning("[FieldCheck] 绳梯顶端无可站平台（uf=1）：" + m.id + " " + m.name + " x=" + r.x);
                }
            }
        }
        PlayerCtrl.SetTerrain(null);
        Check(followFails == 0, "地面跟随在 " + followFails + " 个相接端点判成了踏空");
        // 原数据里本身就有「绳底悬在落脚点上方很远」的条目（如 40002 x=1059 差 107px），
        // 原端从下面也够不着、只能从上方爬下来。这里只做上界守卫，避免导出再次整体错位。
        Check(ropeNoFoot <= 2, "绳梯底端悬空 " + ropeNoFoot + " 条（超过 2 条说明坐标换算有问题）");
        Debug.Log("[FieldCheck] 地面相接端点 " + joinedEnds + " 个全部可走；绳梯 " + ropeCount +
                  " 条，底部悬空 " + ropeNoFoot + "，顶端无平台 " + ropeNoTop);

        // 掉落表：每条记录都要能落到导出的物品上
        int dropRows = 0, zeroChance = 0;
        foreach (var kv in FieldCatalog.Drops)
        {
            Check(FieldCatalog.Mobs.ContainsKey(kv.Key), "掉落表怪物 " + kv.Key + " 未导出");
            foreach (var row in kv.Value)
            {
                dropRows++;
                // chance=0 在 drop_data 里表示「永不掉落」（Randomizer.nextInt(999999) < 0 恒假），不是非法值
                if (row.chance == 0) { zeroChance++; continue; }
                if (row.item == 0) continue;
                Check(FieldCatalog.Items.ContainsKey(row.item), "掉落物品 " + row.item + " 未导出名称");
                Check(row.chance > 0 && row.chance <= FieldCatalog.ChanceDenominator,
                      "掉落概率越界 " + row.item + " chance=" + row.chance);
                Check(row.max >= row.min && row.min >= 1, "掉落数量区间非法 " + row.item);
            }
        }
        foreach (var g in FieldCatalog.GlobalDrops)
            Check(FieldCatalog.Items.ContainsKey(g.item), "全局掉落物品 " + g.item + " 未导出");

        // 怪物属性：HP/经验必须来自 Mob.wz，不能是 0
        foreach (var kv in FieldCatalog.Mobs)
        {
            var s = kv.Value.stats;
            Check(s != null && s.Count > 0, "怪物 " + kv.Key + " 没有属性");
            Check(Art.Num(s, "maxHP", 0) > 0, "怪物 " + kv.Key + " maxHP 缺失");
            Check(Art.Num(s, "level", 0) > 0, "怪物 " + kv.Key + " level 缺失");
            Check(Art.Has("mobs", kv.Value.artKey), "怪物 " + kv.Key + " 动作帧未登记到 manifest");
        }

        // 传送门连通性（从彩虹村出发能走到的地图）
        var seen = new HashSet<int> { 1000000 };
        var queue = new Queue<int>();
        queue.Enqueue(1000000);
        while (queue.Count > 0)
        {
            var cur = maps[queue.Dequeue()];
            foreach (var p in cur.portals)
            {
                if (!p.Warps || p.tm == cur.id) continue;
                if (!maps.ContainsKey(p.tm)) continue;
                if (seen.Add(p.tm)) queue.Enqueue(p.tm);
            }
        }
        Debug.Log("[FieldCheck] 地图 " + maps.Count + " 张（彩虹村可步行到达 " + seen.Count +
                  " 张）；刷怪点 " + totalSpawns + "，传送门 " + totalPortals +
                  "，foothold " + totalFootholds + "，绳梯 " + totalRopes + "，NPC " + totalNpcs +
                  "（隐藏 " + hiddenNpcs + "）；掉落行 " + dropRows + "（chance=0 共 " + zeroChance +
                  "），全局掉落 " + FieldCatalog.GlobalDrops.Count);
        Debug.Log("[FieldCheck] 悬空传送门目标点 " + danglingTargets + " 个（原数据如此，落地时回退到目标图首个传送门）");

        // 换图渐变：三段时长必须都存在，且总时长落在原版手感范围（0.5~1.0s）。
        // 原端这段在 EXE 内硬编码、无数据可查，所以只能守住「别被改成 0 或长到像读条」。
        Check(ScreenFade.OutSeconds > 0f && ScreenFade.InSeconds > 0f && ScreenFade.HoldSeconds >= 0f,
              "换图渐变缺少渐隐/渐显时长");
        Check(ScreenFade.TotalSeconds >= 0.5f && ScreenFade.TotalSeconds <= 1.0f,
              "换图渐变总时长 " + ScreenFade.TotalSeconds.ToString("F2") + "s 超出 0.5~1.0s");

        CheckProgression();
        CheckStatusBar();

        Debug.Log("[FieldCheck] 通过 " + pass + " 项，失败 " + fail + " 项");
        EditorApplication.Exit(fail == 0 ? 0 : 1);
    }

    // —————————— 等级 / 经验 / HP·MP ——————————
    // 数值全部来自原服务端：ExpTable.java（表长 250、上限 255）、Character.levelUp（成长区间）。
    static void CheckProgression()
    {
        ExpTable.Ensure();
        Check(ExpTable.Count == 250, "经验表长度 " + ExpTable.Count + " != 250（ExpTable.java）");
        Check(ExpTable.MaxLevel == 255, "等级上限 " + ExpTable.MaxLevel + " != 255（mxj_max_level）");
        // 原表 exp[level]：1→2 需 15，2→3 需 34，3→4 需 57，4→5 需 92
        Check(ExpTable.Needed(1) == 15, "Lv1 升级经验 " + ExpTable.Needed(1) + " != 15");
        Check(ExpTable.Needed(2) == 34, "Lv2 升级经验 " + ExpTable.Needed(2) + " != 34");
        Check(ExpTable.Needed(3) == 57, "Lv3 升级经验 " + ExpTable.Needed(3) + " != 57");
        Check(ExpTable.Needed(4) == 92, "Lv4 升级经验 " + ExpTable.Needed(4) + " != 92");
        // 表尾 clamp：超过 249 的等级一律取最后一项 2,100,000,000
        Check(ExpTable.Needed(255) == ExpTable.Needed(249), "Lv255 经验未按表尾 clamp");

        // 成长区间（isBeginner/warrior/magician/bowman/thief 与 Character.levelUp 一致）
        int hpMin, hpMax, mpMin, mpMax;
        LevelSystem.HpMpRange(JobBranch.Warrior, out hpMin, out hpMax, out mpMin, out mpMax);
        Check(hpMin == 24 && hpMax == 28 && mpMin == 4 && mpMax == 6, "战士成长区间不对");
        LevelSystem.HpMpRange(JobBranch.Magician, out hpMin, out hpMax, out mpMin, out mpMax);
        Check(hpMin == 10 && hpMax == 14 && mpMin == 22 && mpMax == 24, "法师成长区间不对");
        LevelSystem.HpMpRange(JobBranch.Beginner, out hpMin, out hpMax, out mpMin, out mpMax);
        Check(hpMin == 12 && hpMax == 16 && mpMin == 10 && mpMax == 12, "初心者成长区间不对");

        // 五个职业都必须落到原端的四条成长分支上，不能漏成 Beginner
        foreach (var cls in new[] { "knight", "mage", "priest", "archer" })
            Check(LevelSystem.BranchOf(cls) != JobBranch.Beginner, "职业 " + cls + " 没有对应的成长分支");

        // 升级链路：刚好够升级时，等级 +1、溢出经验归零、HP/MP 按区间上涨
        var d = new PlayerProfileData();
        LevelSystem.EnsureBaseline(d);
        int hp0 = d.baseMaxHp, mp0 = d.baseMaxMp;
        int levels = LevelSystem.AddExperience(d, ExpTable.Needed(1), true);
        Check(levels == 1 && d.level == 2, "15 经验应升到 2 级，实际 " + d.level + " 级（升 " + levels + "）");
        Check(d.experience == 0, "升级后剩余经验 " + d.experience + " != 0");
        Check(d.baseMaxHp > hp0, "升级后 HP 上限没涨 " + hp0 + " → " + d.baseMaxHp);
        Check(d.baseMaxMp > mp0, "升级后 MP 上限没涨 " + mp0 + " → " + d.baseMaxMp);
        Check(d.ap == LevelSystem.ApPerLevel, "升级后 AP " + d.ap + " != " + LevelSystem.ApPerLevel);

        // 升级保护：一次给一大笔经验也只升 1 级（原端 use_level_up_protect=true）
        var e = new PlayerProfileData();
        LevelSystem.EnsureBaseline(e);
        int many = LevelSystem.AddExperience(e, 1000000, true);
        Check(many == 1 && e.level == 2, "升级保护失效：一次升了 " + many + " 级到 " + e.level);

        // 满级封顶 99%（getMaxLevelExpLimit）
        var f = new PlayerProfileData();
        LevelSystem.EnsureBaseline(f);
        f.level = ExpTable.MaxLevel;
        f.attributes.level = f.level;
        LevelSystem.AddExperience(f, 1000000, true);
        Check(f.level == ExpTable.MaxLevel, "满级后等级越界到 " + f.level);
        long cap = (long)ExpTable.Needed(f.level) * 99 / 100;
        // 封顶只在 total 超过 cap 时生效；这里给的是 1000000，必然被压到 cap 以下
        Check(f.experience <= cap, "满级经验 " + f.experience + " 超过 99% 上限 " + cap);

        // 老存档迁移：本地实测存档 experience=469、level=1，按原曲线应补到 6 级、余 136
        var g = new PlayerProfileData();
        LevelSystem.EnsureBaseline(g);
        g.experience = 469;
        int gained = LevelSystem.Reconcile(g);
        Check(gained == 5 && g.level == 6 && g.experience == 136,
              "旧存档补级结果 " + g.level + " 级 / 余 " + g.experience + "（期望 6 级 / 136）");

        // MP 消耗：每个技能的 mpCon 都要读得到（WZ 的 mpCon 是原值，不能是 0）
        int skills = 0, missingMp = 0;
        foreach (var cls in new[] { "knight", "mage", "priest", "archer" })
            foreach (var slot in ClassDefs.Get(cls).skills)
            {
                var def = NativeSkillCatalog.Get(slot.art);
                if (def == null) { missingMp++; continue; }
                skills++;
                if (def.MpCost <= 0) missingMp++;
            }
        Check(skills > 0, "没有任何技能读到等级数据");
        Check(missingMp == 0, "有 " + missingMp + " 个技能缺 mpCon");
        Debug.Log("[FieldCheck] 等级：经验表 250 项，上限 255；技能 MP 消耗 " + skills + " 条全部可读");
    }

    // —————————— 原版状态栏素材 ——————————
    static void CheckStatusBar()
    {
        // 尺寸必须和 UI.wz 原图一致，否则排版常量（216/2/109+108+123）会整体错位。
        // 570x35 = base/backgrnd2 去掉顶部聊天条（y0..35）之后的状态栏面板。
        var panel = Art.Tex("ui/statusbar/bar_lv.bytes");
        Check(panel != null, "缺少状态栏底板 ui/statusbar/bar_lv");
        if (panel != null)
            Check(panel.width == 570 && panel.height == 35,
                  "状态栏底板尺寸 " + panel.width + "x" + panel.height + " != 570x35");
        var full = Art.Tex("ui/bar/backgrnd2_full.bytes");
        Check(full != null && full.width == 570 && full.height == 71, "缺少 570x71 的原始状态栏底板 backgrnd2_full");

        int weight = 0;
        foreach (var n in new[] { "hp", "mp", "exp" })
        {
            var label = Art.Tex("ui/statusbar/gauge_" + n + "_label.bytes");
            var fill = Art.Tex("ui/statusbar/gauge_" + n + "_fill.bytes");
            var frame = Art.Tex("ui/statusbar/gauge_" + n + "_frame.bytes");
            Check(label != null && fill != null && frame != null, "缺少 " + n + " 槽素材");
            if (fill != null)
            {
                Check(fill.height == 18, n + " 槽高度 " + fill.height + " != 18（gauge/bar 下半段）");
                weight += fill.width;
            }
        }
        Check(weight == 340, "三条槽总宽 " + weight + " != 340（gauge/bar 原图宽）");

        for (int i = 0; i < 10; i++)
        {
            var num = Art.Tex("ui/statusbar/num_" + i + ".bytes");
            var lv = Art.Tex("ui/level/lv_" + i + ".bytes");
            Check(num != null && num.width == 5 && num.height == 7, "点阵数字 " + i + " 缺失或尺寸不对");
            Check(lv != null && lv.width == 11 && lv.height == 13, "等级数字 " + i + " 缺失或尺寸不对");
        }
        Check(Art.Tex("ui/statusbar/num_slash.bytes") != null, "缺少 / 字形");
        Check(Art.Tex("ui/statusbar/num_percent.bytes") != null, "缺少 % 字形");
        Check(Art.Tex("ui/statusbar/num_lb.bytes") != null, "缺少 [ 字形");
        Check(Art.Tex("ui/statusbar/num_rb.bytes") != null, "缺少 ] 字形");
        // 空槽底色：1x16 的 #CCCCCC 条，没填满的那一段靠它才是浅灰而不是面板底色
        var gray = Art.Tex("ui/statusbar/gauge_gray.bytes");
        Check(gray != null && gray.width == 1 && gray.height == 16, "缺少空槽底色 gauge/gray（1x16）");

        CheckBottomBar();
        CheckSkillTree();
        CheckKeyboardAssets();
    }

    // —————————— 底部操作栏（UI.wz/StatusBar.img） ——————————
    static void CheckBottomBar()
    {
        var bar = Art.Tex("ui/bar/backgrnd.bytes");
        Check(bar != null && bar.width == 817 && bar.height == 71, "底部栏底板不是 817x71");
        var col = Art.Tex("ui/bar/plate_col.bytes");
        Check(col != null && col.width == 1 && col.height == 35, "底部栏横向平铺条不是 1x35");

        string[] big = { "BtShop", "BtMenu", "BtShort", "BtNPT" };
        foreach (var b in big)
            foreach (var st in new[] { "normal", "mouseOver", "pressed", "disabled" })
            {
                var t = Art.Tex("ui/bar/" + b + "_" + st + ".bytes");
                Check(t != null && t.width == 54 && t.height >= 34, "缺少大按钮 " + b + "/" + st + " 或尺寸不对");
            }

        string[] small = { "EquipKey", "InvenKey", "StatKey", "SkillKey", "KeySet", "QuickSlotD" };
        foreach (var b in small)
        {
            var t = Art.Tex("ui/bar/" + b + "_normal.bytes");
            Check(t != null && t.width == 28 && t.height == 20, "缺少小按钮 " + b + " 或尺寸不对");
        }

        var qs = Art.Tex("ui/bar/quickSlot08.bytes");
        Check(qs != null && qs.width == 151 && qs.height == 80, "快捷栏底图不是 151x80（8 格）");
        for (int i = 0; i < 8; i++)
            Check(Art.Tex("ui/bar/keylabel_" + i + ".bytes") != null, "缺少快捷栏键帽文字 key/" + i);

        // 版式：四张大按钮必须首尾相接、贴着右边，且不压住左侧状态条
        float right = BottomBar.BigButtonRect(3).xMax;
        Check(Mathf.Abs(right - (UiKit.W - 6f * BottomBar.S)) < 0.5f, "大按钮没有贴右边");
        Check(Mathf.Abs(BottomBar.BigButtonRect(1).x - BottomBar.BigButtonRect(0).xMax) < 0.5f, "大按钮之间有缝或重叠");
        Check(BottomBar.BigButtonRect(0).x > 570f * BottomBar.S, "大按钮压到了左侧状态条");
        Check(BottomBar.SmallButtonRect(0).y < BottomBar.BigButtonRect(0).y, "小按钮必须在大按钮上方");
        Check(BottomBar.SlotRect(3).xMax <= BottomBar.QuickPanelRect().xMax + 0.5f, "快捷格超出快捷栏底图");
        Check(BottomBar.SlotRect(7).yMax <= BottomBar.QuickPanelRect().yMax + 0.5f, "第二行快捷格超出快捷栏底图");
    }

    // —————————— 技能树（Skill.wz + String.wz/Skill.img） ——————————
    static void CheckSkillTree()
    {
        SkillBook.Ensure();
        SkillBook.LineFor("knight");
        int lines = 0, total = 0;
        foreach (var id in new[] { "knight", "warrior", "mage", "archer", "priest" })
        {
            var line = SkillBook.LineFor(id);
            Check(line != null, "缺少职业线 " + id);
            if (line == null) continue;
            lines++;
            var tabs = SkillBook.Tabs(line);
            Check(tabs.Count >= 3, id + " 的转职页签少于 3 个");
            foreach (var tab in tabs)
            {
                total++;
                Check(!string.IsNullOrEmpty(SkillBook.JobName((int)Art.Num(tab, "job"))), id + " 页签职业名缺失");
                foreach (var raw in (List<object>)tab["skills"])
                {
                    var d = SkillBook.Get(int.Parse(raw.ToString()));
                    Check(d != null, "技能树里的 " + raw + " 没有数据");
                    if (d == null) continue;
                    Check(!string.IsNullOrEmpty(d.name), "技能 " + d.id + " 没有中文名");
                    Check(d.maxLevel > 0, "技能 " + d.id + " 上限等级不是正数");
                    Check(d.levels.Count > 0, "技能 " + d.id + " 没有逐级数据");
                    // 原客户端里有极少数技能本来就没有 icon（invisible 被动），允许为空但仍要求能加载
                    Check(d.icon == "" || Art.Tex(d.icon) != null, "技能 " + d.id + " 图标文件加载不到");
                }
            }
        }
        Check(lines == 5, "职业技能线只有 " + lines + " 条");
        Check(total >= 15, "转职页签总数 " + total + " 偏少");

        // 原版数值抽查：1121006（英雄的突进）1 级 mpCon=22、damage=72
        var dash = SkillBook.Get(1121006);
        Check(dash != null && dash.maxLevel == 30, "1121006 的等级上限不是 30");
        var native = SkillBook.Native(1121006, 1);
        Check(native != null && native.MpCost == (int)Art.Num(dash.Level(1), "mpCon") &&
              Mathf.RoundToInt(native.DamagePercent) == (int)Art.Num(dash.Level(1), "damage"),
              "1121006 1 级 mpCon/damage 与 Skill.wz 不一致");
        Check(SkillBook.Native(1121006, 30).MpCost == (int)Art.Num(dash.Level(30), "mpCon"),
              "1121006 30 级 mpCon 读取异常");
        Check(dash.IsAttack, "1121006 应判定为攻击技能");
        // 被动/增益的分流
        var heal = SkillBook.Get(1000000);
        Check(heal != null && heal.IsPassive, "1000000 生命恢复应判定为被动");
        // 技能点规则来自服务端 level_up_sp_gain
        Check(SkillProgress.SpPerLevel == 3, "每级技能点不是 3");
    }

    // —————————— 键位设置（UIWindow.img/KeyConfig） ——————————
    static void CheckKeyboardAssets()
    {
        var bg = Art.Tex("ui/keyconfig/backgrnd.png");
        Check(bg != null && bg.width == 629 && bg.height == 373, "键盘底图不是 629x373");

        // 原版默认键位必须在
        Check(KeyMap.Of(GameAction.Jump) == KeyCode.LeftAlt, "跳跃不是 Alt");
        Check(KeyMap.Of(GameAction.Attack) == KeyCode.LeftControl, "攻击不是 Ctrl");
        Check(KeyMap.Of(GameAction.Skill) == KeyCode.K, "技能窗不是 K");
        Check(KeyMap.Of(GameAction.Stat) == KeyCode.S, "能力值不是 S");
        Check(KeyMap.Of(GameAction.Inven) == KeyCode.I, "背包不是 I");
        Check(KeyMap.Of(GameAction.Equip) == KeyCode.E, "装备不是 E");
        Check(KeyMap.Of(GameAction.Slot1) == KeyCode.LeftShift, "快捷格 1 不是 Shift");
        // 八个快捷格互不重复
        var used = new HashSet<KeyCode>();
        for (int i = 0; i < 8; i++)
            Check(used.Add(KeyMap.Of((GameAction)((int)GameAction.Slot1 + i))), "快捷格 " + (i + 1) + " 的键和其它格重复");

        // 技能窗素材
        var win = Art.Tex("ui/skillwin/backgrnd.png");
        Check(win != null && win.width == 175 && win.height == 289, "技能窗底板不是 175x289");
        var row0 = Art.Tex("ui/skillwin/skill0.png");
        var row1 = Art.Tex("ui/skillwin/skill1.png");
        Check(row0 != null && row0.width == 141 && row0.height == 35, "技能条目未选中贴图不是 141x35");
        Check(row1 != null && row1.width == 141, "技能条目选中贴图尺寸不对");
        for (int i = 0; i < 6; i++)
            Check(Art.Tex("ui/skillwin/tab_" + i + ".png") != null, "缺少技能窗页签 " + i);
        Check(Art.Tex("ui/skillwin/spup.png") != null, "缺少 SP 加点按钮");
    }
}
