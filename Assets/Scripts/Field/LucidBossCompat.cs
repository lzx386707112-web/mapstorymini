using System;
using System.Collections.Generic;
using MapleDash.Js;
using UnityEngine;

// org.gms.server.life.LucidBossCompat 的移植层（逐条对齐 LucidBossCompat.java）。
//
// 源端这个类的定位不是「玩法实现」，而是一组**服务端表现控制器**：阶段推进、蝴蝶积攒、
// 妖精之尘、召唤噩梦、巨龙吐息、激光雨、幻影连射、冲刺、碎玻璃、障碍伤害、感染炸弹、
// 45 秒暴怒失败重置。规则全部来自源端常量，事件脚本 event/LucidBattle.js 仍然负责
// 进本 / 死亡次数 / 掉线 / 清关这些流程；C# 这边只补脚本拿不到的服务器能力：
//   startPhase / stop / finishFurySuccess / protectFurySuccessMob
//
// 时钟：源端是 TimerManager.register(this, 250ms)。移植端不自己起线程，由
// NativeEventScripts.Update() 每帧调 Tick(now)，回归用可注入时钟推进（见 TickNow）。
public static class LucidBossCompat
{
    public const int PhaseOne = 1, PhaseTwo = 2, PhaseThree = 3;
    public const int LucidP1 = 8880140, LucidP2 = 8880141, LucidP3 = 8880152;
    public const int FurySuccess = 8880153, FuryFail = 8880154;
    const int MushroomP1 = 8880164, GolemP1 = 8880161, ButterflyP1 = 8880165;
    const int GolemP2 = 8880171, ButterflyP2 = 8880175;

    const int ButterflyCapacity = 40, MaxGolems = 15, MaxMushrooms = 4;
    const long ControllerTickMs = 250;
    const long FuryLimitMs = 45_000;
    const long FuryFailImpactDelayMs = 3000;
    const long FuryFailResummonDelayMs = 10_000;
    const int FuryResummonHpPercent = 16;
    const long DragonBreathDamageMs = 6300;
    const long ButterflyBurstImpactMs = 1800;
    const long RushDurationMs = 3000;
    const long RushHitIntervalMs = 100;
    const int PhantomBarragePrepareMs = 2400, PhantomBarrageHitIntervalMs = 1000, PhantomBarrageHitCount = 12;

    const string EffectRoot = "customSkill/lucid/";
    const string DragonP1Effect = EffectRoot + "dragonP1VideoLayer";
    const string DragonP2Effect = EffectRoot + "dragonP2VideoLayer";
    const string LaserRainEffect = EffectRoot + "laserRainVideoLayer";
    const string PhantomBarrageEffect = EffectRoot + "phantomBarrageVideoLayer";
    const string RushEffect = EffectRoot + "rushVideoLayer";
    const string FuryEffect = EffectRoot + "furyVideoLayer";
    const string FuryStopEffect = EffectRoot + "furyStopVideoLayer";
    const string ButterflyBurstEffect = EffectRoot + "butterflyBurstVideoLayer";
    const string BombEffect = EffectRoot + "bombVideoLayer";
    static readonly string[] StainedGlassEffects =
    {
        EffectRoot + "stainedGlassVideoLayer", EffectRoot + "stainedGlass1VideoLayer",
        EffectRoot + "stainedGlass2VideoLayer", EffectRoot + "stainedGlass3VideoLayer",
        EffectRoot + "stainedGlass4VideoLayer", EffectRoot + "stainedGlass5VideoLayer",
    };

    static readonly HashSet<int> SupportMobs = new HashSet<int>
        { MushroomP1, GolemP1, ButterflyP1, GolemP2, ButterflyP2 };
    static readonly HashSet<int> FuryTransitionMobs = new HashSet<int> { FurySuccess, FuryFail };

    // 每张实例地图一份（源端 ConcurrentMap<MapleMap, Encounter>）。
    static readonly Dictionary<NativeEventMap, Encounter> encounters = new Dictionary<NativeEventMap, Encounter>();

    public const string TypeName = "org.gms.server.life.LucidBossCompat";

    public static bool IsLucidBoss(int mobId) => mobId == LucidP1 || mobId == LucidP2 || mobId == LucidP3;

    // LucidBossCompat.butterflyIntervalMillis / butterflyCreateCount（按 BOSS 血量百分比）。
    public static int ButterflyIntervalMillis(int hpPercent)
        => hpPercent >= 90 ? 5000 : hpPercent >= 70 ? 4500 : hpPercent >= 50 ? 4000 : hpPercent >= 20 ? 3000 : 2000;
    public static int ButterflyCreateCount(int hpPercent)
        => hpPercent >= 90 ? 5 : hpPercent >= 70 ? 7 : hpPercent >= 50 ? 10 : hpPercent >= 20 ? 15 : 20;

    // ————— 脚本面（Java.type("org.gms.server.life.LucidBossCompat")） —————

    public static JsValue Resolve(string name) => name == TypeName ? Type() : null;

    static JsValue Type()
    {
        var type = new JsHostObject(null);
        type.Missing = key => throw new JsRuntimeException("Unmigrated LucidBossCompat API: " + key);
        type.Set("startPhase", JsFunction.FromNative("startPhase", c =>
        {
            var map = Map(c.Arg(0));
            var boss = Mob(c.Arg(1));
            int phase = JsConvert.ToInt32(c.Arg(2));
            if (map == null || boss == null || !IsLucidBoss(boss.Id))
                throw new JsRuntimeException("invalid Lucid encounter phase");
            int expected = phase == PhaseOne ? LucidP1 : phase == PhaseTwo ? LucidP2 : phase == PhaseThree ? LucidP3 : -1;
            if (boss.Id != expected)
                throw new JsRuntimeException("Lucid phase and boss ID do not match");
            StartPhase(map, boss, phase);
            return JsUndefined.Value;
        }));
        type.Set("stop", JsFunction.FromNative("stop", c => { Stop(Map(c.Arg(0))); return JsUndefined.Value; }));
        type.Set("finishFurySuccess", JsFunction.FromNative("finishFurySuccess",
            c => { FinishFurySuccess(Map(c.Arg(0))); return JsUndefined.Value; }));
        type.Set("protectFurySuccessMob", JsFunction.FromNative("protectFurySuccessMob",
            c => { ProtectFurySuccessMob(Mob(c.Arg(0))); return JsUndefined.Value; }));
        type.Set("isLucidBoss", JsFunction.FromNative("isLucidBoss",
            c => JsBoolean.Of(IsLucidBoss(JsConvert.ToInt32(c.Arg(0))))));
        return type;
    }

    static NativeEventMap Map(JsValue value) => (value as JsHostObject)?.Host as NativeEventMap;
    static NativeEventMap.Mob Mob(JsValue value) => NativeEventMap.FindByHost(value);

    // ————— 生命周期 —————

    public static void StartPhase(NativeEventMap map, NativeEventMap.Mob boss, int phase)
    {
        if (map == null || boss == null) return;
        if (phase == PhaseThree) CleanupSupportMobs(map);
        var replacement = new Encounter(map, boss, phase);
        Encounter previous;
        if (encounters.TryGetValue(map, out previous)) { encounters[map] = replacement; previous.StopInternal(false); }
        else encounters.Add(map, replacement);
        replacement.Start();
    }

    public static void Stop(NativeEventMap map)
    {
        if (map == null) return;
        Encounter encounter;
        if (encounters.TryGetValue(map, out encounter))
        {
            encounters.Remove(map);
            encounter.StopInternal(true);
        }
        else CleanupAuxiliaryMobs(map);
    }

    public static void FinishFurySuccess(NativeEventMap map)
    {
        if (map == null) return;
        Encounter encounter;
        if (encounters.TryGetValue(map, out encounter))
        {
            encounters.Remove(map);
            encounter.StopInternal(false);
        }
        map.BroadcastEffect(FuryStopEffect);
    }

    public static void ProtectFurySuccessMob(NativeEventMap.Mob mob)
    {
        if (mob != null && mob.Id == FurySuccess)
            mob.BlockDamageUntil(Now() + 5000);
    }

    static long Now() => NativeEventScripts.Clock();

    // 源端 Encounter 是 TimerManager 的周期任务（250ms）。移植端由宿主每帧推进。
    public static void Tick(long now)
    {
        if (encounters.Count == 0) return;
        List<Encounter> snapshot = null;
        foreach (var encounter in encounters.Values)
        {
            (snapshot ?? (snapshot = new List<Encounter>())).Add(encounter);
        }
        for (int i = 0; i < snapshot.Count; i++) snapshot[i].Tick(now);
    }

    // 编辑器回归：清掉所有控制器，避免跨用例串状态。
    public static void ResetAll()
    {
        encounters.Clear();
        NativeScriptEffect.Reset();
    }

    public static int ActiveCount => encounters.Count;
    public static int PhaseOf(NativeEventMap map)
        => encounters.TryGetValue(map, out var encounter) ? encounter.Phase : 0;

    static void CleanupSupportMobs(NativeEventMap map) => KillMatching(map, id => SupportMobs.Contains(id));
    static void CleanupAuxiliaryMobs(NativeEventMap map)
        => KillMatching(map, id => SupportMobs.Contains(id) || FuryTransitionMobs.Contains(id));

    static void KillMatching(NativeEventMap map, Func<int, bool> matches)
    {
        foreach (var mob in map.AllMobs)
            if (matches(mob.Id)) map.Kill(mob);
    }

    // rushPositionAt（源端按路径长度/速度加权插值）。
    public static Vector2 RushPositionAt(long elapsedMillis)
    {
        var path = LucidTables.RushPath;
        var speeds = LucidTables.RushSpeeds;
        var weights = new double[path.Length - 1];
        double total = 0;
        for (int i = 1; i < path.Length; i++)
        {
            double weight = Vector2.Distance(path[i - 1], path[i]) / Math.Max(1, speeds[i]);
            weights[i - 1] = weight;
            total += weight;
        }
        double target = Math.Max(0, Math.Min(RushDurationMs, elapsedMillis)) * total / RushDurationMs;
        double consumed = 0;
        for (int i = 1; i < path.Length; i++)
        {
            double segment = weights[i - 1];
            if (target <= consumed + segment || i == path.Length - 1)
            {
                double progress = segment == 0 ? 1 : (target - consumed) / segment;
                progress = Math.Max(0, Math.Min(1, progress));
                Vector2 start = path[i - 1], end = path[i];
                return new Vector2(
                    (float)Math.Round(start.x + (end.x - start.x) * progress),
                    (float)Math.Round(start.y + (end.y - start.y) * progress));
            }
            consumed += segment;
        }
        return path[path.Length - 1];
    }

    sealed class Encounter
    {
        readonly NativeEventMap map;
        readonly NativeEventMap.Mob boss;
        readonly int phase, bossObjectId;
        bool active = true, furyFailed;
        long nextButterfly, nextDust, nextSummon, nextDragon, nextLaser, nextShoot, nextRush, nextBomb;
        long nextStainedGlass, nextHurdleDamage, nextFury;
        int butterflyGauge, butterflyPosition, stainedGlassIndex;
        bool butterflyWarningIssued;

        // 源端 TimerManager.getInstance().schedule(..., delay) 的移植：一次性延迟任务。
        readonly List<KeyValuePair<long, Action>> jobs = new List<KeyValuePair<long, Action>>();

        public int Phase => phase;

        public Encounter(NativeEventMap map, NativeEventMap.Mob boss, int phase)
        {
            this.map = map; this.boss = boss; this.phase = phase; bossObjectId = boss.ObjectId;
            long now = Now();
            nextButterfly = now + 5000;
            nextDust = now + 9000;
            nextSummon = now + 7000;
            nextDragon = now + (phase == PhaseOne ? 20_000 : 28_000);
            nextLaser = now + 12_000;
            nextShoot = now + 25_000;
            nextRush = now + 18_000;
            nextBomb = now + (phase == PhaseOne ? 12_000 : 15_000);
            nextStainedGlass = now + 10_000;
            nextHurdleDamage = now + 1000;
            nextFury = now + FuryLimitMs;
        }

        public void Start()
        {
            if (phase == PhaseThree)
            {
                map.BroadcastEffect(FuryEffect);
                map.DropMapMessage(5, "[Lucid] Lucid has become enraged!");
                map.DropMapMessage(5, "[Lucid] The nightmare fog will close in after 45 seconds.");
            }
        }

        void Schedule(long delay, Action action) => jobs.Add(new KeyValuePair<long, Action>(Now() + delay, action));

        public void Tick(long now)
        {
            if (!active) return;
            RunJobs(now);
            if (!active) return;
            if (!IsCurrentBossAlive())
            {
                if (phase == PhaseThree && furyFailed) return;
                if (phase == PhaseTwo)
                {
                    var phaseThree = map.FindAliveById(LucidP3);
                    if (phaseThree != null) { StartPhase(map, phaseThree, PhaseThree); return; }
                }
                if (encounters.TryGetValue(map, out var current) && ReferenceEquals(current, this)) encounters.Remove(map);
                StopInternal(false);
                return;
            }
            if (phase == PhaseThree)
            {
                if (now >= nextFury) FailFury();
                return;
            }
            int hpPercent = HpPercent();
            if (now >= nextButterfly) CreateButterflies(now, hpPercent);
            if (now >= nextDust)
            {
                var position = boss.Position;
                ApplyDamage(10, new Rect(position.x - 600, position.y - 500, 1200, 650), "fairy-dust", boss);
                nextDust = now + (phase == PhaseOne ? 12_000 : 10_000);
            }
            if (now >= nextSummon) { SummonNightmares(); nextSummon = now + (phase == PhaseOne ? 15_000 : 18_000); }
            if (now >= nextDragon)
            {
                map.DropMapMessage(5, "[Lucid] Lucid is preparing a powerful attack!");
                map.BroadcastEffect(phase == PhaseOne ? DragonP1Effect : DragonP2Effect);
                Schedule(DragonBreathDamageMs, () => { if (active && IsCurrentBossAlive()) ApplyDamage(100, null, "dragon-breath", boss); });
                nextDragon = now + (phase == PhaseOne ? 35_000 : 38_000);
            }
            if (phase == PhaseTwo && now >= nextLaser)
            {
                map.DropMapMessage(5, "[Lucid] Lucid is preparing a powerful attack!");
                map.BroadcastEffect(LaserRainEffect);
                ScheduleDamage(1260, 18, null, "laser-rain-1");
                ScheduleDamage(3000, 18, null, "laser-rain-2");
                Schedule(3000, () =>
                {
                    if (active && phase == PhaseTwo && IsCurrentBossAlive() && CountMobs(GolemP2) < MaxGolems)
                        SpawnGroundMob(GolemP2, RandomGroundPoint(PhaseTwo));
                });
                nextLaser = now + 30_000;
            }
            if (phase == PhaseTwo && now >= nextShoot)
            {
                map.DropMapMessage(5, "[Lucid] Lucid is gathering power!");
                map.BroadcastEffect(PhantomBarrageEffect);
                for (int index = 0; index < PhantomBarrageHitCount; index++)
                    ScheduleDamage(PhantomBarragePrepareMs + (long)index * PhantomBarrageHitIntervalMs, 10, null, "phantom-barrage");
                nextShoot = now + 45_000;
            }
            if (phase == PhaseTwo && now >= nextRush) { CastRush(); nextRush = now + 20_000; }
            if (phase == PhaseTwo && now >= nextStainedGlass) { BreakStainedGlass(); nextStainedGlass = now + 10_000; }
            if (now >= nextHurdleDamage) { ApplyHurdleDamage(); nextHurdleDamage = now + 1000; }
            if (now >= nextBomb) { CastContagiousBomb(); nextBomb = now + 25_000; }
        }

        void RunJobs(long now)
        {
            if (jobs.Count == 0) return;
            for (int index = jobs.Count - 1; index >= 0; index--)
            {
                if (jobs[index].Key > now) continue;
                var action = jobs[index].Value;
                jobs.RemoveAt(index);
                if (active) action();
            }
        }

        bool IsCurrentBossAlive()
            => boss.Alive && ReferenceEquals(map.FindByOid(bossObjectId), boss);

        int HpPercent()
        {
            long max = Math.Max(1, boss.MaxHp);
            long hp = boss.CurrentHp;
            long percent = (long)Math.Ceiling(hp * 100.0 / max);
            return (int)Math.Max(1, Math.Min(100, percent));
        }

        void CreateButterflies(long now, int hpPercent)
        {
            int created = Math.Min(ButterflyCreateCount(hpPercent), ButterflyCapacity - butterflyGauge);
            butterflyGauge += created;
            if (!butterflyWarningIssued && butterflyGauge >= ButterflyCapacity / 2)
            {
                butterflyWarningIssued = true;
                map.DropMapMessage(5, "[Lucid] The dream is growing stronger. Be careful!");
            }
            var positions = phase == PhaseOne ? LucidTables.ButterflyP1 : LucidTables.ButterflyP2;
            int butterflyId = phase == PhaseOne ? ButterflyP1 : ButterflyP2;
            int visible = CountMobs(butterflyId);
            int toSpawn = Math.Min(created, ButterflyCapacity - visible);
            for (int index = 0; index < toSpawn; index++)
            {
                var position = positions[butterflyPosition++ % positions.Length];
                SpawnAt(butterflyId, position, true);
            }
            if (butterflyGauge >= ButterflyCapacity)
            {
                butterflyGauge = 0;
                butterflyWarningIssued = false;
                map.DropMapMessage(5, "[Lucid] Lucid has become enraged!");
                KillMatching(map, id => id == ButterflyP1 || id == ButterflyP2);
                map.BroadcastEffect(ButterflyBurstEffect);
                ScheduleDamage(ButterflyBurstImpactMs, 30, null, "butterfly-burst");
            }
            nextButterfly = now + ButterflyIntervalMillis(hpPercent);
        }

        // —— 45 秒暴怒失败：BOSS 换成 8880154，3 秒后打 100%，10 秒后 P2 以 16% 血复活 ——
        void FailFury()
        {
            if (!active || furyFailed || !IsCurrentBossAlive()) return;
            furyFailed = true;
            var position = boss.Position;
            int foothold = boss.Fh;
            map.Kill(boss);

            var fail = map.SpawnAt(FuryFail, position.x, position.y, false);
            if (fail != null)
            {
                fail.Fh = foothold;
                fail.BlockDamageUntil(Now() + FuryFailResummonDelayMs);
            }
            Schedule(FuryFailImpactDelayMs, () =>
            {
                if (active && furyFailed && fail != null && ReferenceEquals(map.FindByOid(fail.ObjectId), fail))
                    ApplyDamage(100, null, "fury-fail", fail);
            });
            Schedule(FuryFailResummonDelayMs, () => ResummonPhaseTwo(fail));
        }

        void ResummonPhaseTwo(NativeEventMap.Mob fail)
        {
            if (!active || !furyFailed) return;
            if (encounters.TryGetValue(map, out var current) && !ReferenceEquals(current, this)) return;
            if (fail != null && fail.Alive) map.Kill(fail);
            var resummoned = map.SpawnAt(LucidP2, LucidTables.FuryResummon.x, LucidTables.FuryResummon.y, false);
            if (resummoned == null) return;
            long targetHp = Math.Max(1, resummoned.MaxHp * FuryResummonHpPercent / 100);
            resummoned.SetHp(targetHp);
            map.Instance?.SetIntProperty("phase", PhaseTwo);
            StartPhase(map, resummoned, PhaseTwo);
        }

        void SummonNightmares()
        {
            map.DropMapMessage(5, "[Lucid] Lucid has summoned a powerful nightmare!");
            if (phase == PhaseOne && CountMobs(MushroomP1) < MaxMushrooms)
                SpawnGroundMob(MushroomP1, RandomGroundPoint(PhaseOne));
            int golemId = phase == PhaseOne ? GolemP1 : GolemP2;
            int spawnCount = Math.Min(phase == PhaseOne ? 1 : 2, MaxGolems - CountMobs(golemId));
            for (int index = 0; index < spawnCount; index++) SpawnGroundMob(golemId, RandomGroundPoint(phase));
        }

        void CastRush()
        {
            map.DropMapMessage(5, "[Lucid] Lucid is unleashing even greater power!");
            map.BroadcastEffect(RushEffect);
            var hit = new HashSet<int>();
            for (long elapsed = 0; elapsed <= RushDurationMs; elapsed += RushHitIntervalMs)
            {
                long collisionTime = elapsed;
                Schedule(elapsed, () =>
                {
                    if (!active || !IsCurrentBossAlive()) return;
                    var position = RushPositionAt(collisionTime);
                    var body = new Rect(
                        position.x + LucidTables.RushBodyLt.x,
                        position.y + LucidTables.RushBodyLt.y,
                        LucidTables.RushBodyRb.x - LucidTables.RushBodyLt.x,
                        LucidTables.RushBodyRb.y - LucidTables.RushBodyLt.y);
                    // 源端一个 Character 在同一轮冲刺里只结算一次（hitCharacters 集合）。
                    if (body.Contains(PlayerPosition) && hit.Add(0)) DamageCharacter(20, "rush", boss);
                });
            }
        }

        void BreakStainedGlass()
        {
            int index = stainedGlassIndex++ % LucidTables.GlassAreas.Length;
            var area = LucidTables.GlassAreas[index];
            map.BroadcastEffect(StainedGlassEffects[index]);
            ScheduleDamage(1260, 20, area, "stained-glass");
            Schedule(3000, () =>
            {
                if (active && IsCurrentBossAlive() && CountMobs(GolemP2) < MaxGolems)
                    SpawnGroundMob(GolemP2, new Vector2(area.x + area.width / 2, area.y));
            });
        }

        void ApplyHurdleDamage()
        {
            int golemId = phase == PhaseOne ? GolemP1 : GolemP2;
            var players = PlayerPosition;
            foreach (var golem in map.AllMobs)
            {
                if (golem.Id != golemId || !golem.Alive) continue;
                var position = golem.Position;
                var hurdle = new Rect(position.x - 20, position.y - 500, 40, 510);
                if (!hurdle.Contains(players)) continue;
                DamageCharacter(20, "hurdle-area", boss);
                break;
            }
        }

        void CastContagiousBomb()
        {
            // 源端从在场的活人里随机挑一个「带菌者」，单机只有本地角色。
            if (!PlayerAvailable) return;
            map.DropMapMessage(5, "Lucid's bomb is spreading from you. Move away from the party!");
            map.BroadcastEffect(BombEffect);
            Schedule(3000, () =>
            {
                if (!active || !IsCurrentBossAlive() || !PlayerAvailable) return;
                var center = PlayerPosition;
                ApplyDamage(35, new Rect(center.x - 250, center.y - 250, 500, 500), "contagious-bomb", boss);
            });
        }

        void ScheduleDamage(long delay, int percent, Rect? range, string skill)
            => Schedule(delay, () => { if (active && IsCurrentBossAlive()) ApplyDamage(percent, range, skill, boss); });

        void ApplyDamage(int percent, Rect? range, string skill, NativeEventMap.Mob source)
        {
            if (!PlayerAvailable) return;
            if (range.HasValue && !range.Value.Contains(PlayerPosition)) return;
            DamageCharacter(percent, skill, source);
        }

        void DamageCharacter(int percent, string skill, NativeEventMap.Mob source)
        {
            if (!PlayerAvailable) return;
            long maxHp = NpcApi.ScriptMaxHp;
            int damage = (int)Math.Max(1, maxHp * percent / 100);
            NpcApi.ScriptHp -= damage;
            map.DamagePlayerPacket(damage, source?.Id ?? boss.Id, source?.ObjectId ?? bossObjectId);
            Debug.Log("[LucidSkillTrace] map=" + map.Id + " phase=" + phase + " skill=" + skill +
                " mob=" + (source?.Id ?? boss.Id) + " oid=" + (source?.ObjectId ?? bossObjectId) +
                " percent=" + percent + " damage=" + damage + " hp=" + NpcApi.ScriptHp);
        }

        void SpawnGroundMob(int mobId, Vector2 point)
            => map.SpawnOnGround(mobId, point.x, point.y);

        void SpawnAt(int mobId, Vector2 point, bool raw)
            => map.SpawnAt(mobId, point.x, point.y, ground: !raw);

        int CountMobs(int mobId)
        {
            int count = 0;
            foreach (var mob in map.AllMobs) if (mob.Id == mobId && mob.Alive) count++;
            return count;
        }

        static Vector2 RandomGroundPoint(int targetPhase)
        {
            int[] xPositions = targetPhase == PhaseOne
                ? new[] { 220, 520, 820, 1120, 1420, 1720 }
                : new[] { 0, 250, 500, 750, 1000, 1250 };
            int x = xPositions[UnityEngine.Random.Range(0, xPositions.Length)];
            return new Vector2(x, targetPhase == PhaseOne ? 0 : 380);
        }

        bool PlayerAvailable
        {
            get
            {
                if (!NpcApi.ScriptAlive) return false;
                return PlayerMapMatches;
            }
        }

        bool PlayerMapMatches => NpcApi.ScriptMapId == map.Id;

        Vector2 PlayerPosition => NpcApi.ScriptPosition;

        public void StopInternal(bool cleanup)
        {
            active = false;
            jobs.Clear();
            if (cleanup) CleanupAuxiliaryMobs(map);
        }
    }
}
