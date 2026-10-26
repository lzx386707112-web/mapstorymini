using System;

public sealed class ScriptValueRequest
{
    public int characterId;
    public bool account, write;
    // 0 永久，1 每日，2 每周。与源端两个参数的 isDaily=true/false 对应。
    public int period;
    public string name, value;
}
public sealed class ScriptValueResponse { public string value; }

// AbstractPlayerInteraction + ExtendUtil/ExtendValueMapper：所有脚本共享持久化键值系统。
public sealed class NativeScriptValues
{
    readonly DbStore db;
    readonly Func<DateTimeOffset> clock;
    public NativeScriptValues(DbStore db, Func<DateTimeOffset> clock = null)
    { this.db = db; this.clock = clock ?? (() => DateTimeOffset.Now); }

    public ScriptValueResponse Handle(ScriptValueRequest request)
    {
        var character = CharactersDao.FindById(db, request.characterId);
        if (character == null) throw new MapleDash.Js.JsRuntimeException("Script character does not exist");
        if (request.period < 0 || request.period > 2) throw new MapleDash.Js.JsRuntimeException("Invalid script value period");
        if (request.name == null) throw new MapleDash.Js.JsRuntimeException("Script value key cannot be null");
        long owner = request.account ? character.accountid : character.id;
        int type = (request.account ? 11 : 21) + request.period;
        var table = db.Table("extend_value");
        var now = clock();
        long boundary = 0;
        if (request.period > 0)
        {
            var date = now.Date;
            // Java Calendar 的 China 默认周从星期日开始；set(DAY_OF_WEEK, MONDAY)
            // 对星期日得到下一天，与 ExtendType.getCleanMap 原逻辑一致。
            if (request.period == 2) date = date.AddDays(1 - (int)date.DayOfWeek);
            boundary = new DateTimeOffset(date, now.Offset).ToUnixTimeMilliseconds();
            table.Delete(r => DbStore.Int(r, "extend_type") == type && DbStore.Long(r, "create_time") < boundary);
        }
        string id = owner.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var row = table.Find(r => DbStore.Str(r, "extend_id") == id && DbStore.Int(r, "extend_type") == type && DbStore.Str(r, "extend_name") == request.name);
        if (request.write)
        {
            long time = now.ToUnixTimeMilliseconds();
            if (row == null)
            {
                table.Insert(new DbRow { ["extend_id"] = id, ["extend_type"] = (long)type,
                    ["extend_name"] = request.name, ["extend_value"] = request.value, ["create_time"] = time, ["update_time"] = 0L });
                row = table.Find(r => DbStore.Str(r, "extend_id") == id && DbStore.Int(r, "extend_type") == type && DbStore.Str(r, "extend_name") == request.name);
            }
            else { row["extend_value"] = request.value; row["update_time"] = time; }
        }
        if (request.write || request.period > 0) db.Save();
        return new ScriptValueResponse { value = row == null ? null : DbStore.Str(row, "extend_value") };
    }
}
