using MapleDash.Js;

// org.gms.server.life.LifeFactory.getMonster(int) 的移植层。
//
// 源端返回的是一个「还没进地图的 Monster」：脚本拿到它之后先 setPosition / setFh /
// setDamageBlockedUntil / addHp，再交给 map.spawnMonster(OnGroundBelow) 才真正进图
// （event/AreaBoss*.js、PinkBeanBattle.js、LucidBattle.js 全是这个套路）。
//
// 移植端地图里的怪由 NativeEventMap 掌管（Mob），所以这里只保存「进图前要带过去的字段」，
// 真正 spawn 的那一刻 NativeEventMap 会把宿主整体接管成 Mob，并把这几项落到实体上。
public sealed class ScriptMonster
{
    public readonly int Id;
    public double X, Y;
    public int Fh;
    // Monster.setDamageBlockedUntil（Monster.java:1701）：绝对毫秒时间戳，之前免疫伤害。
    public long DamageBlockedUntil;
    // Monster.addHp / setHp 的进图前覆盖；-1 表示没动过，进图时用 Mob.wz 的 maxHP。
    public long HpOverride = -1;
    // Monster.changeDifficulty 的进图前结果（ChangeableStats）：进图时同时改 MaxHp 与 exp，
    // 不能走 HpOverride（那条会 clamp 到原 MaxHp，而难度通常是上调）。
    public (long hp, long exp)? DifficultyStats;
    public bool PositionSet;
    public ScriptMonster(int id) { Id = id; }

    public const string TypeName = "org.gms.server.life.LifeFactory";

    static JsValue Done => JsUndefined.Value;

    public JsHostObject Host()
    {
        var host = new JsHostObject(this);
        host.Set("id", new JsNumber(Id));
        host.Missing = key => throw new JsRuntimeException("Unmigrated monster API: " + key);
        Bind(host, "getId", _ => new JsNumber(Id));
        host.Set("setId", JsFunction.FromNative("setId", _ => Done));
        Bind(host, "getName", _ => new JsString(WzNames.Mob(Id)));
        // AbstractMapObject 的 position 默认 (0,0)：源端 getMonster 出来的 Monster 也是这个值，
        // 所以 spawnMonster(不带落点) 在源端等价于「刷在原点上」。
        host.Set("position", NpcApi.BuildPoint(0, 0));
        Bind(host, "getPosition", _ => NpcApi.BuildPoint(X, Y));
        host.Set("setPosition", JsFunction.FromNative("setPosition", c =>
        {
            if (!(c.Arg(0) is JsHostObject point)) return Done;
            X = JsConvert.ToNumber(point.Get("x"));
            Y = JsConvert.ToNumber(point.Get("y"));
            PositionSet = true;
            host.Set("position", point);
            return Done;
        }));
        Bind(host, "getFh", _ => new JsNumber(Fh));
        host.Set("setFh", JsFunction.FromNative("setFh", c =>
        {
            Fh = JsConvert.ToInt32(c.Arg(0));
            FhSet = true;
            return Done;
        }));
        // 还没进图的怪恒为活着：脚本拿它做 isAlive() 守卫（castFlowerExplosion 就是先判 boss 活没活）。
        Bind(host, "isAlive", _ => JsBoolean.Of(true));
        Bind(host, "getObjectId", _ => new JsNumber(0));
        Bind(host, "getHp", _ => new JsNumber(HpOverride >= 0 ? HpOverride : MaxHp()));
        Bind(host, "getMaxHp", _ => new JsNumber(MaxHp()));
        Bind(host, "getMap", _ => JsNull.Value);
        host.Set("setHp", JsFunction.FromNative("setHp", c => { HpOverride = (long)JsConvert.ToNumber(c.Arg(0)); return Done; }));
        host.Set("addHp", JsFunction.FromNative("addHp", c =>
        {
            long delta = (long)JsConvert.ToNumber(c.Arg(0));
            HpOverride = System.Math.Max(0, (HpOverride >= 0 ? HpOverride : MaxHp()) + delta);
            return Done;
        }));
        host.Set("setDamageBlockedUntil", JsFunction.FromNative("setDamageBlockedUntil",
            c => { DamageBlockedUntil = (long)JsConvert.ToNumber(c.Arg(0)); return Done; }));
        // Monster.changeDifficulty(difficulty, pqMob)：PiratePQ 在 em.getMonster 的模板上调它。
        host.Set("changeDifficulty", JsFunction.FromNative("changeDifficulty", c =>
        {
            var stats = ChangeableStats(Id, JsConvert.ToInt32(c.Arg(0)), JsConvert.ToBoolean(c.Arg(1)));
            if (stats.HasValue) DifficultyStats = stats;
            return Done;
        }));
        return host;
    }

    public bool FhSet;

    long MaxHp()
    {
        FieldCatalog.LoadMob(Id);
        return FieldCatalog.Mobs.TryGetValue(Id, out var def)
            ? System.Math.Max(1, NativeCombatData.Long(def.stats, "maxHP", 1)) : 1;
    }

    // GameConstants.mobHpVal（GMS083 口径，index=等级 0..198），超界返回 int.MaxValue。
    static readonly int[] MobHpTable = {0,15,20,25,35,50,65,80,95,110,125,150,175,200,225,250,275,300,325,350,
        375,405,435,465,495,525,580,650,720,790,900,990,1100,1200,1300,1400,1500,1600,1700,1800,
        1900,2000,2100,2200,2300,2400,2520,2640,2760,2880,3000,3200,3400,3600,3800,4000,4300,4600,4900,5200,
        5500,5900,6300,6700,7100,7500,8000,8500,9000,9500,10000,11000,12000,13000,14000,15000,17000,19000,21000,23000,
        25000,27000,29000,31000,33000,35000,37000,39000,41000,43000,45000,47000,49000,51000,53000,55000,57000,59000,61000,63000,
        65000,67000,69000,71000,73000,75000,77000,79000,81000,83000,85000,89000,91000,93000,95000,97000,99000,101000,103000,
        105000,107000,109000,111000,113000,115000,118000,120000,125000,130000,135000,140000,145000,150000,155000,160000,165000,170000,175000,180000,
        185000,190000,195000,200000,205000,210000,215000,220000,225000,230000,235000,240000,250000,260000,270000,280000,290000,300000,310000,320000,
        330000,340000,350000,360000,370000,380000,390000,400000,410000,420000,430000,440000,450000,460000,470000,480000,490000,500000,510000,520000,
        530000,550000,570000,590000,610000,630000,650000,670000,690000,710000,730000,750000,770000,790000,810000,830000,850000,870000,890000,910000};
    static long MobHpVal(int level)
        => level < 0 || level >= MobHpTable.Length ? int.MaxValue : MobHpTable[level];
    // Java Math.round(double)=floor(x+0.5)，C# Math.Round 是银行家舍入，必须手写。
    static long JRound(double v) => (long)System.Math.Floor(v + 0.5);

    // Monster.changeDifficulty(difficulty, pqMob)（Monster.java:1920）→ changeLevelByDifficulty
    // → changeLevel((int)(level*rate), pqMob) → ChangeableStats(MonsterStats, newLevel, pqMob)
    // （ChangeableStats.java:37）。rate：2→1.4 / 3→2.1 / 4→3.2 / 5→5.6 / 6→7.7。
    // mod=newLevel/level；非 boss HP=GameConstants.mobHpVal[newLevel]*pqMod、
    // exp=mobHpVal[newLevel]/hpRatio*pqMod（hpRatio=hp/exp，exp<=0 → ∞ → exp=0）；
    // boss HP=hp*mod*pqMod、exp 不变；pqMod=pqMob?1.5:1。
    // 源端 MonsterStats.setChange 全工程无调用点 → changeable 恒 false → changeDifficulty
    // 实际总是 no-op；这里照实守卫（返回 null）。
    public static (long hp, long exp)? ChangeableStats(int mobId, int difficulty, bool pqMob)
    {
        FieldCatalog.LoadMob(mobId);
        if (!FieldCatalog.Mobs.TryGetValue(mobId, out var def)) return null;
        var s = def.stats;
        if (NativeCombatData.Long(s, "changeable", 0) <= 0) return null;
        float rate = difficulty switch { 6 => 7.7f, 5 => 5.6f, 4 => 3.2f, 3 => 2.1f, 2 => 1.4f, _ => 1f };
        long baseHp = NativeCombatData.Long(s, "maxHP", 1), baseExp = NativeCombatData.Long(s, "exp", 0);
        int level = (int)NativeCombatData.Long(s, "level", 1);
        int newLevel = System.Math.Max(1, (int)(level * rate));
        bool boss = NativeCombatData.Long(s, "boss", 0) > 0;
        double mod = (double)newLevel / System.Math.Max(1, level);
        double hpRatio = baseExp > 0 ? (double)baseHp / baseExp : double.PositiveInfinity;
        double pqMod = pqMob ? 1.5 : 1.0;
        long hp = System.Math.Max(1, JRound((!boss ? MobHpVal(newLevel) : (double)baseHp * mod) * pqMod));
        long exp = System.Math.Min(JRound((!boss ? MobHpVal(newLevel) / hpRatio : (double)baseExp) * pqMod), int.MaxValue);
        return (hp, exp);
    }

    static void Bind(JsHostObject host, string name, System.Func<JsCall, JsValue> call)
        => host.Set(name, JsFunction.FromNative(name, call));

    public static JsValue Resolve(string name)
        => name == TypeName ? Type() : null;

    static JsValue Type()
    {
        var type = new JsHostObject(null);
        type.Missing = key => throw new JsRuntimeException("Unmigrated LifeFactory API: " + key);
        type.Set("getMonster", JsFunction.FromNative("getMonster",
            c => new ScriptMonster(JsConvert.ToInt32(c.Arg(0))).Host()));
        return type;
    }
}
