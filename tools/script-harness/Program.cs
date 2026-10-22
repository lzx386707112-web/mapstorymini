using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using MapleDash.Js;

// 离线验收器：用 dotnet 直接编译 Scripting/ 下的引擎（不依赖 Unity），
// 对源端 npc/*.js、quest/*.js 做「解析 + 跑一段对话」的冒烟测试。
// 用法：
//   dotnet run -- --parse <dir>    只解析，检查语法覆盖率
//   dotnet run -- --run   <dir>    解析并执行 start/action 序列
//   dotnet run -- --pack  <json>   校验打包通道：npc_scripts.json 能否被 MiniJson 解出、
//                                  条数/体积是否正常、抽样内容是否与磁盘上的 .js 一致
internal static class Program
{
    // 校验打包通道：Android/WebGL 读不到 /sdcard 上的 .js，全靠 Resources/Scripts/npc_scripts.json。
    // 这里把那份 JSON 用客户端同一套 MiniJson 解开，逐条丢给引擎解析，确保没有任何一条在
    // "打包 → 读取 → 解析"链路上被破坏（转义、BOM、编码问题都会在这一步暴露）。
    static int CheckPack(string jsonPath)
    {
        string text = File.ReadAllText(jsonPath, System.Text.Encoding.UTF8);
        var map = MiniJson.Parse(text) as Dictionary<string, object>;
        if (map == null)
        {
            Console.Error.WriteLine("pack is not a JSON object: " + jsonPath);
            return 2;
        }

        int npc = 0, quest = 0, parseFail = 0, mangled = 0;
        long bytes = 0;
        foreach (var kv in map)
        {
            var body = kv.Value as string;
            if (body == null) continue;
            bytes += body.Length;
            if (kv.Key.StartsWith("npc/", StringComparison.Ordinal)) npc++; else quest++;

            if (body.IndexOf('\uFFFD') >= 0 && mangled++ < 5)
                Console.WriteLine("  MANGLED " + kv.Key + " :: 含替换字符，源端文件本身是乱码");
            try { new JsEngine(body, kv.Key); }
            catch (Exception e)
            {
                if (parseFail++ < 8) Console.WriteLine("  PARSE " + kv.Key + " :: " + e.Message);
            }
        }
        Console.WriteLine($"pack={map.Count} npc={npc} quest={quest} chars={bytes / 1024}KiB "
                          + $"parseFail={parseFail} mangled={mangled}");
        return parseFail == 0 ? 0 : 1;
    }

    static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "--run";
        if (mode == "--pack") return CheckPack(args[1]);
        if (mode == "--img") return DumpImg(args[1], args.Length > 2 ? args[2] : "");
        if (mode == "--quest") return DumpQuests(args[1]);
        if (mode == "--trace")
        {
            string file = args[1];
            var eng = new JsEngine(File.ReadAllText(file), Path.GetFileName(file));
            var trace = new MockConversation(File.ReadAllText(file)) { Trace = true };
            eng.Bind("cm", trace.Cm); eng.Bind("qm", trace.Cm); eng.Bind("pi", trace.Cm); eng.Bind("player", trace.Player);
            eng.Run();
            bool quest = File.ReadAllText(file).Contains("qm.");
            if (eng.HasFunction("start")) { if (quest) eng.Call("start", new JsNumber(1), new JsNumber(0), new JsNumber(0)); else eng.Call("start"); }
            for (int i = 0; i < 8 && !trace.Disposed; i++)
            {
                if (!eng.HasFunction("action") && !quest) break;
                Console.WriteLine("   >>> action(1, 1, " + (i % 3) + ")");
                try { if (quest) eng.Call("start", new JsNumber(1), new JsNumber(1), new JsNumber(i % 3)); else eng.Call("action", new JsNumber(1), new JsNumber(1), new JsNumber(i % 3)); }
                catch (Exception ex) { Console.WriteLine("   !!! " + ex.Message); break; }
            }
            return 0;
        }
        string dir = args.Length > 1 ? args[1] : DefaultScriptsDir();
        if (!Directory.Exists(dir))
        {
            Console.Error.WriteLine("scripts dir not found: " + dir);
            return 2;
        }

        var files = new List<string>(Directory.GetFiles(dir, "*.js", SearchOption.TopDirectoryOnly));
        files.Sort(StringComparer.Ordinal);

        int ok = 0, parseFail = 0, runFail = 0, unsupported = 0, disposed = 0, dialogs = 0;
        var failures = new List<string>();

        foreach (var file in files)
        {
            string src = File.ReadAllText(file);
            JsEngine engine;
            try
            {
                engine = new JsEngine(src, Path.GetFileName(file));
            }
            catch (Exception ex)
            {
                parseFail++;
                failures.Add("PARSE " + Path.GetFileName(file) + " :: " + Brief(ex));
                continue;
            }

            if (mode == "--parse") { ok++; continue; }

            try
            {
                RunScript(engine, src, out int dlg, out int disp);
                dialogs += dlg; disposed += disp;
                ok++;
            }
            catch (JsRuntimeException ex) when (ex.Message.Contains("Java interop"))
            {
                unsupported++;
            }
            catch (Exception ex)
            {
                runFail++;
                failures.Add("RUN   " + Path.GetFileName(file) + " :: " + Brief(ex));
            }
        }

        Console.WriteLine("mode=" + mode + " files=" + files.Count + " ok=" + ok +
                          " parseFail=" + parseFail + " runFail=" + runFail +
                          " javaInterop=" + unsupported + " dialogs=" + dialogs + " disposed=" + disposed);
        foreach (var f in failures) Console.WriteLine("  " + f);
        return parseFail + runFail == 0 ? 0 : 1;
    }

    // 验证 Quest.wz 结构与 QuestCatalog 的读法一致
    static int DumpQuests(string dataRoot)
    {
        using var chk = new MapleDash.Wz.ImgFile(Path.Combine(dataRoot, "Quest/Check.img"));
        using var act = new MapleDash.Wz.ImgFile(Path.Combine(dataRoot, "Quest/Act.img"));
        using var info = new MapleDash.Wz.ImgFile(Path.Combine(dataRoot, "Quest/QuestInfo.img"));

        int n = 0, withNpc = 0, withScript = 0, withItems = 0, withMobs = 0, withJobs = 0;
        foreach (var q in chk.Root.Children)
        {
            if (!int.TryParse(q.Name, out _)) continue;
            n++;
            if (q.Get("0/npc")?.Value != null) withNpc++;
            if (!string.IsNullOrEmpty(q.Text("0/startscript")) || !string.IsNullOrEmpty(q.Text("1/endscript"))) withScript++;
            if (q.Get("0/item")?.Children.Count > 0 || q.Get("1/item")?.Children.Count > 0) withItems++;
            if (q.Get("0/mob")?.Children.Count > 0 || q.Get("1/mob")?.Children.Count > 0) withMobs++;
            if (q.Get("0/job")?.Children.Count > 0) withJobs++;
        }
        Console.WriteLine($"quests={n} npc={withNpc} script={withScript} items={withItems} mobs={withMobs} job={withJobs}");

        foreach (var id in new[] { "1021", "20000", "2001" })
        {
            var q = chk.Root.Get(id);
            var a = act.Root.Get(id);
            var i = info.Root.Get(id);
            Console.WriteLine($"--- {id} ---");
            Console.WriteLine("  npc=" + (q?.Int("0/npc", -1)) + " level=" + (q?.Int("0/level", 0)) +
                              " startscript=" + q?.Text("0/startscript") + " endscript=" + q?.Text("1/endscript"));
            Console.WriteLine("  name=" + i?.Text("name") + " pages=" + (i == null ? 0 : i.Children.Count));
            if (q?.Get("0/item") != null)
                foreach (var it in q.Get("0/item").Children)
                    Console.WriteLine("   req item " + it.Get("id")?.Value + " x" + it.Get("count")?.Value);
            if (q?.Get("0/mob") != null)
                foreach (var it in q.Get("0/mob").Children)
                    Console.WriteLine("   req mob " + it.Get("id")?.Value + " x" + it.Get("count")?.Value);
            if (a?.Get("1/exp")?.Value != null) Console.WriteLine("   act exp " + a.Get("1/exp").Value);
            if (a?.Get("1/item") != null)
                foreach (var it in a.Get("1/item").Children)
                    Console.WriteLine("   give " + it.Get("id")?.Value + " x" + it.Get("count")?.Value + " prop=" + it.Get("prop")?.Value);
        }
        return 0;
    }

    static int DumpImg(string path, string node)
    {
        using var img = new MapleDash.Wz.ImgFile(path);
        var n = node.Length == 0 ? img.Root : img.Root.Get(node);
        if (n == null) { Console.WriteLine("node not found: " + node); return 1; }
        Console.WriteLine("node " + (n.Name ?? "/") + " kind=" + n.Kind + " children=" + n.Children.Count);
        foreach (var c in n.Children)
            Console.WriteLine("  " + c.Name + "  kind=" + c.Kind + " " + c.Width + "x" + c.Height +
                              " origin=" + c.X + "," + c.Y + " value=" + (c.Value ?? "") +
                              (c.Children.Count > 0 ? "  [" + c.Children.Count + "]" : ""));
        return 0;
    }

    static string Brief(Exception ex)
    {
        string m = ex.Message.Replace("\n", " ");
        return m.Length > 220 ? m.Substring(0, 220) + "…" : m;
    }

    static string DefaultScriptsDir()
    {
        string root = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && root != null; i++)
        {
            string candidate = Path.Combine(root, "BeiDou-Server", "gms-server", "scripts-zh-CN", "npc");
            if (Directory.Exists(candidate)) return candidate;
            root = Path.GetDirectoryName(root);
        }
        return "/Users/lizixian/Documents/mxd/BeiDou-Server/gms-server/scripts-zh-CN/npc";
    }

    // ————— 执行：模拟源端 NPCScriptManager / QuestScriptManager 的调用序列 —————
    static void RunScript(JsEngine engine, string source, out int dialogs, out int disposed)
    {
        var mock = new MockConversation(source);
        engine.Bind("cm", mock.Cm);
        engine.Bind("qm", mock.Cm);
        engine.Bind("pi", mock.Cm);
        engine.Bind("player", mock.Player);

        engine.Run();

        bool isQuest = source.Contains("qm.") && !source.Contains("cm.");
        if (engine.HasFunction("start"))
        {
            if (isQuest) engine.Call("start", new JsNumber(1), new JsNumber(0), new JsNumber(0));
            else engine.Call("start");
        }

        for (int i = 0; i < 14 && mock.DialogCount < 40; i++)
        {
            if (!engine.HasFunction("action") && !isQuest) break;
            double mode = i % 4 == 2 ? 0 : 1;
            double type = i % 2 == 0 ? 1 : 0;
            double selection = i % 3;
            try
            {
                if (isQuest) engine.Call("start", new JsNumber(mode), new JsNumber(type), new JsNumber(selection));
                else engine.Call("action", new JsNumber(mode), new JsNumber(type), new JsNumber(selection));
            }
            catch (JsRuntimeException) { break; }
            if (mock.Disposed) break;
        }

        if (isQuest && engine.HasFunction("end"))
        {
            mock.ResetDispose();
            try { engine.Call("end", new JsNumber(1), new JsNumber(0), new JsNumber(0)); }
            catch (JsRuntimeException) { }
        }

        dialogs = mock.DialogCount;
        disposed = mock.Disposed ? 1 : 0;
    }

    sealed class MockConversation
    {
        public readonly JsHostObject Cm;
        public readonly JsHostObject Player;
        public bool Disposed;
        public bool Trace;
        public int DialogCount;

        static readonly HashSet<string> ObjectGetters = new HashSet<string>(StringComparer.Ordinal)
        {
            "getClient", "getPlayer", "getMap", "getEventManager", "getEventInstance", "getParty",
            "getExpedition", "getInventory", "getStorage", "getAbstractPlayerInteraction", "getQuest",
            "getItem", "getCharacter", "getPosition", "getPartner", "getFamilyEntry", "getAreaInfos",
            "getEvents", "getChannelServer", "getInstanceMap", "getMapInstance", "getEventTeam",
            "getCashShop", "getDuey", "getHiredMerchant", "getMTS", "getBuffStatValueHolder",
            "getEquip", "getEquippedItem", "getPet", "getSummoned", "getDragon", "getSkill",
            "getInstance", "getEm", "getEim", "getValue", "getData", "getCharacters", "getChar",
            "getAllCharacters", "getPlayerCount", "getReactor", "getMonster", "getLevels", "getRecord",
        };

        public MockConversation(string source)
        {
            Player = NewHost();
            Cm = NewHost();
            Player.Set("getHp", FnOf(new JsNumber(50)));
            Player.Set("getMp", FnOf(new JsNumber(50)));
            Player.Set("getLevel", FnOf(new JsNumber(30)));
            Player.Set("getGender", FnOf(new JsNumber(0)));
            Player.Set("getName", FnOf(new JsString("Test")));
            Player.Set("getId", FnOf(new JsNumber(1)));
            Player.Set("getJobId", FnOf(new JsNumber(0)));
            Player.Set("getMapId", FnOf(new JsNumber(1000000)));
            Player.Set("getMeso", FnOf(new JsNumber(100000)));

            Cm.Set("getPlayer", JsFunction.FromNative("getPlayer", c => Player));
            Cm.Set("getMap", JsFunction.FromNative("getMap", c => NewHost()));
            Cm.Set("c", NewHost().With("getPlayer", JsFunction.FromNative("getPlayer", c => Player)));

            // 预先登记脚本里出现的 cm/qm 成员，未知成员由 Missing 兜底。
            foreach (Match m in Regex.Matches(source, @"\b(?:cm|qm|pi)\.([A-Za-z_][A-Za-z0-9_]*)"))
                if (!Cm.Members.Has(m.Groups[1].Value)) Cm.Set(m.Groups[1].Value, DefaultFn(m.Groups[1].Value));
        }

        public void ResetDispose() => Disposed = false;

        JsHostObject NewHost()
        {
            var h = new JsHostObject(this);
            h.Missing = key => DefaultFn(key);
            return h;
        }

        static JsFunction FnOf(JsValue v) => JsFunction.FromNative("const", c => v);

        void ResetDialog() { }

        JsFunction DefaultFn(string name)
        {
            return JsFunction.FromNative(name, c =>
            {
                if (name == "dispose") { Disposed = true; return JsUndefined.Value; }
                if (name.StartsWith("send"))
                {
                    DialogCount++;
                    if (Trace) Console.WriteLine("   " + name + "(" + Brief1(JsConvert.ToString(c.Arg(0))) + ")");
                    return JsUndefined.Value;
                }
                if (Trace && (name == "warp" || name == "gainItem" || name == "gainMeso" || name == "gainExp" ||
                              name == "forceStartQuest" || name == "forceCompleteQuest" || name == "setQuestProgress"))
                    Console.WriteLine("   " + name + "(" + Brief1(JsConvert.ToString(c.Arg(0))) + (c.ArgCount > 1 ? ", " + Brief1(JsConvert.ToString(c.Arg(1))) : "") + ")");
                if (name.StartsWith("is") || name.StartsWith("has") || name.StartsWith("can") || name.StartsWith("have"))
                    return JsBoolean.False;
                if (ObjectGetters.Contains(name)) return NewHost();
                if (name.StartsWith("get") || name.StartsWith("item") || name.StartsWith("count") || name.StartsWith("size"))
                    return new JsNumber(0);
                if (name == "equals") return JsBoolean.False;
                return JsUndefined.Value;
            });
        }
    }

    static string Brief1(string s)
    {
        s = (s ?? "").Replace("\r", "\\r").Replace("\n", "\\n");
        return s.Length > 90 ? s.Substring(0, 90) + "…" : s;
    }

    static JsHostObject With(this JsHostObject o, string key, JsValue v)
    {
        o.Set(key, v);
        return o;
    }
}
