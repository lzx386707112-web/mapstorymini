// 技能参数口径检查（不依赖 Unity 编辑器，编辑器开着主工程也能跑）。
//
// 背景：主工程被 Unity 编辑器占用时 -batchmode -executeMethod 起不来，
// 但 tools/unity-compile-check 已经把 Assets/Scripts 编成 AssemblyCSharpCheck.dll，
// 这里直接引用那份 DLL 调真实运行时类型，验证 JobTree.json → NativeSkillDefinition 的换算。
//
// 用法（先保证 DLL 是新的）：
//   dotnet build ../unity-compile-check/Check.csproj --no-restore
//   cd tools/skill-param-check && dotnet run
//
// 覆盖：
//   1) 每一级：段数 = max(1, attackCount, bulletCount)（源端 AbstractDealDamageHandler:1103）；
//   2) 老职业只带 bulletCount 的多段技能（二连射/双飞斩/三连环/箭扫射/急速射/三连杀/双弹射击）；
//   3) 单段技能的段数没有被误抬（强力攻击/狂乱之剑）。
using System;
using System.Collections.Generic;
using System.IO;

public static class SkillParamCheck
{
    static int assertions;

    static void Check(bool ok, string label)
    {
        assertions++;
        if (!ok) throw new Exception("[SkillParamCheck] " + label);
    }

    static string Find(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    // SkillBook.Native 同款：JobTree 条目只有 id/type/levels，运行时先包一层 skill 字典。
    static Dictionary<string, object> Wrap(Dictionary<string, object> data)
    {
        var keys = new HashSet<string>();
        foreach (var row in ((Dictionary<string, object>)data["levels"]).Values)
            foreach (var key in ((Dictionary<string, object>)row).Keys) keys.Add(key);
        return new Dictionary<string, object>
        {
            { "id", Convert.ToInt32(data["id"]) },
            { "kind", data["type"] },
            { "magic", keys.Contains("mad") },
            { "element", "" },
            { "summonInterval", 1f },
        };
    }

    public static int Main(string[] args)
    {
        try
        {
            string path = args.Length > 0 ? args[0] : Find("Assets/Resources/UI/JobTree.json");
            if (path == null) throw new Exception("找不到 Assets/Resources/UI/JobTree.json");
            var root = (Dictionary<string, object>)MiniJson.Parse(File.ReadAllText(path));
            var skills = (Dictionary<string, object>)root["skills"];

            int rows = 0;
            foreach (var pair in skills)
            {
                var data = (Dictionary<string, object>)pair.Value;
                var levels = (Dictionary<string, object>)data["levels"];
                foreach (var level in levels)
                {
                    var row = (Dictionary<string, object>)level.Value;
                    var def = new NativeSkillDefinition(Wrap(data), row);
                    int expected = Math.Max(1, Math.Max((int)Art.Num(row, "attackCount"), (int)Art.Num(row, "bulletCount")));
                    Check(def.HitCount == expected, pair.Key + " lv" + level.Key + " hit lines " + expected + " got " + def.HitCount);
                    rows++;
                }
            }

            var spot = new (string id, int hits, string name)[]
            {
                ("3001005", 2, "二连射"), ("13001003", 2, "骑士团二连射"),
                ("4001344", 2, "双飞斩"), ("14001004", 2, "骑士团双飞斩"),
                ("5001003", 2, "双弹射击"), ("4121007", 3, "三连环光击破"),
                ("14111005", 3, "骑士团三连环"), ("5210000", 3, "三连射杀"),
                ("5221007", 3, "急速射"), ("13111007", 3, "骑士团疾风扫射"),
                ("3111006", 4, "箭扫射"), ("13111001", 4, "骑士团箭扫射"),
                ("3211006", 5, "弩弓箭扫射"), ("2001005", 3, "魔法双击"),
                ("12001003", 2, "骑士团魔法双击"), ("1001004", 1, "强力攻击"),
                ("1111003", 1, "狂乱之剑"),
            };
            foreach (var item in spot)
            {
                var data = (Dictionary<string, object>)skills[item.id];
                var row = (Dictionary<string, object>)((Dictionary<string, object>)data["levels"])["1"];
                var def = new NativeSkillDefinition(Wrap(data), row);
                Check(def.HitCount == item.hits, item.name + "(" + item.id + ") lines " + item.hits + " got " + def.HitCount);
            }

            Console.WriteLine("[SkillParamCheck] PASS " + skills.Count + " skills / " + rows + " level rows / " + assertions + " assertions");
            return 0;
        }
        catch (Exception error)
        {
            Console.WriteLine(error.Message);
            return 1;
        }
    }
}
