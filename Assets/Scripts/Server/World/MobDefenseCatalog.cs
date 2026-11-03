using System;
using System.Collections.Generic;

// 282 IMG defense percentages plus the explicitly approved legacy migration values.
public sealed class MobDefenseCatalog
{
    static MobDefenseCatalog shared;
    public static MobDefenseCatalog Shared => shared ??= new MobDefenseCatalog(PlatformRes.LoadText("Enhancement/MobDefense"));
    readonly Dictionary<string,object> monsters;
    public MobDefenseCatalog(string json)
    {
        var root=MiniJsonDb.Parse(json) as Dictionary<string,object>;
        monsters=root?["monsters"] as Dictionary<string,object>
            ?? throw new InvalidOperationException("282怪物防御数据缺失");
    }
    public void Apply(int id,Dictionary<string,object> stats)
    {
        if(stats==null)throw new ArgumentNullException(nameof(stats));
        if(!monsters.TryGetValue(id.ToString(System.Globalization.CultureInfo.InvariantCulture),out var raw))
            throw new InvalidOperationException("怪物尚未核实282防御："+id);
        var row=(Dictionary<string,object>)raw;
        stats["PDRate"]=Convert.ToInt32(row["PDRate"]);
        stats["MDRate"]=Convert.ToInt32(row["MDRate"]);
    }
}
