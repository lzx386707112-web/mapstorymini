using System;
using System.Collections.Generic;
using MapleDash.Js;

public enum ScriptOperation { Open, Call, Close, SaveLocation, PeekLocation, GetLocation, ClearLocation, GachaDraw }

// 本地传输专用宿主桥：JS 对象显式绑定，禁止反射调用任意 C#/Java 方法。
// 脚本状态/解释执行属于 Server；对话与地图效果由 Client 注入的适配器消费。
public sealed class ScriptRequest
{
    public ScriptOperation operation;
    public int characterId, sessionId;
    public string name, source, function;
    public Dictionary<string, JsValue> bindings;
    public JsValue[] arguments;
    public string location;
    public int mapId, portalId;
    public int gachaponId;   // GachaDraw：gachapon_reward_pool.gachapon_id（NPC 编号或远程扭蛋 id）
}
public sealed class ScriptResponse
{
    public int sessionId;
    public bool hasStart, hasAction, hasEnd;
    public JsValue value;
}

public sealed class NativeScriptService
{
    sealed class Session { public int characterId; public JsEngine engine; }
    readonly Dictionary<int, Session> sessions = new Dictionary<int, Session>();
    int sequence;
    readonly DbStore db;
    public NativeScriptService(DbStore db = null) { this.db = db; }
    static readonly HashSet<string> LocationTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "FREE_MARKET", "WORLDTOUR", "FLORINA", "INTRO", "SUNDAY_MARKET", "MIRROR",
        "EVENT", "BOSSPQ", "HAPPYVILLE", "MONSTER_CARNIVAL", "DEVELOPER", "JAIL"
    };
    public int ActiveCount => sessions.Count;

    public ScriptResponse Handle(ScriptRequest request)
    {
        if (request.operation == ScriptOperation.GachaDraw)
        {
            if (db == null || CharactersDao.FindById(db, request.characterId) == null)
                throw new JsRuntimeException("Script character does not exist");
            return new ScriptResponse { value = GachaDraw(request.gachaponId) };
        }
        if (request.operation >= ScriptOperation.SaveLocation)
        {
            if (db == null || CharactersDao.FindById(db, request.characterId) == null)
                throw new JsRuntimeException("Script character does not exist");
            if (!LocationTypes.Contains(request.location ?? "")) throw new JsRuntimeException("Invalid SavedLocationType: " + request.location);
            var row = SavedLocationsDao.Find(db, request.characterId, request.location);
            int map = row == null ? -1 : DbStore.Int(row, "map");
            if (request.operation == ScriptOperation.SaveLocation)
                SavedLocationsDao.Save(db, request.characterId, request.location, request.mapId, request.portalId);
            if (request.operation == ScriptOperation.GetLocation || request.operation == ScriptOperation.ClearLocation)
                db.Table("savedlocations").Delete(r => DbStore.Int(r, "characterid") == request.characterId && DbStore.Str(r, "locationtype") == request.location);
            if (request.operation != ScriptOperation.PeekLocation) db.Save();
            return new ScriptResponse { value = new JsNumber(map) };
        }
        if (request.operation == ScriptOperation.Close)
        {
            if (sessions.TryGetValue(request.sessionId, out var closing) && closing.characterId == request.characterId)
                sessions.Remove(request.sessionId);
            return new ScriptResponse();
        }
        if (request.operation == ScriptOperation.Open)
        {
            var engine = new JsEngine(request.source, request.name);
            if (request.bindings != null)
                foreach (var pair in request.bindings)
                    if (pair.Key != "Java") engine.Bind(pair.Key, pair.Value);
            engine.Interp.ResolveHostType = name =>
            {
                if (name == "org.gms.config.GameConfig" && db != null) return ConfigClass();
                if (name == "org.gms.util.packets.Fishing") return FishingClass();
                if (request.bindings != null && request.bindings.TryGetValue("Java", out var fallback))
                    return engine.Interp.Call(engine.Interp.GetMember(fallback, "type"), fallback, new JsValue[] { new JsString(name) });
                return null;
            };
            // 顶层异常不保留半初始化会话。
            engine.Run();
            int id = ++sequence;
            sessions.Add(id, new Session { characterId = request.characterId, engine = engine });
            return new ScriptResponse { sessionId = id, hasStart = engine.HasFunction("start"),
                hasAction = engine.HasFunction("action"), hasEnd = engine.HasFunction("end") };
        }
        if (!sessions.TryGetValue(request.sessionId, out var session) || session.characterId != request.characterId)
            throw new JsRuntimeException("Script session is closed or belongs to another character");
        try
        {
            return new ScriptResponse { sessionId = request.sessionId,
                value = session.engine.Call(request.function, request.arguments ?? Array.Empty<JsValue>()) };
        }
        catch
        {
            sessions.Remove(request.sessionId);
            throw;
        }
    }

    // GachaponService.doGachapon（GachaponService.java:210）：先在 1,000,000 点积分里按
    // 「公共池 prob*100 / 私有池 weight 占比」切分选池，再在池内奖品中均匀抽取。
    // 返回 [itemId, quantity]；奖池为空返回空数组（客户端给「百宝箱为空」提示）。
    static readonly Random gachaRandom = new Random();

    JsValue GachaDraw(int gachaponId)
    {
        var now = DateTime.Now;
        var pools = new List<DbRow>();
        if (db.HasTable("gachapon_reward_pool"))
            foreach (var pool in db.Table("gachapon_reward_pool").rows)
            {
                if (DbStore.Int(pool, "gachapon_id") != gachaponId) continue;
                string start = DbStore.Str(pool, "start_time"), end = DbStore.Str(pool, "end_time");
                if (!string.IsNullOrEmpty(start) && DateTime.TryParse(start, out var s) && now < s) continue;
                if (!string.IsNullOrEmpty(end) && DateTime.TryParse(end, out var e) && now > e) continue;
                pools.Add(pool);
            }
        if (pools.Count == 0) return new JsArray();

        int probTotal = 0, totalWeight = 0;
        foreach (var p in pools) { probTotal += DbStore.Int(p, "prob"); totalWeight += DbStore.Int(p, "weight"); }
        int weightPoint = 1000000 - probTotal * 100;
        int random = gachaRandom.Next(1000000);
        DbRow target = null;
        int pointTotal = 0;
        foreach (var p in pools)
        {
            int point = DbStore.Int(p, "is_public") != 0
                ? DbStore.Int(p, "prob") * 100
                : (totalWeight > 0 ? MathfRound(weightPoint * DbStore.Int(p, "weight") / (float)totalWeight) : 0);
            pointTotal += point;
            if (pointTotal > random) { target = p; break; }
        }
        target ??= pools[0];

        var rewards = new List<DbRow>();
        if (db.HasTable("gachapon_reward"))
            foreach (var r in db.Table("gachapon_reward").rows)
                if (DbStore.Int(r, "pool_id") == DbStore.Int(target, "id")) rewards.Add(r);
        if (rewards.Count == 0) return new JsArray();
        var pick = rewards[gachaRandom.Next(rewards.Count)];
        return new JsArray(new JsValue[] {
            new JsNumber(DbStore.Int(pick, "item_id")), new JsNumber(DbStore.Int(pick, "quantity", 1)) });
    }

    static int MathfRound(float v) => (int)MathF.Round(v, MidpointRounding.AwayFromZero);

    JsValue ConfigClass()
    {
        var config = new JsHostObject(null);
        foreach (string method in new[] { "get", "getObject", "getInteger", "getIntValue", "getLong", "getLongValue",
            "getShort", "getShortValue", "getByte", "getByteValue", "getFloat", "getFloatValue", "getDouble", "getDoubleValue",
            "getBoolean", "getBooleanValue", "getString", "getStringValue" })
            config.Set(method, JsFunction.FromNative(method, call =>
            {
                if (call.ArgCount != 1) throw new JsRuntimeException("Unmigrated GameConfig overload: " + method + "/" + call.ArgCount);
                string key = JsConvert.ToString(call.Arg(0));
                var row = db.Table("game_config").Find(r => string.Equals(DbStore.Str(r, "config_code"), key, StringComparison.OrdinalIgnoreCase));
                if (row == null) return JsNull.Value;
                string raw = DbStore.Str(row, "config_value");
                if (method == "getString" || method == "getStringValue") return new JsString(raw);
                if (method == "getBoolean" || method == "getBooleanValue") return JsBoolean.Of(raw == "1" || raw.Equals("true", StringComparison.OrdinalIgnoreCase));
                if (method != "get" && method != "getObject") return new JsNumber(JsConvert.StringToNumber(raw));
                string clazz = DbStore.Str(row, "config_clazz");
                if (clazz == "java.lang.String") return new JsString(raw);
                if (clazz == "java.lang.Boolean") return JsBoolean.Of(raw == "1" || raw.Equals("true", StringComparison.OrdinalIgnoreCase));
                if (clazz == "java.lang.Integer" || clazz == "java.lang.Long" || clazz == "java.lang.Double" || clazz == "java.lang.Float" || clazz == "java.lang.Short" || clazz == "java.lang.Byte") return new JsNumber(JsConvert.StringToNumber(raw));
                if (raw.TrimStart().StartsWith("{", StringComparison.Ordinal) || raw.TrimStart().StartsWith("[", StringComparison.Ordinal)) return FromConfig(MiniJsonDb.Parse(raw));
                throw new JsRuntimeException("Unmigrated GameConfig value type: " + clazz);
            }));
        // 源端还有按作用域读的版本（GameConfig.getValueProp("server"/"world", key)）。
        // scripts 里的 GameConfig.getServerInt("rebirth_level") 走的是这一层，不能只按 config_code 查。
        config.Set("getServerInt", JsFunction.FromNative("getServerInt",
            call => new JsNumber(JsConvert.StringToNumber(Scoped("server", call, false)))));
        config.Set("getServerString", JsFunction.FromNative("getServerString",
            call => new JsString(Scoped("server", call, true))));
        config.Set("getServerBoolean", JsFunction.FromNative("getServerBoolean",
            call => JsBoolean.Of(Scoped("server", call, true) is string raw
                && (raw == "1" || raw.Equals("true", StringComparison.OrdinalIgnoreCase)))));
        return config;
    }

    // org.gms.util.packets.Fishing（源端 Fishing.java）。
    // npc/9330045.js 在脚本顶层就 Java.type 它，随后立刻用 getInstance().getItemsByType()
    // 把 6 类掉落填进 dropTypes 常量 —— 类不注册的话脚本连 start() 都跑不到，
    // 自由市场里「余夫」这个 NPC 等于完全打不开（玩家只看到「未接入的服务端类」）。
    // 常量照抄 Fishing.java:44-60；物品清单走 fishing_items 表（:153 同一条 SQL）。
    JsValue FishingClass()
    {
        var type = new JsHostObject(null);
        type.Set("COMMON_BAIT_BASE_RATE", new JsNumber(0.3));
        type.Set("ADVANCED_BAIT_BASE_RATE", new JsNumber(0.6));
        type.Set("MAX_SUCCESS_LEVEL_RATE", new JsNumber(0.3));
        type.Set("ITEM_RATE_MYTHIC", new JsNumber(0.0001));
        type.Set("ITEM_RATE_LEGENDARY", new JsNumber(0.0002));
        type.Set("ITEM_RATE_SUPER_RARE", new JsNumber(0.001));
        type.Set("ITEM_RATE_RARE", new JsNumber(0.01));
        type.Set("ITEM_RATE_UNCOMMON", new JsNumber(0.1));
        type.Set("ITEM_RATE_COMMON", new JsNumber(0.8887));
        var instance = new JsHostObject(null);
        instance.Set("getItemsByType", JsFunction.FromNative("getItemsByType",
            call => new JsArray(FishingItems(JsConvert.ToInt32(call.Arg(0))).ConvertAll<JsValue>(id => new JsNumber(id)))));
        instance.Set("refreshItemCache", JsFunction.FromNative("refreshItemCache", _ => JsUndefined.Value));
        type.Set("getInstance", JsFunction.FromNative("getInstance", _ => instance));
        return type;
    }

    // SELECT item_id FROM fishing_items WHERE item_type = ?（Fishing.java:153）
    List<int> FishingItems(int itemType)
    {
        var items = new List<int>();
        if (db == null) return items;
        foreach (var row in db.Table("fishing_items").rows)
            if (DbStore.Int(row, "item_type") == itemType) items.Add(DbStore.Int(row, "item_id"));
        return items;
    }

    // 找不到键时按源端默认值返回：getServerInt → 0，getServerString → ""
    string Scoped(string scope, JsCall call, bool asText)
    {
        string key = JsConvert.ToString(call.Arg(0));
        var row = db.Table("game_config").Find(r =>
            string.Equals(DbStore.Str(r, "config_type"), scope, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(DbStore.Str(r, "config_code"), key, StringComparison.OrdinalIgnoreCase));
        return row == null ? (asText ? "" : "0") : DbStore.Str(row, "config_value");
    }

    static JsValue FromConfig(object value)
    {
        if (value == null) return JsNull.Value;
        if (value is bool boolean) return JsBoolean.Of(boolean);
        if (value is string text) return new JsString(text);
        if (value is Dictionary<string, object> dictionary)
        {
            var result = new JsObject(); foreach (var pair in dictionary) result.Set(pair.Key, FromConfig(pair.Value)); return result;
        }
        if (value is List<object> list)
        {
            var result = new JsArray(); foreach (var item in list) result.Items.Add(FromConfig(item)); return result;
        }
        return new JsNumber(Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture));
    }

    public void EndSession(int characterId)
    {
        var remove = new List<int>();
        foreach (var pair in sessions) if (pair.Value.characterId == characterId) remove.Add(pair.Key);
        foreach (int id in remove) sessions.Remove(id);
    }
}
