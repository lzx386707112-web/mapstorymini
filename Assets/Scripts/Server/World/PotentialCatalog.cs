using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

// Raw TMS 282 ItemOption data. Retains every field, including effects not yet wired to combat.
public sealed class PotentialCatalog
{
    readonly Dictionary<int, Dictionary<string, object>> entries = new Dictionary<int, Dictionary<string, object>>();
    static PotentialCatalog shared;
    public static PotentialCatalog Shared => shared ??= new PotentialCatalog(PlatformRes.LoadText("Enhancement/ItemOption"));
    public string SourceSha256 { get; }
    public int Count => entries.Count;
    public IEnumerable<int> Ids => entries.Keys;
    public PotentialCatalog(string json)
    {
        var root = MiniJsonDb.Parse(json ?? "") as Dictionary<string, object>;
        if (root == null || Convert.ToInt32(root["schemaVersion"]) != 1 || !(root["options"] is Dictionary<string, object> options))
            throw new InvalidOperationException("282 潜能数据缺失或版本错误");
        SourceSha256 = (string)root["sourceSha256"];
        foreach (var pair in options)
        {
            int id = int.Parse(pair.Key, CultureInfo.InvariantCulture);
            var row = pair.Value as Dictionary<string, object>;
            if (id <= 0 || row == null || !(row["info"] is Dictionary<string, object>) || !(row["level"] is Dictionary<string, object>))
                throw new InvalidOperationException("无效潜能条目：" + pair.Key);
            entries.Add(id, row);
        }
    }
    public Dictionary<string, object> Info(int id)
        => entries.TryGetValue(id, out var row) ? new Dictionary<string, object>((Dictionary<string, object>)row["info"]) : null;
    // ItemOption.getOptionLevel: this intentionally retains the source's rawLevel=1 -> 0 edge case.
    public static int OptionLevel(int rawLevel)
    {
        if (rawLevel < 0) throw new ArgumentOutOfRangeException(nameof(rawLevel));
        return rawLevel == 0 ? 1 : rawLevel / 10 + (rawLevel % 10 > 1 ? 1 : 0);
    }
    public Dictionary<string, object> ValuesAt(int id, int optionLevel)
    {
        if (!entries.TryGetValue(id, out var row)) return null;
        var levels = (Dictionary<string, object>)row["level"];
        return levels.TryGetValue(optionLevel.ToString(CultureInfo.InvariantCulture), out var values)
            ? new Dictionary<string, object>((Dictionary<string, object>)values) : null;
    }
    public string Describe(int id, int optionLevel)
    {
        var info = Info(id);
        var values = ValuesAt(id, optionLevel);
        if (info == null || values == null) throw new ArgumentException("潜能或等级档不存在");
        string template = info.TryGetValue("string", out var text) ? text as string : null;
        if (template == null) throw new InvalidOperationException("潜能缺少文本模板");
        // Replace each named value independently; never substitute one arbitrary value into all placeholders.
        return Regex.Replace(template, @"#([A-Za-z][A-Za-z0-9]*)", match =>
            values.TryGetValue(match.Groups[1].Value, out var value) ? Convert.ToString(value, CultureInfo.InvariantCulture) : match.Value);
    }
}
