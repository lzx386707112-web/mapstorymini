using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using MapleDash.Wz;

// 角色从高处掉落的回归：用真实 PlayerCtrl.StepMovement 在原地图上走空边缘再下落。
//   1) 跌落伤害公式（源端 BotContactDamage.fallDamageFromDistance）
//   2) 落地按「最高点→落点」的总落差结算，且不带 270px/s 接触冲量
//   3) 掉出地图的复位必须往下落，不能把人吸回原高度（回原地）
//   4) 魔法密林等原图逐个真边缘跑一遍
public static class FallChecks
{
    static readonly MethodInfo Step = typeof(PlayerCtrl).GetMethod("Step", BindingFlags.NonPublic | BindingFlags.Instance);
    static void Tick(PlayerCtrl p, float h, bool jump, float dt)
    {
        TouchInput.MoveH = h;
        if (jump) TouchInput.PushJump();
        Step.Invoke(p, new object[] { dt });
    }
    static void Require(bool pass, string message) { if (!pass) throw new Exception(message); }

    static PlayerCtrl Create(List<Vector4> terrain, float x, float y)
    {
        PlayerCtrl.SetTerrain(terrain);
        PlayerCtrl.Climbs = null;
        TouchInput.Reset();
        var p = new GameObject("FallCheck").AddComponent<PlayerCtrl>();
        typeof(PlayerCtrl).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(p, null);
        p.worldX = x; p.terrainY = y; p.jumpY = 0f; p.grounded = true;
        return p;
    }

    // 只留「真边缘」：该端点没有别的段接着走（相接的链式段不算边缘）。
    static List<(float x, float y, int dir)> Ledges(List<Vector4> terrain)
    {
        var result = new List<(float, float, int)>();
        for (int i = 0; i < terrain.Count; i++)
        {
            var s = terrain[i];
            if (Mathf.Abs(s.z - s.x) < .01f) continue;
            int sign = s.z > s.x ? 1 : -1;
            foreach (var end in new[] { (x: s.x, y: s.y, dir: -sign), (x: s.z, y: s.w, dir: sign) })
            {
                bool joined = false;
                for (int j = 0; j < terrain.Count && !joined; j++)
                {
                    if (j == i) continue;
                    var o = terrain[j];
                    if (Mathf.Abs(o.z - o.x) < .01f) continue;
                    float lo = Mathf.Min(o.x, o.z), hi = Mathf.Max(o.x, o.z);
                    if (end.x < lo - .05f || end.x > hi + .05f) continue;
                    float oy = Mathf.Lerp(o.y, o.w, (end.x - o.x) / (o.z - o.x));
                    if (Mathf.Abs(oy - end.y) <= .05f && (Mathf.Abs(lo - end.x) > .001f || Mathf.Abs(hi - end.x) > .001f)) joined = true;
                }
                if (!joined) result.Add((end.x, end.y, end.dir));
            }
        }
        return result;
    }

    static List<Vector4> Terrain(FieldMap map)
    {
        var terrain = new List<Vector4>();
        foreach (var f in map.footholds)
            terrain.Add(new Vector4(f.x1 / Art.PPU, -f.y1 / Art.PPU, f.x2 / Art.PPU, -f.y2 / Art.PPU));
        return terrain;
    }

    public static void Run()
    {
        try
        {
            CheckFormula();
            CheckFallDamage();
            CheckNoUpwardRecovery();
            CheckRealMaps();
            TouchInput.Reset();
            Debug.Log("[FallChecks] PASS 跌落公式、落地结算(不带冲量)、掉出地图只往下、原图真边缘");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    // 源端 BotContactDamage.fallDamageFromDistance 的参考值（Java Math.round 口径）。
    static void CheckFormula()
    {
        var expect = new (double px, int dmg)[] {
            (0, 0), (500, 0), (889, 0), (890, 0), (890.5, 1), (900, 3), (950, 15), (1000, 22),
            (1100, 27), (1200, 28), (1500, 29), (2000, 31), (2500, 32), (3000, 33), (4000, 35), (5000, 38),
        };
        foreach (var c in expect)
            Require(FallDamage.FromDistance(c.px) == c.dmg,
                "跌落伤害公式 " + c.px + "px 应为 " + c.dmg + "，实际 " + FallDamage.FromDistance(c.px));
        int prev = -1;
        for (double d = 0; d < 8000; d += 7.3)
        {
            int v = FallDamage.FromDistance(d);
            Require(v >= prev, "跌落伤害必须随落差单调不减，断在 " + d + "px");
            prev = v;
        }
    }

    static void CheckFallDamage()
    {
        foreach (float dt in new[] { 1f / 30, 1f / 60, 1f / 120 })
        {
            // 30 世界单位 = 1200px：从高台走空掉到平地，按源端公式应是 28 点。
            var p = Create(new List<Vector4> { new Vector4(-5, 30, 5, 30), new Vector4(-10, 0, 10, 0) }, 4.9f, 30f);
            PlayerCtrl.SetBounds(-9f, 9f);
            PlayerCtrl.FallFloor = -100f;
            p.hpMax = p.hp = 100000;
            for (int i = 0; i < Mathf.CeilToInt(2f / dt) && p.grounded; i++) Tick(p, 1f, false, dt);
            Require(!p.grounded, "1200px 用例没走出高台边缘 dt=" + dt);
            for (int i = 0; i < Mathf.CeilToInt(6f / dt) && !p.grounded; i++) Tick(p, 0, false, dt);
            Require(p.grounded && Mathf.Abs(p.FeetPos.y) < .01f, "1200px 用例没落到平地 dt=" + dt + " feet=" + p.FeetPos.y);
            Require(p.hp == 100000 - FallDamage.FromDistance(1200),
                "1200px 跌落伤害不对 dt=" + dt + " 扣=" + (100000 - p.hp) + " 期望=" + FallDamage.FromDistance(1200));
            // 源端落地只扣血：不能带 270px/s 接触冲量（那一下会把角色弹离地）。
            for (int i = 0; i < 40; i++) Tick(p, 0, false, dt);
            Require(p.grounded && Mathf.Abs(p.FeetPos.y) < .01f,
                "跌落伤害把人弹离了地面（不该施加接触冲量）dt=" + dt + " feet=" + p.FeetPos.y);
            UnityEngine.Object.DestroyImmediate(p.gameObject);

            // 10 世界单位 = 400px：低于阈值，一滴血都不能掉。
            p = Create(new List<Vector4> { new Vector4(-5, 10, 5, 10), new Vector4(-10, 0, 10, 0) }, 4.9f, 10f);
            PlayerCtrl.SetBounds(-9f, 9f);
            PlayerCtrl.FallFloor = -100f;
            p.hpMax = p.hp = 100000;
            for (int i = 0; i < Mathf.CeilToInt(2f / dt) && p.grounded; i++) Tick(p, 1f, false, dt);
            for (int i = 0; i < Mathf.CeilToInt(4f / dt) && !p.grounded; i++) Tick(p, 0, false, dt);
            Require(p.grounded && p.hp == 100000, "400px 落差不应扣血 dt=" + dt + " 扣=" + (100000 - p.hp));
            UnityEngine.Object.DestroyImmediate(p.gameObject);

            // 22.5 世界单位 = 900px：刚过阈值，应当是 3 点。
            p = Create(new List<Vector4> { new Vector4(-5, 22.5f, 5, 22.5f), new Vector4(-10, 0, 10, 0) }, 4.9f, 22.5f);
            PlayerCtrl.SetBounds(-9f, 9f);
            PlayerCtrl.FallFloor = -100f;
            p.hpMax = p.hp = 100000;
            for (int i = 0; i < Mathf.CeilToInt(2f / dt) && p.grounded; i++) Tick(p, 1f, false, dt);
            for (int i = 0; i < Mathf.CeilToInt(6f / dt) && !p.grounded; i++) Tick(p, 0, false, dt);
            Require(p.grounded && p.hp == 100000 - FallDamage.FromDistance(900),
                "900px 跌落伤害不对 dt=" + dt + " 扣=" + (100000 - p.hp) + " 期望=" + FallDamage.FromDistance(900));
            UnityEngine.Object.DestroyImmediate(p.gameObject);
        }
    }

    // 掉出地图（下面一条段都没有）时，复位点必须比离地点低——以前会吸回原高度。
    static void CheckNoUpwardRecovery()
    {
        var p = Create(new List<Vector4> { new Vector4(-10, 10, 0, 10), new Vector4(20, 0, 30, 0) }, -0.5f, 10f);
        PlayerCtrl.SetBounds(-40f, 40f);
        PlayerCtrl.FallFloor = -60f;
        p.hpMax = p.hp = 100000;
        for (int i = 0; i < 600 && p.grounded; i++) Tick(p, 1f, false, 1f / 60f);
        Require(!p.grounded, "高台边缘没走空");
        for (int i = 0; i < 2400 && !p.grounded; i++) Tick(p, 0, false, 1f / 60f);
        Require(p.grounded, "掉出地图后没有复位");
        Require(p.FeetPos.y < 10f - .01f, "掉出地图把人吸回了原高度 feet=" + p.FeetPos.y);
        Require(Mathf.Abs(p.FeetPos.y) < .01f, "复位应落在下方那条地面 feet=" + p.FeetPos.y);
        UnityEngine.Object.DestroyImmediate(p.gameObject);
    }

    static void CheckRealMaps()
    {
        int ledges = 0, damaged = 0, recovered = 0;
        float maxRebound = 0f, maxDrop = 0f;
        foreach (int id in new[] { 101000000, 101010000, 101020000, 100040000, 100050000 })
        {
            var map = NativeMapView.LoadMap(new FieldMap { id = id });
            var terrain = Terrain(map);
            PlayerCtrl.SetBounds(map.ViewMinX, map.ViewMaxX);
            PlayerCtrl.FallFloor = map.ViewMinY - 4f;
            var p = Create(terrain, 0, 0);
            p.hpMax = p.hp = 1000000;
            foreach (var l in Ledges(terrain))
            {
                p.hp = 1000000;
                int recoveries = PlayerCtrl.FallRecoveries;
                p.PlaceAt(l.x - l.dir * .1f, l.y);
                for (int i = 0; i < 90 && p.grounded; i++) Tick(p, l.dir, false, 1f / 60f);
                if (p.grounded) continue;                       // 地图左右边界被 ClampX 夹住，没掉下去
                for (int i = 0; i < 1200 && !p.grounded; i++) Tick(p, 0, false, 1f / 60f);
                ledges++;
                Require(p.grounded, "原图边缘掉出后一直没落地 map=" + id + " x=" + l.x);
                Require(p.FeetPos.y <= l.y + .05f,
                    "原图边缘被吸回原高度 map=" + id + " x=" + l.x + " start=" + l.y + " land=" + p.FeetPos.y);
                maxRebound = Mathf.Max(maxRebound, (p.FeetPos.y - l.y) * Art.PPU);
                float dropPx = (l.y - p.FeetPos.y) * Art.PPU;
                maxDrop = Mathf.Max(maxDrop, dropPx);
                // 真空缺口走的是「掉出地图复位」：源端那一路不结算跌落伤害，这里跳过扣血断言。
                if (PlayerCtrl.FallRecoveries != recoveries) { recovered++; continue; }
                bool shouldHurt = FallDamage.FromDistance(dropPx) > 0;
                Require(shouldHurt == (p.hp < 1000000),
                    "原图落差 " + dropPx.ToString("F0") + "px 的跌落伤害对不上 map=" + id + " x=" + l.x);
                if (shouldHurt) damaged++;
            }
            UnityEngine.Object.DestroyImmediate(p.gameObject);
            TouchInput.Reset();
        }
        Require(ledges > 0, "原图没有跑出任何一次边缘掉落");
        Require(damaged > 0, "原图上没有落差超过 890px 的跌落，数据可疑");
        Debug.Log("[FallChecks] 原图真边缘 " + ledges + " 处，最大落差 " + maxDrop.ToString("F0") +
            "px，扣血 " + damaged + " 次，真空复位 " + recovered + " 次，最大回弹 " + maxRebound.ToString("F0") + "px");
    }

    // 诊断用：只打印每张图的落点分布，不做断言。
    public static void Probe()
    {
        try
        {
            foreach (int id in new[] { 101000000, 101010000, 101020000, 100040000, 100050000 })
            {
                var map = NativeMapView.LoadMap(new FieldMap { id = id });
                var terrain = Terrain(map);
                PlayerCtrl.SetBounds(map.ViewMinX, map.ViewMaxX);
                PlayerCtrl.FallFloor = map.ViewMinY - 4f;
                var p = Create(terrain, 0, 0);
                p.hpMax = p.hp = 1000000;
                int total = 0, lower = 0, same = 0, rebound = 0, hurt = 0;
                float maxDrop = 0f, maxUp = 0f;
                var samples = new List<string>();
                foreach (var l in Ledges(terrain))
                {
                    p.hp = 1000000;
                    p.PlaceAt(l.x - l.dir * .1f, l.y);
                    for (int i = 0; i < 90 && p.grounded; i++) Tick(p, l.dir, false, 1f / 60f);
                    if (p.grounded) continue;
                    for (int i = 0; i < 1200 && !p.grounded; i++) Tick(p, 0, false, 1f / 60f);
                    total++;
                    float drop = (l.y - p.FeetPos.y) * Art.PPU;
                    maxDrop = Mathf.Max(maxDrop, drop);
                    if (p.hp < 1000000) hurt++;
                    if (drop > .5f) lower++;
                    else if (drop > -.5f) { same++; if (samples.Count < 10) samples.Add("same x=" + l.x.ToString("F2") + " y=" + l.y.ToString("F2")); }
                    else { rebound++; maxUp = Mathf.Max(maxUp, -drop); if (samples.Count < 10) samples.Add("rebound x=" + l.x.ToString("F2") + " y=" + l.y.ToString("F2") + " land=" + p.FeetPos.y.ToString("F2")); }
                }
                UnityEngine.Object.DestroyImmediate(p.gameObject);
                TouchInput.Reset();
                Debug.Log("[FallProbe] map=" + id + " ledges=" + total + " 落到更低=" + lower + " 原地=" + same +
                    " 往上回弹=" + rebound + " 扣血=" + hurt + " 最大落差=" + maxDrop.ToString("F0") +
                    "px 最大回弹=" + maxUp.ToString("F0") + "px");
                foreach (var s in samples) Debug.Log("[FallProbe]   " + s);
            }
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
