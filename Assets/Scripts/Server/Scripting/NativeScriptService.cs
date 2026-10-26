using System;
using System.Collections.Generic;
using MapleDash.Js;

public enum ScriptOperation { Open, Call, Close, SaveLocation, PeekLocation, GetLocation, ClearLocation }

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
        return config;
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
