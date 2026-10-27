using System;
using System.Collections.Generic;

namespace Server.Bot
{
    // SoloMaplingResource 的Android适配：保留源路径，统一从打包JSON读取。
    public sealed class BotResources
    {
        readonly Dictionary<string, object> resources;
        public BotResources(string json) : this(json == null ? null : MiniJsonDb.Parse(json) as Dictionary<string, object>) { }
        public BotResources(Dictionary<string, object> root)
        {
            resources = root != null && root.TryGetValue("resources", out var value) ? value as Dictionary<string, object> : null;
            if (resources == null) throw new ArgumentException("SoloMapling resource pack is missing or invalid");
        }
        public static BotResources Load() => new BotResources(BotWarmup.Root("Bot/SoloMapling"));
        // 原始文件可直接取回；解析失败不影响原字节的保存与读取。
        public byte[] ReadBytes(string path)
        {
            if (resources.TryGetValue(path, out var value) && value is Dictionary<string, object> entry
                && entry.TryGetValue("base64", out var encoded) && encoded is string bytes)
                return Convert.FromBase64String(bytes);
            return null;
        }
        public string ReadText(string path)
        {
            if (resources.TryGetValue(path, out var value) && value is Dictionary<string, object> entry)
            {
                if (entry.TryGetValue("error", out var error)) throw new System.IO.InvalidDataException(path + ": " + error);
                if (entry.TryGetValue("text", out var text)) return text as string;
            }
            return null;
        }
        public object ReadData(string path)
        {
            if (resources.TryGetValue(path, out var value) && value is Dictionary<string, object> entry)
            {
                if (entry.TryGetValue("error", out var error)) throw new System.IO.InvalidDataException(path + ": " + error);
                if (entry.TryGetValue("data", out var data)) return data;
            }
            return null;
        }
        public List<string> ListFileStems(string directory, string extension = ".csv")
        {
            string prefix = directory.TrimEnd('/') + "/";
            var stems = new List<string>();
            foreach (string path in resources.Keys)
                if (path.StartsWith(prefix, StringComparison.Ordinal) && path.EndsWith(extension, StringComparison.Ordinal))
                    stems.Add(path.Substring(prefix.Length, path.Length - prefix.Length - extension.Length));
            return stems;
        }
    }
}
