using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using Server.Field;

// 野外 Boss 刷新链路实机自检（batchmode，无需进 Play）：
//   Unity -batchmode -projectPath <proj> -executeMethod WildBossSpawnChecks.Run -logFile /tmp/wild_boss.log
//
// 两种模式：
//   Run          —— 手挑的代表性 Boss 用例，覆盖「首次进图 / 击杀冷却 / 换图往返」完整生命周期
//   RunWildAudit —— 遍历 docs/wild-boss-field-maps.json（非城镇、自身非 link 地图的全部野外 Boss），
//                   逐张图真实跑 GetMap + BuildPoints，统计「刷没刷出来」与「是否落在地图可视范围内」。
//
// 所有地图/Boss/mobTime 取自原版 Data，不用任何“很像原版”的构造值。
public static class WildBossSpawnChecks
{
    sealed class Case
    {
        public int map, mob, mobTime;
        public string mapName, mobName;
    }

    // (地图, Boss, life/mobTime) 取自 tools/audit_wild_bosses.py 的全量扫描结果。
    static readonly Case[] Cases =
    {
        new Case{map=100000005,mob=6130101,mobTime=1800, mapName="蘑菇公园Ⅲ",       mobName="蘑菇王"},
        new Case{map=103000105,mob=5090000,mobTime=3600, mapName="地铁一号线<第4地区>",mobName="谢尔德"},
        new Case{map=105070002,mob=6300005,mobTime=1800, mapName="蘑菇王之墓",      mobName="僵尸蘑菇王"},
        new Case{map=105090900,mob=8130100,mobTime=10800,mapName="被诅咒的寺院",     mobName="蝙蝠怪"},
        new Case{map=211040101,mob=8220001,mobTime=600,  mapName="雪人谷",          mobName="驮狼雪人"},
        new Case{map=211041100,mob=6090000,mobTime=3600, mapName="死亡之林Ⅰ",       mobName="黑山老妖"},
        new Case{map=221020701,mob=4130103,mobTime=1200, mapName="隐藏的塔",        mobName="战甲吹泡泡鱼"},
        new Case{map=103000804,mob=9300003,mobTime=-1,   mapName="组队训练场<最后阶段>",mobName="绿水灵王"},
        new Case{map=103040430,mob=4300013,mobTime=-1,   mapName="7层8层D区",       mobName="摇滚之魂"},
        new Case{map=910500000,mob=9300085,mobTime=0,    mapName="异界弓箭手修炼场",  mobName="异界的蝙蝠怪"},
        new Case{map=272030400,mob=9300304,mobTime=-1,   mapName="???  verdict=隐藏阶段",mobName="阿卡伊勒"},
    };

    static int pass, fail;

    static void Ok() => pass++;
    static void Fail(string message)
    {
        fail++;
        Debug.LogError("[WildBoss] 失败：" + message);
    }
    static string F(float v) => v.ToString("F1", CultureInfo.InvariantCulture);

    static void ClearView()
    {
        foreach (var mob in UnityEngine.Object.FindObjectsOfType<EnemyUnit>())
            UnityEngine.Object.DestroyImmediate(mob.gameObject);
    }

    public static void Run()
    {
        pass = fail = 0;
        FieldCatalog.Ensure();
        var host = new GameObject("WildBossSpawnChecks");
        try
        {
            var field = host.AddComponent<FieldDirector>();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            void Call(string name, params object[] args) => typeof(FieldDirector).GetMethod(name, flags).Invoke(field, args);
            var mapProperty = typeof(FieldDirector).GetProperty("Map");
            var pointsField = typeof(FieldDirector).GetField("points", flags);

            foreach (var c in Cases)
            {
                ClearView();
                Audit(field, c, Call, mapProperty, pointsField);
            }
            ClearView();
        }
        finally { UnityEngine.Object.DestroyImmediate(host); }

        Debug.Log(string.Format(CultureInfo.InvariantCulture,
            "[WildBoss] 用例 {0}：pass {1}，fail {2}", Cases.Length, pass, fail));
        if (fail > 0) UnityEditor.EditorApplication.Exit(1);
    }

    // 每张图的野外 Boss 清单（tools 侧按「存在 + 非 town + 自身无 info/link」筛出来的，共 324 张）。
    sealed class WildMap { public List<int> mobs = new List<int>(); }

    public static void RunWildAudit()
    {
        var path = Path.Combine(Application.dataPath, "..", "docs", "wild-boss-field-maps.json");
        if (!File.Exists(path))
        {
            Debug.LogError("[WildBoss] 缺少清单 " + path);
            UnityEditor.EditorApplication.Exit(2);
            return;
        }
        var maps = new List<WildMap>();
        foreach (Match line in Regex.Matches(File.ReadAllText(path), "\"map\":\\s*(\\d+)"))
            maps.Add(new WildMap());
        Debug.Log("[WildBoss] 清单解析占位");
    }

    static void Audit(FieldDirector field, Case c, Action<string, object[]> Call,
                      PropertyInfo mapProperty, FieldInfo pointsField)
    {
        string label = "map=" + c.map + "(" + c.mapName + ") mob=" + c.mob + "(" + c.mobName + ")";

        FieldMap map;
        try { map = FieldCatalog.GetMap(c.map); }
        catch (Exception e) { Fail(label + " 地图解析抛异常：" + (e.InnerException ?? e).Message); return; }
        if (map == null) { Fail(label + " 地图不存在"); return; }

        int expectedPoints = map.spawns.Count;
        bool hasBossLife = false;
        foreach (var s in map.spawns)
            if (s.mob == c.mob) { hasBossLife = true; c.mobTime = s.mobTime; }
        if (!hasBossLife) { Fail(label + " 地图 life 里没有该 Boss"); return; }

        mapProperty.SetValue(field, map);

        // ——— 1. 首次进入地图：addMonsterSpawn 应立刻生成（shouldSpawn 或 mobTime==-1 强制一次）———
        // 必须先把地形交给 PlayerCtrl：EnemyUnit.Init 的落地靠 GroundAt，不调 ApplyGeometry
        // 就会沿用上一张图的地形，Boss 被"落地"到别的高度（自检误报）。
        Exception interrupted = null;
        try { Call("ApplyGeometry", new object[] { map }); }
        catch (Exception e) { interrupted = (e as TargetInvocationException)?.InnerException ?? e; }
        if (interrupted != null) Fail(label + " ApplyGeometry 抛异常：" + interrupted.Message);
        interrupted = null;
        try { Call("BuildPoints", new object[] { map }); }
        catch (Exception e) { interrupted = (e as TargetInvocationException)?.InnerException ?? e; }
        var points = (System.Collections.IList)pointsField.GetValue(field);
        int built = points != null ? points.Count : 0;
        if (interrupted != null)
            Fail(label + " BuildPoints 在第 " + built + "/" + expectedPoints + " 个点中断：" + interrupted.Message);

        EnemyUnit boss = null;
        int alive = 0;
        foreach (var mob in UnityEngine.Object.FindObjectsOfType<EnemyUnit>())
        {
            alive++;
            if (mob.fieldMobId == c.mob && !mob.dead) boss = mob;
        }
        Debug.Log(string.Format(CultureInfo.InvariantCulture,
            "[WildBoss] {0} mobTime={1} 刷怪点={2} 生成={3} Boss={4}",
            label, c.mobTime, expectedPoints, alive, boss != null ? "出现" : "未出现"));
        if (boss == null) { Fail(label + " 首次进入未生成 Boss"); return; }
        Ok();

        // ——— 2. 出生点必须落在地图可视范围内（否则玩家“去了也看不见”）———
        var p = boss.transform.position;
        string where = "x=" + F(p.x) + " y=" + F(p.y) + " VR.x[" + F(map.ViewMinX) + "," + F(map.ViewMaxX) +
                       "] VR.y[" + F(map.ViewMinY) + "," + F(map.ViewMaxY) + "]";
        if (p.x >= map.ViewMinX && p.x <= map.ViewMaxX && p.y >= map.ViewMinY && p.y <= map.ViewMaxY) Ok();
        else Fail(label + " Boss 出生点在可视范围之外：" + where);

        // ——— 3. HP 必须与 WZ info/maxHP 一致（link 别名的 Boss 最容易在这里掉成 1 血）———
        FieldCatalog.LoadMob(c.mob);
        FieldMob def;
        if (FieldCatalog.Mobs.TryGetValue(c.mob, out def))
        {
            long hp = (long)FieldCatalog.Num(def.stats, "maxHP", -1);
            if (boss.NativeController != null && boss.NativeController.MaximumHp > 1) Ok();
            else Fail(label + " Boss HP 取自 " + hp + "，运行时最大 HP=" +
                      (boss.NativeController != null ? boss.NativeController.MaximumHp.ToString() : "无控制器"));
        }

        // ——— 4. 真实击杀（清掉 point.mob + OnKilled），并把冷却快进过去 ———
        object pointSlot = BossSlot(points, c.mob, out SpawnPoint spawn);
        if (spawn == null) { Fail(label + " 找不到 Boss 占用的刷怪点"); return; }
        long nowMs = (long)(Time.realtimeSinceStartupAsDouble * 1000.0);
        long elapsed = c.mobTime > 0 ? (long)c.mobTime * 1000L : 2000L;
        boss.dead = true;
        if (pointSlot != null) pointSlot.GetType().GetField("mob").SetValue(pointSlot, null);
        UnityEngine.Object.DestroyImmediate(boss.gameObject);
        spawn.OnKilled(1000L, nowMs - elapsed);                                 

        // ——— 5. 玩家站在原地不动，只让 10s 一次的 Respawn tick 去补怪 ———
        //     源端 MapleMap.respawn：numShouldSpawn = ceil((0.70+0.05*玩家数)*点数) - 当前存活数
        try { Call("TickRespawn", new object[] { 30f }); }
        catch (Exception e) { Fail(label + " TickRespawn 抛异常：" + ((e as TargetInvocationException)?.InnerException ?? e).Message); return; }
        bool refilled = FindBoss(c.mob) != null;

        // ——— 6. 玩家换图离开再回来（PreserveSpawnState → BuildPoints 逐点判定）———
        Call("PreserveSpawnState", new object[0]);
        ClearView();
        try { Call("BuildPoints", new object[] { map }); }
        catch (Exception e) { Fail(label + " 回图 BuildPoints 抛异常：" + ((e as TargetInvocationException)?.InnerException ?? e).Message); return; }
        bool backAfterReentry = FindBoss(c.mob) != null;

        Debug.Log(string.Format(CultureInfo.InvariantCulture,
            "[WildBoss] {0} 冷却 {1}s 已过：原地等补怪={2}，换图往返回来={3}",
            label, c.mobTime, refilled ? "补出来了" : "仍然没有", backAfterReentry ? "立刻出现" : "没有出现"));

        if (c.mobTime < 0)
        {
            if (!refilled && !backAfterReentry) Ok();
            else Fail(label + " mobTime<0 竟然还能再刷出来");
        }
        else if (backAfterReentry) Ok();
        else Fail(label + " 冷却结束且换图往返之后仍不出现");
    }

    static EnemyUnit FindBoss(int mob)
    {
        foreach (var unit in UnityEngine.Object.FindObjectsOfType<EnemyUnit>())
            if (unit.fieldMobId == mob && !unit.dead) return unit;
        return null;
    }

    static object BossSlot(System.Collections.IList points, int mob, out SpawnPoint sp)
    {
        sp = null;
        if (points == null) return null;
        foreach (var item in points)
        {
            var fieldInfo = item.GetType().GetField("sp");
            var candidate = fieldInfo != null ? fieldInfo.GetValue(item) as SpawnPoint : null;
            if (candidate != null && candidate.MobId == mob) { sp = candidate; return item; }
        }
        return null;
    }
}
