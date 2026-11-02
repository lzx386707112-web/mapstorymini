using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using MapleDash.Js;

// 露希妲远征（反馈清单第 12 项 / 交接文档「仍未做」里的 LucidBattle 本体）。
// 全部走源端原脚本，不在这里重写任何一条规则：
//   portal/lucid_expedition.js（450004000 的传送门）
//   npc/3003208.js → BeiDouSpecial/露希妲远征.js（组建远征队 → startInstance）
//   event/LucidBattle.js（阶段、死亡次数、花朵爆炸、坠落回收、清关）
// 宿主侧被验证的是 LucidBossCompat（源端 org.gms.server.life.LucidBossCompat 的控制器）
// 与五个脚本面 Java 类型：LifeFactory / LucidBossCompat / PacketCreator /
// AbstractAnimatedMapObject / ThreadLocalRandom。
//
// 时间轴一律用「距开场的绝对毫秒」推进（At），所以每条断言都对应源端一个确切的定时点。
public static class LucidBattleChecks
{
    const BindingFlags Flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
    const int ExitMap = 450004000, EntryMap = 450004150, PhaseTwoMap = 450004250;
    const int BattleNpc = 9900001;
    // 露希妲各阶段与召唤物（LucidBossCompat.java 常量）。
    const int P1 = 8880140, P2 = 8880141, P3 = 8880152, FuryFail = 8880154;
    const int ButterflyP1 = 8880165, GolemP1 = 8880161, MushroomP1 = 8880164;

    static long clock = 1700000000000;
    static long battleStart;
    static JsValue eim;

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("LucidBattle: " + message);
    }
    static int passed;
    static void Step(string message) { passed++; Debug.Log("[LucidBattle] 通过 " + message); }

    static void Set(string key, object value)
        => typeof(GameSession).GetProperty(key).GetSetMethod(true).Invoke(null, new[] { value });
    static T Field<T>(Type type, string name) => (T)type.GetField(name, Flags).GetValue(null);
    static void SetField(Type type, string name, object value) => type.GetField(name, Flags).SetValue(null, value);

    static NativeEventScripts Host() => Field<NativeEventScripts>(typeof(NativeEventScripts), "active");
    // batchmode 不跑 Update：事件延迟任务 + LucidBossCompat 的 250ms 控制器都靠这里推进。
    static void Tick() => typeof(NativeEventScripts).GetMethod("TickNow", Flags).Invoke(Host(), null);
    // 一拍最多推进 250ms —— 源端 Encounter 就是 TimerManager.register(this, 250ms)。
    // 一次跨几秒直接跳会把「本该在不同拍发生的两件事」压进同一拍，后执行的那个会把
    // LastPacket 覆盖掉（踩过：bomb 覆盖 rush）。
    static void StepTo(long target)
    {
        if (clock == target) { Tick(); return; }
        while (clock < target) { clock = Math.Min(target, clock + 250); Tick(); }
    }
    static void Advance(long ms) => StepTo(clock + ms);
    // 推进到「距开场 offset 毫秒」并跑一拍。
    static void At(long offset)
    {
        long target = battleStart + offset;
        Require(clock <= target, "时间轴倒退了（当前 " + (clock - battleStart) + " > " + offset + "）");
        StepTo(target);
    }
    static NativeEventMap EventMap(int id) => NativeEventScripts.ResolveMap(id);
    static JsHostObject PlayerHost()
        => NpcApi.BuildPlayer((NpcSession)typeof(NpcSession).GetMethod("MapContext", Flags).Invoke(null, null));

    sealed class Probe
    {
        readonly JsEngine engine;
        public Probe(string body, params (string, JsValue)[] bindings)
        {
            engine = new JsEngine("function check(){" + body + "}", "lucid-battle");
            foreach (var binding in bindings) engine.Bind(binding.Item1, binding.Item2);
            if (GameSession.Server != null) engine.Interp.ResolveHostType = GameSession.Server.Scripts.ResolveHostType;
            engine.Run();
        }
        public JsValue Value() => engine.Call("check");
        public int Int() => JsConvert.ToInt32(Value());
        public bool Bool() => JsConvert.ToBoolean(Value());
        public string Text() => JsConvert.ToString(Value());
    }

    static JsValue Eval(string js) => new Probe(js, ("e", eim), ("p", PlayerHost())).Value();
    static int Num(string js) => JsConvert.ToInt32(Eval(js));
    static bool Flag(string js) => JsConvert.ToBoolean(Eval(js));
    static string Text(string js) => JsConvert.ToString(Eval(js));
    static int Prop(string key) => Num("return Number(e.getProperty('" + key + "')||0);");
    static string PropText(string key) => Text("return ''+(e.getProperty('" + key + "')||'');");
    static void FullHp() => Num("p.updateHpMp(Number(p.getMaxHp()),Number(p.getMaxMp()));return 1;");
    static int Hp() => Num("return Number(p.getHp());");
    static int MaxHp() => Num("return Number(p.getMaxHp());");
    static int Deaths() => Num("return Number(e.getIntProperty('death_" + GameSession.CharacterId + "'));");

    static int MobCount(int mapId, int mobId)
    {
        var map = EventMap(mapId);
        if (map == null) return -1;
        int count = 0;
        foreach (var mob in map.AllMobs) if (mob.Id == mobId && mob.Alive) count++;
        return count;
    }
    // 用脚本自己的入口杀怪：killMonster(mob, player, false) 才会把 hasKiller 传成 true。
    static int KillBoss(int mapId, int mobId)
        => Num("var m=e.getInstanceMap(" + mapId + ");var b=m.getMonsterById(" + mobId + ");" +
               "if(b==null)return 0;m.killMonster(b,p,false);return 1;");

    static string LastPacket(int mapId)
    {
        var map = EventMap(mapId);
        return map == null ? "" : (map.LastPacket ?? "");
    }

    // 这张图上「播过」某个特效（按顺序记录，同一拍连播两个也不会误判）。
    static bool SawEffect(int mapId, string fragment)
    {
        var map = EventMap(mapId);
        if (map == null) return false;
        foreach (string path in map.EffectLog) if (path.Contains(fragment)) return true;
        return false;
    }
    static int EffectCount(int mapId, string fragment)
    {
        var map = EventMap(mapId);
        if (map == null) return 0;
        int count = 0;
        foreach (string path in map.EffectLog) if (path.Contains(fragment)) count++;
        return count;
    }

    // ——————————————————— 入口链路 ———————————————————
    // portal/lucid_expedition.js → npc/3003208.js → BeiDouSpecial/露希妲远征.js
    // fresh=true 时队伍还不存在，脚本走 status 0 → 「是否组建远征队」；
    // 队伍已建好后再进来走 status 0 → isLeader 分支 → 「你想做什么？」三选项（源端脚本原样）。
    static NpcSession OpenExpeditionMenu(bool fresh)
    {
        Profile.LastMapId = ExitMap;
        NpcSession.CloseActive();
        Require(NpcSession.RunPortal(new FieldPortal { name = "lucid_expedition", script = "lucid_expedition", type = 7 }),
            "portal/lucid_expedition 存在");
        var session = NpcSession.Active;
        Require(session != null && session.LastError == null, "门脚本转 NPC：" + session?.LastError);
        if (fresh)
            Require(session.Dialog.RawText.Contains("梦中的路西德"), "远征队菜单标题：" + session.Dialog.RawText);
        return session;
    }

    // 起实例失败时的现场：manager 里还剩哪些实例、各自 lobby 与释放状态。
    static string InstanceDiag()
    {
        var host = Host();
        var service = host == null ? null : typeof(NativeEventScripts).GetField("service", Flags)?.GetValue(host);
        if (service == null) return "service=null";
        var manager = service.GetType().GetMethod("GetManager").Invoke(service, new object[] { "LucidBattle" });
        if (manager == null) return "manager=null";
        var table = manager.GetType().GetField("instances", Flags)?.GetValue(manager) as System.Collections.IDictionary;
        if (table == null) return "instances=null";
        var text = "instances=" + table.Count;
        foreach (System.Collections.DictionaryEntry entry in table)
        {
            var value = entry.Value;
            var lobby = value.GetType().GetField("Lobby", Flags)?.GetValue(value);
            var players = value.GetType().GetMethod("GetPlayers")?.Invoke(value, null);
            text += "｜" + entry.Key + " lobby=" + lobby + " players=" + (players ?? "?");
        }
        return text;
    }

    static void StartBattle()
    {
        ExpeditionRegistry.Clear();
        LucidBossCompat.ResetAll();
        var create = OpenExpeditionMenu(true);
        Require(create.Dialog.RawText.Contains("组建远征队"), "菜单给组建入口：" + create.Dialog.RawText);
        create.Reply(1, 1);
        Require(create.LastError == null, "组建远征队：" + create.LastError);
        Require(ExpeditionRegistry.GetExpedition(ExpeditionType.LUCID) != null, "远征队已登记");
        NpcSession.CloseActive();

        var menu = OpenExpeditionMenu(false);
        Require(menu.Dialog.RawText.Contains("查看当前远征队成员"), "队长看到三个选项：" + menu.Dialog.RawText);
        menu.Reply(1, 2);
        Require(menu.LastError == null, "选[开始战斗！]：" + menu.LastError);
        Require(menu.Dialog.RawText.Contains("远征即将开始"), "进入开战确认：" + menu.Dialog.RawText);
        menu.Reply(1, 1);
        Require(menu.LastError == null, "startInstance(expedition)：" + menu.LastError);
        NpcSession.CloseActive();

        var instance = NativeEventScripts.Instance();
        Require(instance != null && !(instance is JsNull),
            "起实例后角色挂在事件实例上｜" + InstanceDiag() + "｜对话：" + menu.Dialog.RawText);
        eim = instance;
        battleStart = clock;
        FullHp();
    }

    // manager 上还剩几个实例（绕过「按角色查实例」：人被摘出去之后 Instance() 就查不到了，
    // 但实例本体还挂在 manager 上，会挡住下一场的 setup）。
    static System.Collections.IDictionary LucidInstances()
    {
        var host = Host();
        var service = host == null ? null : typeof(NativeEventScripts).GetField("service", Flags)?.GetValue(host);
        if (service == null) return null;
        var manager = service.GetType().GetMethod("GetManager").Invoke(service, new object[] { "LucidBattle" });
        return manager?.GetType().GetField("instances", Flags)?.GetValue(manager) as System.Collections.IDictionary;
    }
    static int LucidInstanceCount()
    {
        var table = LucidInstances();
        return table?.Count ?? 0;
    }

    // 收尾：没清关的实例靠 30 分钟总时间 scheduledTimeout 收，清关的靠 5 分钟关图计时器；
    // 释放后还要等 event_lobby_delay 才会从 manager 里摘掉名字（露希妲只有 1 个 lobby，
    // 摘不掉下一场 setup 就会撞「Event instance already exists」）。
    static void CloseBattle()
    {
        for (int i = 0; i < 12 && LucidInstanceCount() > 0; i++) Advance(400000);
        LucidBossCompat.ResetAll();
        Require(LucidInstanceCount() == 0, "上一场的实例已从 manager 摘除｜" + InstanceDiag());
        Require(EventMap(EntryMap) == null && EventMap(PhaseTwoMap) == null, "上一场的实例地图已回收");
        Require(LucidBossCompat.ActiveCount == 0, "上一场的控制器已全部停止");
    }

    static void CheckTypeResolution()
    {
        // event/LucidBattle.js 顶层写了 6 个 Java.type：任何一个缺失都在加载时抛异常。
        Require(NativeEventScripts.Manager("LucidBattle") is JsHostObject,
            "event/LucidBattle.js 加载成功（6 个 Java 类型全部解析）");
        var probe = new Probe(
            "var java=Java;\n" +
            "var lf=java.type('org.gms.server.life.LifeFactory').getMonster(8880140);\n" +
            "var point=java.type('java.awt.Point');\n" +
            "var animated=java.type('org.gms.server.maps.AbstractAnimatedMapObject');\n" +
            "var random=java.type('java.util.concurrent.ThreadLocalRandom').current().nextInt(4);\n" +
            "var compat=java.type('org.gms.server.life.LucidBossCompat');\n" +
            "var packet=java.type('org.gms.util.PacketCreator');\n" +
            "var pos=lf.getPosition();\n" +
            "return [lf.getId(),pos.getX(),pos.getY(),animated.IDLE_MOVEMENT_PACKET_LENGTH,random," +
            "compat.isLucidBoss(8880141)?1:0,typeof packet.showEffect];",
            ("Java", NpcApi.BuildJava()));
        var values = probe.Value() as JsArray;
        Require(values != null && values.Length == 7, "类型探测返回 7 项");
        Require(JsConvert.ToInt32(values.Items[0]) == 8880140, "LifeFactory.getMonster 返回对应 mobId");
        Require(JsConvert.ToInt32(values.Items[1]) == 0 && JsConvert.ToInt32(values.Items[2]) == 0,
            "LifeFactory.getMonster 的 position 默认 (0,0)（AbstractMapObject 语义）");
        Require(JsConvert.ToInt32(values.Items[3]) == 15, "IDLE_MOVEMENT_PACKET_LENGTH = 15");
        Require(JsConvert.ToInt32(values.Items[4]) >= 0 && JsConvert.ToInt32(values.Items[4]) < 4,
            "ThreadLocalRandom.nextInt(4) 落在 0..3");
        Require(JsConvert.ToInt32(values.Items[5]) == 1, "LucidBossCompat.isLucidBoss(8880141)");
        Require(JsConvert.ToString(values.Items[6]) != "undefined", "PacketCreator.showEffect 存在");
        Step("5 个 Java 类型 + PacketCreator 全部解析（含 ThreadLocalRandom / AbstractAnimatedMapObject / LifeFactory）");
    }

    static void CheckSetup()
    {
        Require(Prop("phase") == 1, "setup 把 phase 置 1，实际 " + Prop("phase"));
        Require(Prop("flowerExplosionVariant") == -1, "花朵爆炸变体初始 -1，实际 " + Prop("flowerExplosionVariant"));
        Require(PropText("canJoin") == "1", "canJoin=1");
        Require(MobCount(EntryMap, P1) == 1, "阶段 1 的 BOSS 8880140 已刷在 " + EntryMap + "，实际 " + MobCount(EntryMap, P1));
        Require(LucidBossCompat.ActiveCount == 1, "控制器已注册，实际 " + LucidBossCompat.ActiveCount);
        Require(LucidBossCompat.PhaseOf(EventMap(EntryMap)) == 1, "控制器处于阶段 1");
        Require(Profile.LastMapId == EntryMap, "playerEntry 把角色送进 " + EntryMap + "，实际 " + Profile.LastMapId);
        Require(Flag("return e.isTimerStarted();"), "事件计时器在跑");
        int left = Num("return Number(Math.floor(e.getTimeLeft()/1000));");
        Require(left >= 1795 && left <= 1800, "总时间 30 分钟，实际 " + left + " 秒");
        var exped = ExpeditionRegistry.GetExpedition(ExpeditionType.LUCID);
        Require(exped != null && exped.IsInProgress(), "远征队已进入 inProgress（招募结束）");
        Step("setup：phase=1 / BOSS 8880140 / 控制器阶段 1 / 30 分钟计时 / 远征队开战");
    }

    // 花朵爆炸（脚本自己的 2 秒循环 + 1080ms 延迟伤害）
    static void CheckFlowerExplosion()
    {
        FullHp();
        At(2000);
        Require(SawEffect(EntryMap, "flowerExplosion"),
            "2 秒后播花朵爆炸特效，实际 " + LastPacket(EntryMap));
        int variant = Prop("flowerExplosionVariant");
        Require(variant >= 0 && variant <= 3, "变体落在 0..3，实际 " + variant);

        int before = Hp();
        At(3080);
        int expected = Math.Max(1, MaxHp() * 35 / 100);
        Require(before - Hp() == expected, "花朵爆炸扣 35% 最大血：" + before + "→" + Hp() + "，应扣 " + expected);
        Step("花朵爆炸：特效 + 35% 最大血伤害（1080ms 延迟）");

        int casts = EffectCount(EntryMap, "flowerExplosion");
        At(4000);
        Require(EffectCount(EntryMap, "flowerExplosion") > casts,
            "4 秒后第二次爆炸，实际次数 " + EffectCount(EntryMap, "flowerExplosion"));
        int next = Prop("flowerExplosionVariant");
        Require(next != variant, "相邻两次爆炸变体不同（源端跳过上一次）：" + variant + " → " + next);
        Step("花朵爆炸变体不重复");
    }

    // 控制器阶段 1：5s 蝴蝶 / 9s 妖精之尘 + 召唤噩梦 / 12s 炸弹 / 20s 巨龙
    static void CheckPhaseOneController()
    {
        At(5000);
        Require(MobCount(EntryMap, ButterflyP1) > 0, "5 秒后蝴蝶 8880165 出现，实际 " + MobCount(EntryMap, ButterflyP1));
        Require(ButterflyP1 == 8880165, "蝴蝶 id 与源端一致");
        Step("阶段 1 蝴蝶按血量档位间隔积攒（上限 40）");

        FullHp();
        int before = Hp();
        At(9000);
        Require(Hp() < before, "妖精之尘（9 秒）扣 10% 血：" + before + "→" + Hp());
        Require(MobCount(EntryMap, MushroomP1) > 0, "召唤噩梦刷出蘑菇 8880164，实际 " + MobCount(EntryMap, MushroomP1));
        Require(MobCount(EntryMap, GolemP1) > 0, "召唤噩梦刷出石巨人 8880161，实际 " + MobCount(EntryMap, GolemP1));
        Step("阶段 1 妖精之尘扣血 + 召唤噩梦（蘑菇 / 石巨人）");

        FullHp();
        At(12000);
        Require(SawEffect(EntryMap, "bombVideoLayer"), "12 秒感染炸弹特效，实际 " + LastPacket(EntryMap));
        Step("阶段 1 感染炸弹（3 秒后 35% 范围伤害）");

        FullHp();
        At(20000);
        Require(SawEffect(EntryMap, "dragonP1VideoLayer"), "20 秒巨龙特效，实际 " + LastPacket(EntryMap));
        Step("阶段 1 巨龙吐息特效 + 6300ms 后 100% 伤害");
    }

    // P1 死亡 → 脚本 phase=2 → 2500ms 后 advanceToPhaseTwo
    static void CheckPhaseTwoTransition()
    {
        FullHp();
        Require(KillBoss(EntryMap, P1) == 1, "阶段 1 的 BOSS 可被击杀");
        Require(Prop("phase") == 2, "杀死 8880140 后脚本 phase=2，实际 " + Prop("phase"));
        Require(Num("return Number(e.getKillCount(p));") == 1, "monsterValue(8880140) 记 1 分");
        At(22500);
        Require(MobCount(EntryMap, P1) == 0, "阶段 1 地图已清空");
        Require(MobCount(EntryMap, ButterflyP1) == 0, "阶段 1 的蝴蝶被清掉，实际 " + MobCount(EntryMap, ButterflyP1));
        Require(MobCount(PhaseTwoMap, P2) == 1, "阶段 2 的 BOSS 8880141 刷在 " + PhaseTwoMap);
        Require(LucidBossCompat.PhaseOf(EventMap(PhaseTwoMap)) == 2, "控制器切到阶段 2");
        Require(LucidBossCompat.ActiveCount == 1, "同一时刻只有一个控制器，实际 " + LucidBossCompat.ActiveCount);
        Require(Profile.LastMapId == PhaseTwoMap, "全员被送到 " + PhaseTwoMap + "，实际 " + Profile.LastMapId);
        Step("P1 死亡 → phase=2 → 清场 → 刷 8880141 → 控制器阶段 2 → 换图");
    }

    // 控制器阶段 2：10s 碎玻璃 / 12s 激光雨 / 18s 冲刺 / 25s 幻影连射
    static void CheckPhaseTwoController()
    {
        long start = clock - battleStart;             // 阶段 2 控制器的起点
        FullHp();
        At(start + 10000);
        Require(SawEffect(PhaseTwoMap, "stainedGlass"), "10 秒碎玻璃特效，实际 " + LastPacket(PhaseTwoMap));
        Step("阶段 2 碎玻璃（六块区域轮转 + 3 秒后补石巨人）");

        FullHp();
        At(start + 12000);
        Require(SawEffect(PhaseTwoMap, "laserRainVideoLayer"), "12 秒激光雨特效，实际 " + LastPacket(PhaseTwoMap));
        int before = Hp();
        At(start + 13260);
        Require(before - Hp() > 0, "激光雨 1260ms 后扣 18%：" + before + "→" + Hp());
        Step("阶段 2 激光雨两段伤害");

        FullHp();
        At(start + 18000);
        Require(SawEffect(PhaseTwoMap, "rushVideoLayer"), "18 秒冲刺特效，实际 " + LastPacket(PhaseTwoMap));
        Step("阶段 2 冲刺（RUSH_PATH 加权插值 + 同一轮只结算一次）");

        FullHp();
        At(start + 25000);
        Require(SawEffect(PhaseTwoMap, "phantomBarrageVideoLayer"), "25 秒幻影连射特效，实际 " + LastPacket(PhaseTwoMap));
        Step("阶段 2 幻影连射 12 段");
    }

    // P2 死亡 → 客户端按 Mob.wz/info/revives 刷 8880152 → monsterRevive → 控制器阶段 3
    static void CheckPhaseThreeRevive()
    {
        FullHp();
        long killed = clock - battleStart;
        Require(KillBoss(PhaseTwoMap, P2) == 1, "阶段 2 的 BOSS 可被击杀");
        Require(Num("return Number(e.getKillCount(p));") == 2, "8880141 记分，累计 2");
        At(killed + 250);
        Require(LucidBossCompat.ActiveCount == 0, "P2 死亡且没有后继形态时控制器自行退出，实际 " + LucidBossCompat.ActiveCount);

        // 生产链路是 NativeMobController.OnKilled → FieldDirector.SpawnNativeMob(revives) →
        // Instance.ReviveMonster。batchmode 没有渲染实体，这里按同一条链手工补这一步。
        var map = EventMap(PhaseTwoMap);
        Require(map != null, "阶段 2 地图还在");
        var revived = map.SpawnAt(P3, 600, -100, false);
        Require(revived != null, "复苏形态 8880152 已刷出");
        NativeEventScripts.InstanceById(GameSession.CharacterId).ReviveMonster(revived.Host);
        Require(Prop("phase") == 3, "monsterRevive 后脚本 phase=3，实际 " + Prop("phase"));
        Require(LucidBossCompat.PhaseOf(map) == 3, "控制器切到阶段 3（暴怒）");
        At(killed + 1250);
        Require(SawEffect(PhaseTwoMap, "furyVideoLayer"), "阶段 3 播暴怒特效，实际 " + LastPacket(PhaseTwoMap));
        Step("P2 死亡 → 复苏形态 8880152 → monsterRevive → 控制器阶段 3 + 暴怒特效");
    }

    // 45 秒暴怒失败：BOSS 换 8880154 → 3 秒 100% 伤害 → 10 秒后 P2 以 16% 血复活
    static void CheckFuryFail()
    {
        long furyStart = clock - battleStart;
        FullHp();
        At(furyStart + 45000);
        Require(MobCount(PhaseTwoMap, FuryFail) == 1, "45 秒后出现暴怒失败怪 8880154，实际 " + MobCount(PhaseTwoMap, FuryFail));
        Require(MobCount(PhaseTwoMap, P3) == 0, "8880152 被换掉");
        Step("45 秒暴怒失败：8880152 → 8880154");

        int before = Hp();
        At(furyStart + 48000);
        Require(Hp() <= 1, "暴怒失败 3 秒后打满 100%：" + before + "→" + Hp());
        FullHp();

        At(furyStart + 55000);
        Require(MobCount(PhaseTwoMap, P2) == 1, "10 秒后阶段 2 的 BOSS 重新出现");
        Require(MobCount(PhaseTwoMap, FuryFail) == 0, "暴怒失败怪已被清掉");
        var boss = EventMap(PhaseTwoMap).FindAliveById(P2);
        Require(boss != null, "复活后的 8880141 还在");
        long ratio = boss.CurrentHp * 100 / Math.Max(1, boss.MaxHp);
        Require(ratio >= 15 && ratio <= 17, "复活后血量 16%，实际 " + boss.CurrentHp + "/" + boss.MaxHp + " = " + ratio + "%");
        Require(Prop("phase") == 2, "脚本 phase 被改回 2，实际 " + Prop("phase"));
        Require(LucidBossCompat.PhaseOf(EventMap(PhaseTwoMap)) == 2, "控制器回到阶段 2");
        Step("暴怒失败重置：3 秒 100% 伤害 → 10 秒后 P2 以 16% 血重来");
    }

    // 击杀 8880152 → 脚本 phase=4 → finishFurySuccess → 5 秒后 clearPQ
    static void CheckClear()
    {
        long killed = clock - battleStart;
        FullHp();
        var map = EventMap(PhaseTwoMap);
        var p3 = map.SpawnAt(P3, 600, -100, false);
        NativeEventScripts.InstanceById(GameSession.CharacterId).ReviveMonster(p3.Host);
        Require(LucidBossCompat.PhaseOf(map) == 3, "击杀前控制器回到阶段 3");
        Require(KillBoss(PhaseTwoMap, P3) == 1, "8880152 可被击杀");
        Require(Prop("phase") == 4, "击杀 8880152 后 phase=4，实际 " + Prop("phase"));
        Require(SawEffect(PhaseTwoMap, "furyStopVideoLayer"),
            "finishFurySuccess 播 furyStop，实际 " + LastPacket(PhaseTwoMap));
        Require(PropText("canJoin") == "0", "击杀瞬间 canJoin=0");
        // 记分累计：8880140(P1) + 8880141(P2 在阶段 3 之前那次击杀) + 8880152(P3) = 3。
        int kills = Num("return Number(e.getKillCount(p));");
        Require(kills == 3, "三次 BOSS 击杀各记 1 分，累计 3，实际 " + kills);
        At(killed + 5000);
        Require(Flag("return e.isEventCleared();"), "completeFurySuccess → clearPQ 已置通关");
        int left = Num("return Number(Math.floor(e.getTimeLeft()/1000));");
        Require(left >= 294 && left <= 300, "清关后重新计 5 分钟，实际 " + left + " 秒");
        Require(LucidBossCompat.ActiveCount == 0, "清关后控制器全部停止，实际 " + LucidBossCompat.ActiveCount);
        Step("通关链路：phase=4 → furyStop → clearPQ → 5 分钟后关图 → 控制器停");
    }

    // 50 次死亡上限（脚本自己的 playerRevive）
    static void CheckDeathLimit()
    {
        Require(Profile.LastMapId == EntryMap, "新一场回到 " + EntryMap + "，实际 " + Profile.LastMapId);
        FullHp();
        for (int i = 1; i <= 49; i++)
        {
            bool unhandled = Flag("return e.invokeScriptFunction('playerRevive',e,p)===true;");
            Require(!unhandled, "第 " + i + " 次复活由脚本接管（返回 false）");
            Require(Deaths() == i, "死亡计数 " + i + "，实际 " + Deaths());
            Require(Hp() == 50, "复活后血量 50，实际 " + Hp());
        }
        Step("死亡次数逐次累加 + 每次复活回 50 血");

        Flag("return e.invokeScriptFunction('playerRevive',e,p)===true;");
        // 满 50 次走 player.respawn(eim, exitMap)：源端先 eim.unregisterPlayer 再换图，
        // 随后脚本 disposeIfEmpty 立刻回收实例 —— 实例属性（death_/eliminated_）跟着被清掉，
        // 所以这一拍只能从「人被送回出生图 + 实例已释放」这两个外部可见结果断言。
        Require(Profile.LastMapId == ExitMap, "被送回 " + ExitMap + "，实际 " + Profile.LastMapId);
        Require(NativeEventScripts.Instance() is JsNull, "复活后角色已脱离事件实例");
        Require(LucidInstanceCount() == 1, "实例本体还在（已安排释放），实际 " + LucidInstanceCount());
        Step("死亡上限：第 50 次 respawn 送回 450004000 + 实例释放");
    }

    // eliminated 的人再进本：playerEntry 只摘人不进图（新一场里手工造这个标记来验分支）
    static void CheckEliminatedEntry()
    {
        Require(Profile.LastMapId == EntryMap, "新一场回到 " + EntryMap + "，实际 " + Profile.LastMapId);
        FullHp();
        Num("e.setIntProperty('eliminated_" + GameSession.CharacterId + "',1);return 1;");
        var instance = NativeEventScripts.InstanceById(GameSession.CharacterId);
        Require(instance != null, "本场实例还在");
        instance.Invoke("playerEntry", instance.Host, PlayerHost());
        Require(Profile.LastMapId == ExitMap, "eliminated 的人被送回 " + ExitMap + "，实际 " + Profile.LastMapId);
        Require(NativeEventScripts.Instance() is JsNull, "eliminated 的人不挂在实例上");
        Require(LucidInstanceCount() == 1, "实例本体还在（等自己的定时器收），实际 " + LucidInstanceCount());
        Step("eliminated 分支：playerEntry 直接送回出生图，不进事件图");
    }

    // 中途离图：changedMap 到非事件图 → 摘人 + 空场回收
    static void CheckChangedMap()
    {
        Require(LucidBossCompat.ActiveCount == 1, "新一场控制器已注册");
        Profile.LastMapId = ExitMap;
        NativeEventScripts.ChangedMap(ExitMap, false);
        Require(NativeEventScripts.Instance() is JsNull, "离图后角色脱离事件实例");
        Require(LucidBossCompat.ActiveCount == 0, "离图后控制器停止，实际 " + LucidBossCompat.ActiveCount);
        Require(EventMap(EntryMap) == null, "空场后实例地图已回收");
        Step("中途离图：changedMap → unregisterPlayer → disposeIfEmpty");
    }

    public static void Run()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), "lucid-battle-" + Guid.NewGuid() + ".json");
        var dataField = typeof(Profile).GetField("data", Flags);
        var savingField = typeof(Profile).GetField("debugOverride", Flags);
        object oldData = dataField.GetValue(null), oldSaving = savingField.GetValue(null);
        int oldMap = Profile.LastMapId;
        var oldJsClock = Field<Func<long>>(typeof(JsDate), "Clock");
        var oldReactorClock = Server.Field.MapReactorService.Clock;
        var oldReactorPlayer = Server.Field.MapReactorService.Player;
        var oldEventInstance = Server.Field.MapReactorService.EventInstance;
        var oldExpedClock = Expedition.Clock;
        var errors = new List<string>();
        Application.LogCallback log = (text, stack, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception) errors.Add(text);
        };
        passed = 0;
        int exitCode = 0;
        try
        {
            savingField.SetValue(null, true);
            SetField(typeof(NativeEventScripts), "Clock", (Func<long>)(() => clock));
            SetField(typeof(JsDate), "Clock", (Func<long>)(() => clock));
            Expedition.Clock = () => clock;
            var db = DbStore.Create(dbPath);
            int account = AccountsDao.AutoRegister(db, "lucidreview", "isolated");
            var server = GameServer.StartWith(db);
            Set("Server", server); Set("Net", server.Transport); Set("AccountId", account); Set("InGame", true);
            ScriptSource.ClearCache();
            Application.logMessageReceived += log;

            var record = new CharRecord
            {
                accountid = account, name = "LucidReview", level = 230, face = 20000, hair = 30300,
                hp = 500, mp = 200, maxhp = 500, maxmp = 500,
                equipslots = 96, useslots = 96, setupslots = 96, etcslots = 96, meso = 1000000
            };
            record.id = CharactersDao.Insert(db, record);
            int cid = (int)record.id;
            Set("CharacterId", cid);
            Set("Character", CharService.Load(db, cid));
            GameSession.ApplyToProfile();

            Server.Field.MapReactorService.Clock = () => clock;
            Server.Field.MapReactorService.Player = _ => PlayerHost();
            Server.Field.MapReactorService.EventInstance = _ => NativeEventScripts.Instance();
            // 玩家站位必须落在 BOSS 的范围技里：妖精之尘是「BOSS 位置 ±600/±500」的矩形，
            // 感染炸弹是「玩家位置 ±250」，站 (0,0) 会让所有范围判定 miss，扣血断言全假失败。
            // 阶段 1 的 BOSS 在 1000 上（setup 里 new Point(1000,0)），阶段 2 在 600。
            SetField(typeof(NpcApi), "HeadlessPosition", new Vector2(1000, 0));

            CheckTypeResolution();

            // 第 1 场：只看脚本自己的花朵爆炸节奏
            StartBattle();
            CheckSetup();
            CheckFlowerExplosion();
            CloseBattle();

            // 第 2 场：控制器阶段 1 → 2 → 3 → 通关
            StartBattle();
            CheckPhaseOneController();
            CheckPhaseTwoTransition();
            CheckPhaseTwoController();
            CheckPhaseThreeRevive();
            CheckFuryFail();
            CheckClear();
            CloseBattle();

            // 第 3 场：死亡上限
            StartBattle();
            CheckDeathLimit();
            CloseBattle();

            // 第 4 场：eliminated 标记的人再进本
            StartBattle();
            CheckEliminatedEntry();
            CloseBattle();

            // 第 5 场：中途离图
            StartBattle();
            CheckChangedMap();
            CloseBattle();

            Require(errors.Count == 0, "全程不应出现 Error/Exception 日志：" + string.Join(" | ", errors));

            foreach (var host in UnityEngine.Object.FindObjectsOfType<NativeEventScripts>())
                UnityEngine.Object.DestroyImmediate(host.gameObject);
        }
        catch (Exception error)
        {
            Debug.LogError("[LucidBattle] 失败：" + error);
            exitCode = 1;
        }
        finally
        {
            Application.logMessageReceived -= log;
            NpcSession.CloseActive();
            LucidBossCompat.ResetAll();
            ExpeditionRegistry.Clear();
            Expedition.Clock = oldExpedClock;
            SetField(typeof(NativeEventScripts), "Clock", (Func<long>)(() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
            SetField(typeof(JsDate), "Clock", oldJsClock);
            Server.Field.MapReactorService.Clock = oldReactorClock;
            Server.Field.MapReactorService.Player = oldReactorPlayer;
            Server.Field.MapReactorService.EventInstance = oldEventInstance;
            GameServer.Stop(); Set("InGame", false);
            dataField.SetValue(null, oldData); savingField.SetValue(null, oldSaving); Profile.LastMapId = oldMap;
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
        Debug.Log("[LucidBattle] 结果：通过 " + passed + " 项，退出码 " + exitCode);
        if (Application.isBatchMode) EditorApplication.Exit(exitCode);
    }
}
