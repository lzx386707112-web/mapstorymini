using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

// 资源包可以是一个 ZIP，也可以是打包脚本按 2 GiB 切出来的多个**独立** ZIP：
//   MapleDashResources.part01.zip / part02.zip / ...
// 每个分包都是结构完整的 ZIP（逐包解压即可，不需要先拼回 9 GiB 整包），
// 根目录都带 resource-pack.json 写明 index/count，只有第 1 包带 resource-manifest.json。
//
// 为什么不在手机端把分包拼成一个大数据流再解压：拼接要额外一整份 9 GiB 空间，
// 而且 Android/Mono 解析超大 ZIP64 中央目录本来就不可靠。独立分包没有这两个问题。
//
// 缺包、包号重复/不连续、单包与分包混放、清单不在第 1 包，都会在**开始解压前**报错，
// 不会留下解压到一半的 Data。
public sealed class WzResourceParts : IDisposable
{
    public const string InfoEntry = "resource-pack.json";
    public const string ManifestEntry = "resource-manifest.json";
    public const string PatchEntry = "resource-patch.json";
    const int ManifestLimit = 64 * 1024 * 1024;

    sealed class Member
    {
        public string Path;
        public int Number;
        public ZipArchive Archive;
    }

    readonly List<Member> members = new List<Member>();
    Dictionary<string, ZipArchiveEntry> lookup;
    string manifest;
    int dataEntries = -1;

    WzResourceParts() { }

    public int PartCount => members.Count;
    public bool IsSplit => members.Count > 1;
    public string PartPath(int index) => members[index].Path;
    public int PartNumber(int index) => members[index].Number;
    public ZipArchive PartArchive(int index) => members[index].Archive;

    // 资源包是否存在（单包或分包任一种）。
    public static bool Exists(string baseZipPath) => Discover(baseZipPath).Length > 0;

    // 按包号列出这个资源包由哪些文件组成；不存在时返回空数组。
    // 单包与分包同时存在时取修改时间较新的一份：手机里残留上一版整包时，
    // 不能让它把刚传上去的新分包顶掉（否则会装上旧资源）。
    public static string[] Discover(string baseZipPath)
    {
        string directory = System.IO.Path.GetDirectoryName(baseZipPath);
        if (string.IsNullOrEmpty(directory)) directory = ".";
        string stem = System.IO.Path.GetFileNameWithoutExtension(baseZipPath);
        var parts = new List<KeyValuePair<int, string>>();
        if (Directory.Exists(directory))
            foreach (string path in Directory.GetFiles(directory, stem + ".part*.zip"))
                if (TryPartNumber(System.IO.Path.GetFileName(path), stem, out int number))
                    parts.Add(new KeyValuePair<int, string>(number, path));
        bool single = File.Exists(baseZipPath);
        if (parts.Count == 0) return single ? new[] { baseZipPath } : Array.Empty<string>();
        parts.Sort((a, b) => a.Key.CompareTo(b.Key));
        if (single)
        {
            DateTime newest = DateTime.MinValue;
            foreach (var part in parts)
            {
                DateTime written = File.GetLastWriteTimeUtc(part.Value);
                if (written > newest) newest = written;
            }
            if (File.GetLastWriteTimeUtc(baseZipPath) > newest) return new[] { baseZipPath };
        }
        var result = new string[parts.Count];
        for (int i = 0; i < parts.Count; i++) result[i] = parts[i].Value;
        return result;
    }

    // stem.partNN.zip；其他命名返回 false，避免把目录里的杂项当成分包。
    static bool TryPartNumber(string name, string stem, out int number)
    {
        number = 0;
        string head = stem + ".part", tail = ".zip";
        if (name.Length <= head.Length + tail.Length) return false;
        if (!name.StartsWith(head, StringComparison.Ordinal) ||
            !name.EndsWith(tail, StringComparison.OrdinalIgnoreCase)) return false;
        string digits = name.Substring(head.Length, name.Length - head.Length - tail.Length);
        foreach (char c in digits) if (c < '0' || c > '9') return false;
        return int.TryParse(digits, out number) && number > 0;
    }

    public static WzResourceParts Open(string baseZipPath)
    {
        string[] paths = Discover(baseZipPath);
        return paths.Length == 0 ? null : Open(paths);
    }

    public static WzResourceParts Open(string[] paths)
    {
        var pack = new WzResourceParts();
        try
        {
            var counts = new List<int>();
            foreach (string path in paths)
            {
                var member = new Member { Path = path, Number = 1 };   // 老式整包没有描述文件，按第 1 包算
                try
                {
                    member.Archive = ZipFile.OpenRead(path);
                }
                catch (Exception error) when (error is InvalidDataException || error is IOException ||
                                              error is UnauthorizedAccessException)
                {
                    throw new InvalidDataException("资源包 " + System.IO.Path.GetFileName(path) +
                        " 打不开（分包没传完整或已损坏），请重新传输该包", error);
                }
                pack.members.Add(member);
                int count = 1;
                var info = member.Archive.GetEntry(InfoEntry);
                if (info != null) ReadInfo(info, member.Path, out member.Number, out count);
                counts.Add(count);
            }
            pack.Validate(paths.Length, counts);
            return pack;
        }
        catch
        {
            pack.Dispose();
            throw;
        }
    }

    static void ReadInfo(ZipArchiveEntry entry, string path, out int index, out int count)
    {
        if (entry.Length > 1024 * 1024)
            throw new InvalidDataException("分包描述文件异常: " + System.IO.Path.GetFileName(path));
        string text;
        using (var reader = new StreamReader(entry.Open(), Encoding.UTF8)) text = reader.ReadToEnd();
        index = count = 0;
        try
        {
            if (!(MiniJson.Parse(text) is Dictionary<string, object> info)) throw new FormatException();
            if (info.TryGetValue("index", out object i)) index = Convert.ToInt32(i);
            if (info.TryGetValue("count", out object c)) count = Convert.ToInt32(c);
        }
        catch (Exception error) when (error is FormatException || error is InvalidCastException ||
                                      error is OverflowException || error is ArgumentException)
        {
            throw new InvalidDataException("分包描述文件损坏: " + System.IO.Path.GetFileName(path), error);
        }
        if (index < 1 || count < 1 || index > count)
            throw new InvalidDataException("分包描述包号非法: " + System.IO.Path.GetFileName(path));
    }

    // 包号必须正好覆盖 1..N，且各包声明的总包数一致；清单必须在第 1 包里。
    // 这些条件不满足时宁可拒绝解压，也不要在手机上装出一份缺文件的 Data。
    void Validate(int found, List<int> counts)
    {
        foreach (int count in counts)
            if (count != found)
                throw new InvalidDataException("资源包不完整：分包声明共 " + count + " 包，实际只找到 " +
                    found + " 包（" + MissingNames(count, found) + "），请把所有分包都传到同一目录");
        members.Sort((a, b) => a.Number.CompareTo(b.Number));
        var seen = new HashSet<int>();
        foreach (var member in members)
            if (!seen.Add(member.Number))
                throw new InvalidDataException("分包包号重复：" + member.Number + "（同名不同号的旧包残留？）");
        for (int i = 0; i < members.Count; i++)
            if (members[i].Number != i + 1)
                throw new InvalidDataException("分包包号不连续：缺少第 " + (i + 1) + " 包");
        if (members[0].Archive.GetEntry(ManifestEntry) == null)
            throw new InvalidDataException("资源包缺少清单（第 1 包里没有 " + ManifestEntry +
                "）：请用 tools/pack_resources.sh 重新打包");
    }

    static string MissingNames(int declared, int found)
    {
        var list = new List<string>();
        for (int i = found + 1; i <= declared && list.Count < 3; i++) list.Add("part" + i.ToString("00"));
        return list.Count == 0 ? "检查是否被覆盖" : "缺 " + string.Join("、", list);
    }

    // 第 1 包里的完整清单（与 APK 内嵌清单同源），只读一次。
    public string ManifestText
    {
        get
        {
            if (manifest != null) return manifest;
            var entry = members[0].Archive.GetEntry(ManifestEntry);
            if (entry == null || entry.Length > ManifestLimit)
                throw new InvalidDataException("资源包缺少有效清单");
            using (var reader = new StreamReader(entry.Open(), Encoding.UTF8)) manifest = reader.ReadToEnd();
            if (!(MiniJson.Parse(manifest) is Dictionary<string, object> index) ||
                !(index["files"] is List<object> files) || files.Count == 0)
                throw new InvalidDataException("资源清单没有文件");
            return manifest;
        }
    }

    // 增量补丁包：带 resource-patch.json 标记（只可能出现在第 1 包）。
    public bool IsPatch => members[0].Archive.GetEntry(PatchEntry) != null;

    // 所有分包里 Data/ 条目总数，用来算解压进度。
    public int DataEntryCount
    {
        get
        {
            if (dataEntries >= 0) return dataEntries;
            int total = 0;
            foreach (var member in members)
                foreach (var entry in member.Archive.Entries)
                    if (entry.FullName.StartsWith("Data/", StringComparison.Ordinal) &&
                        !entry.FullName.EndsWith("/", StringComparison.Ordinal)) total++;
            dataEntries = total;
            return total;
        }
    }

    // 按名跨分包查条目（Mono 的 GetEntry 是线性扫描，5.6 万条要建表才不卡）。
    public ZipArchiveEntry FindEntry(string name)
    {
        if (lookup == null)
        {
            lookup = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
            foreach (var member in members)
                foreach (var entry in member.Archive.Entries)
                    if (!entry.FullName.EndsWith("/", StringComparison.Ordinal) &&
                        !lookup.ContainsKey(entry.FullName)) lookup[entry.FullName] = entry;
        }
        return lookup.TryGetValue(name, out ZipArchiveEntry found) ? found : null;
    }

    public void Dispose()
    {
        foreach (var member in members)
            try { member.Archive?.Dispose(); }
            catch (IOException) { }
        members.Clear();
        lookup = null;
    }
}
