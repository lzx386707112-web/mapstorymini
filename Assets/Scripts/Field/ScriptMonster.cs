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
        return host;
    }

    public bool FhSet;

    long MaxHp()
    {
        FieldCatalog.LoadMob(Id);
        return FieldCatalog.Mobs.TryGetValue(Id, out var def)
            ? System.Math.Max(1, NativeCombatData.Long(def.stats, "maxHP", 1)) : 1;
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
