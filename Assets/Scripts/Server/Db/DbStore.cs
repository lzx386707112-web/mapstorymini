// 嵌入式数据库：表结构复刻源端 Schema.sql，行数据存 JSON 文件。
//
// 为什么不是 SQLite/MySQL：
//   - 源端是 MySQL（beidou 库），本地单机版没有数据库进程；IL2CPP 也没有原生 sqlite 依赖。
//   - 但「表结构完全和源端一样」是硬需求 → 本类直接解析 Schema.sql，
//     用同一套表名/列名/类型/默认值建表，行存取走内存 + JSON 持久化。
//   - 接口形状（Insert/Update/Delete/Select/事务）按 IDbConnection 习惯设计，
//     将来要接真库时只替换本文件，Dao 与业务层零改动。
//
// 约束：
//   - 纯 C#，不引用 UnityEngine（可被 tools/ 离线单测）。
//   - 自增 id、CURRENT_TIMESTAMP 由本类模拟（源端由 MySQL 给）。
//   - 事务 = 快照式：BeginTransaction 记全库快照，Rollback 还原。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

public enum DbType { Integer, Real, Text }

public sealed class DbColumn
{
    public string name;
    public DbType type;
    public bool notNull;
    public bool primaryKey;
    public bool autoIncrement;
    public object defaultValue;   // 已按类型转换；null = 无默认值
}

public sealed class DbRow : Dictionary<string, object> { }

public sealed class DbTable
{
    public string name;
    public List<DbColumn> columns = new List<DbColumn>();
    public List<DbRow> rows = new List<DbRow>();
    public long nextId = 1;

    public DbColumn Col(string n)
    {
        foreach (var c in columns) if (c.name == n) return c;
        return null;
    }

    // 源端 MySQL 语义：缺列补默认值；自增主键由存储分配。
    public long Insert(DbRow values)
    {
        var row = new DbRow();
        foreach (var c in columns)
        {
            if (c.primaryKey && c.autoIncrement && (!values.ContainsKey(c.name) || values[c.name] == null))
            {
                row[c.name] = nextId;
                continue;
            }
            if (values.TryGetValue(c.name, out var v) && v != null)
            {
                row[c.name] = Coerce(c, v);
                continue;
            }
            row[c.name] = c.defaultValue ?? (c.notNull || c.primaryKey ? DefaultFor(c.type) : null);
        }
        rows.Add(row);
        string primary = PrimaryName;
        long id = primary != null && row.ContainsKey(primary) ? Convert.ToInt64(row[primary], CultureInfo.InvariantCulture) : 0;
        if (id >= nextId) nextId = id + 1;
        return id;
    }

    public int Update(Func<DbRow, bool> where, DbRow values)
    {
        int n = 0;
        foreach (var r in rows)
        {
            if (!where(r)) continue;
            foreach (var kv in values)
            {
                var c = Col(kv.Key);
                if (c == null) continue;
                r[kv.Key] = Coerce(c, kv.Value);
            }
            n++;
        }
        return n;
    }

    public int Delete(Func<DbRow, bool> where)
    {
        return rows.RemoveAll(r => where(r));
    }

    public List<DbRow> Select(Func<DbRow, bool> where)
    {
        var outp = new List<DbRow>();
        foreach (var r in rows) if (where == null || where(r)) outp.Add(r);
        return outp;
    }

    public DbRow Find(Func<DbRow, bool> where)
    {
        foreach (var r in rows) if (where(r)) return r;
        return null;
    }

    public string PrimaryName
    {
        get
        {
            foreach (var c in columns) if (c.primaryKey) return c.name;
            return null;
        }
    }

    static object DefaultFor(DbType t)
    {
        switch (t)
        {
            case DbType.Integer: return 0L;
            case DbType.Real: return 0d;
            default: return string.Empty;
        }
    }

    public static object Coerce(DbColumn c, object v)
    {
        if (v == null) return null;
        switch (c.type)
        {
            case DbType.Integer:
                if (v is bool b) return b ? 1L : 0L;
                try { return Convert.ToInt64(v, CultureInfo.InvariantCulture); }
                catch { return 0L; }
            case DbType.Real:
                try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); }
                catch { return 0d; }
            default:
                return v is string s ? s : v.ToString();
        }
    }
}

public sealed class DbStore
{
    public const int SchemaVersion = 1;

    readonly Dictionary<string, DbTable> tables = new Dictionary<string, DbTable>();
    string path;
    bool dirty;
    List<Dictionary<string, DbTable>> txStack;   // 事务快照（浅拷贝表，行对象共享 -> 行级写入前克隆）
    List<Dictionary<string, List<DbRow>>> txRows;

    public string FilePath => path;
    public bool Dirty => dirty;

    // —————————————————— 建库 / 读库 ——————————————————

    public static DbStore Create(string dbPath)
    {
        var store = new DbStore { path = dbPath };
        store.LoadSchema();
        return store;
    }

    public static DbStore Open(string dbPath)
    {
        var store = new DbStore { path = dbPath };
        store.LoadSchema();
        if (File.Exists(dbPath)) store.ReadJson(File.ReadAllText(dbPath));
        else { store.Save(); }
        return store;
    }

    void LoadSchema()
    {
        // Schema.sql 走 Server 统一资源读取（Unity Resources 或文件系统）
        string sql = PlatformRes.LoadText("Server/Db/Schema");
        if (string.IsNullOrEmpty(sql)) throw new InvalidOperationException("Schema.sql 缺失");
        Parse(sql);
    }

    // —————————————————— Schema 解析 ——————————————————

    void Parse(string sql)
    {
        // 去掉行注释，按 ; 切语句
        var sb = new StringBuilder();
        foreach (var line in sql.Replace("\r\n", "\n").Split('\n'))
        {
            var t = line.Trim();
            if (t.StartsWith("--")) continue;
            sb.Append(line).Append('\n');
        }
        foreach (var stmt in sb.ToString().Split(';'))
        {
            var s = stmt.Trim();
            if (s.Length == 0) continue;
            if (s.StartsWith("CREATE TABLE", StringComparison.OrdinalIgnoreCase)) ParseCreate(s);
            else if (s.StartsWith("INSERT INTO", StringComparison.OrdinalIgnoreCase)) ParseInsert(s);
        }
    }

    void ParseCreate(string stmt)
    {
        int p = stmt.IndexOf('(');
        string head = stmt.Substring(0, p).Trim();
        string name = head.Substring(head.IndexOf("TABLE", StringComparison.OrdinalIgnoreCase) + 5).Trim();
        string body = stmt.Substring(p + 1, stmt.LastIndexOf(')') - p - 1);

        var table = new DbTable { name = name };
        foreach (var raw in SplitTop(body))
        {
            var def = raw.Trim();
            if (def.Length == 0) continue;
            var upper = def.ToUpperInvariant();
            // 跳过索引/约束定义（UNIQUE(...)、KEY `x`(...)、PRIMARY KEY(...)、INDEX(...)、CONSTRAINT…）。
            // 注意：MySQL 的 KEY 后面一定跟空格/反引号/括号，列名 keymap 不会被误伤。
            if (upper.StartsWith("UNIQUE") || upper.StartsWith("PRIMARY KEY") || upper.StartsWith("FOREIGN KEY") ||
                upper.StartsWith("CONSTRAINT") || upper.StartsWith("INDEX") || upper.StartsWith("FULLTEXT") ||
                (upper.StartsWith("KEY") && upper.Length > 3 &&
                    !upper.Substring(3).TrimStart().StartsWith("INTEGER", StringComparison.Ordinal) &&
                    (char.IsWhiteSpace(upper[3]) || upper[3] == '`' || upper[3] == '(')))
                continue;

            // 列名 类型 [约束...]
            int sp = def.IndexOf(' ');
            var col = new DbColumn { name = def.Substring(0, sp).Trim('`', '"', ' ') };
            string rest = def.Substring(sp + 1).Trim();
            int sp2 = rest.IndexOf(' ');
            string typeName = (sp2 < 0 ? rest : rest.Substring(0, sp2)).ToUpperInvariant();
            col.type = typeName.Contains("INT") ? DbType.Integer
                     : typeName.Contains("REAL") || typeName.Contains("FLOAT") || typeName.Contains("DOUBLE") ? DbType.Real
                     : DbType.Text;
            string mods = (sp2 < 0 ? "" : rest.Substring(sp2 + 1)).ToUpperInvariant();
            col.notNull = mods.Contains("NOT NULL");
            col.primaryKey = mods.Contains("PRIMARY KEY");
            col.autoIncrement = mods.Contains("AUTOINCREMENT");
            int di = mods.IndexOf("DEFAULT ", StringComparison.Ordinal);
            if (di >= 0)
            {
                string d = mods.Substring(di + 8).Trim();
                if (d.StartsWith("'"))
                {
                    int q = d.IndexOf('\'', 1);
                    col.defaultValue = d.Substring(1, q - 1);
                }
                else
                {
                    int e = d.IndexOf(' ');
                    string tok = e < 0 ? d : d.Substring(0, e);
                    if (tok.StartsWith("CURRENT_TIMESTAMP", StringComparison.OrdinalIgnoreCase))
                        col.defaultValue = NowText();
                    else if (col.type == DbType.Integer) col.defaultValue = ParseLong(tok);
                    else if (col.type == DbType.Real) col.defaultValue = ParseDouble(tok);
                    else col.defaultValue = tok;
                }
            }
            table.columns.Add(col);
        }
        if (table.columns.Count == 0) throw new InvalidOperationException("Schema 解析失败: " + name);
        tables[name] = table;
    }

    void ParseInsert(string stmt)
    {
        // INSERT INTO t (c1, c2, ...) VALUES (v1, v2, ...), (v1, v2, ...), ...;
        // 支持单行与多行 VALUES（多行是 game_config 等种子数据的写法；此前只取第一组、
        // 且会把跨组的逗号当成列分隔，导致后面几组种子丢失、末列串进下一组——已修）。
        int p = stmt.IndexOf('(');
        string head = stmt.Substring(0, p);
        string name = head.Substring(head.IndexOf("INTO", StringComparison.OrdinalIgnoreCase) + 4).Trim();
        string colsPart = stmt.Substring(p + 1, stmt.IndexOf(')') - p - 1);
        var cols = new List<string>();
        foreach (var c in SplitTop(colsPart)) cols.Add(c.Trim().Trim('`', '"', ' '));

        int vp = stmt.IndexOf("VALUES", StringComparison.OrdinalIgnoreCase);
        string valsPart = stmt.Substring(vp + 6).Trim();
        foreach (var tuple in SplitTuples(valsPart))
        {
            var vals = SplitTop(tuple);
            var table = Table(name);
            var row = new DbRow();
            for (int i = 0; i < cols.Count && i < vals.Count; i++)
            {
                var c = table.Col(cols[i]);
                if (c == null) continue;
                row[c.name] = DbTable.Coerce(c, Unquote(vals[i].Trim()));
            }
            table.Insert(row);
        }
    }

    // 把 "a,b),(c,d),..." 按顶层括号切成每组的内层文本（不含最外层括号）
    static List<string> SplitTuples(string valsPart)
    {
        var outp = new List<string>();
        int depth = 0, start = -1;
        bool inStr = false;
        for (int i = 0; i < valsPart.Length; i++)
        {
            char ch = valsPart[i];
            if (inStr) { if (ch == '\'') inStr = false; continue; }
            if (ch == '\'') { inStr = true; continue; }
            if (ch == '(') { if (depth == 0) start = i + 1; depth++; continue; }
            if (ch == ')')
            {
                depth--;
                if (depth == 0 && start >= 0) { outp.Add(valsPart.Substring(start, i - start)); start = -1; }
                if (depth < 0) depth = 0;
            }
        }
        return outp;
    }

    static string Unquote(string v)
    {
        if (v.Length >= 2 && v[0] == '\'' && v[v.Length - 1] == '\'') return v.Substring(1, v.Length - 2).Replace("''", "'");
        return v;
    }

    static long ParseLong(string s) => long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0L;
    static double ParseDouble(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0d;

    // 按顶层逗号切分（忽略引号内）
    static List<string> SplitTop(string body)
    {
        var outp = new List<string>();
        int depth = 0;
        bool inStr = false;
        var cur = new StringBuilder();
        foreach (var ch in body)
        {
            if (inStr) { cur.Append(ch); if (ch == '\'') inStr = false; continue; }
            if (ch == '\'') { inStr = true; cur.Append(ch); continue; }
            if (ch == '(') depth++;
            if (ch == ')') depth--;
            if (ch == ',' && depth == 0) { outp.Add(cur.ToString()); cur.Clear(); continue; }
            cur.Append(ch);
        }
        if (cur.Length > 0) outp.Add(cur.ToString());
        return outp;
    }

    // —————————————————— 表访问 ——————————————————

    public DbTable Table(string name)
    {
        if (!tables.TryGetValue(name, out var t)) throw new KeyNotFoundException("表不存在: " + name);
        return t;
    }

    public bool HasTable(string name) => tables.ContainsKey(name);

    public IEnumerable<string> TableNames => tables.Keys;

    // —————————————————— 事务 ——————————————————

    public void BeginTransaction()
    {
        if (txStack == null) { txStack = new List<Dictionary<string, DbTable>>(); txRows = new List<Dictionary<string, List<DbRow>>>(); }
        var snap = new Dictionary<string, DbTable>();
        var rowsSnap = new Dictionary<string, List<DbRow>>();
        foreach (var kv in tables)
        {
            snap[kv.Key] = kv.Value;
            rowsSnap[kv.Key] = new List<DbRow>();
            foreach (var row in kv.Value.rows)
            {
                var copy = new DbRow();
                foreach (var cell in row) copy[cell.Key] = cell.Value;
                rowsSnap[kv.Key].Add(copy);
            }
        }
        txStack.Add(snap);
        txRows.Add(rowsSnap);
    }

    public void Commit()
    {
        if (txStack == null || txStack.Count == 0) return;
        int last = txStack.Count - 1;
        txStack.RemoveAt(last);
        txRows.RemoveAt(last);
        dirty = true;
    }

    public void Rollback()
    {
        if (txStack == null || txStack.Count == 0) return;
        int last = txStack.Count - 1;
        var snap = txStack[last];
        var rowsSnap = txRows[last];
        foreach (var kv in snap)
        {
            kv.Value.rows.Clear();
            kv.Value.rows.AddRange(rowsSnap[kv.Key]);
        }
        txStack.RemoveAt(last);
        txRows.RemoveAt(last);
        dirty = true;
    }

    // —————————————————— 持久化 ——————————————————

    public void Save()
    {
        var sb = new StringBuilder();
        sb.Append("{\"version\":").Append(SchemaVersion).Append(",\"tables\":{");
        bool first = true;
        foreach (var kv in tables)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('"').Append(kv.Key).Append("\":{\"nextId\":").Append(kv.Value.nextId).Append(",\"rows\":[");
            for (int i = 0; i < kv.Value.rows.Count; i++)
            {
                if (i > 0) sb.Append(',');
                WriteRow(sb, kv.Value, kv.Value.rows[i]);
            }
            sb.Append("]}");
        }
        sb.Append("}");
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, sb.ToString());
        dirty = false;
    }

    static void WriteRow(StringBuilder sb, DbTable t, DbRow row)
    {
        sb.Append('{');
        bool first = true;
        foreach (var c in t.columns)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('"').Append(c.name).Append("\":");
            row.TryGetValue(c.name, out var v);
            WriteValue(sb, v);
        }
        sb.Append('}');
    }

    static void WriteValue(StringBuilder sb, object v)
    {
        if (v == null) { sb.Append("null"); return; }
        if (v is long l) { sb.Append(l.ToString(CultureInfo.InvariantCulture)); return; }
        if (v is int i) { sb.Append(i.ToString(CultureInfo.InvariantCulture)); return; }
        if (v is double d) { sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); return; }
        if (v is bool b) { sb.Append(b ? "1" : "0"); return; }
        sb.Append('"').Append(Escape(v.ToString())).Append('"');
    }

    static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");

    public void ReadJson(string json)
    {
        var root = MiniJsonDb.Parse(json) as Dictionary<string, object>;
        if (root == null) return;
        if (root.TryGetValue("tables", out var t) && t is Dictionary<string, object> tabs)
        {
            foreach (var kv in tabs)
            {
                if (!tables.TryGetValue(kv.Key, out var table)) continue;   // schema 没有的表忽略
                if (!(kv.Value is Dictionary<string, object> td)) continue;
                if (td.TryGetValue("nextId", out var nid) && nid != null)
                    table.nextId = Convert.ToInt64(nid, CultureInfo.InvariantCulture);
                table.rows.Clear();
                if (td.TryGetValue("rows", out var rows) && rows is List<object> list)
                {
                    foreach (var item in list)
                    {
                        if (!(item is Dictionary<string, object> rd)) continue;
                        var row = new DbRow();
                        foreach (var c in table.columns)
                        {
                            if (!rd.TryGetValue(c.name, out var v)) continue;
                            row[c.name] = v == null ? null : DbTable.Coerce(c, v);
                        }
                        table.rows.Add(row);
                    }
                }
            }
        }
    }

    public static string NowText() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    // 查询辅助（Dao 里少写 lambda 噪音）
    public static Func<DbRow, bool> Eq(string col, object val)
    {
        return r => r.TryGetValue(col, out var v) && Equals(v, DbTable.Coerce(new DbColumn { type = GuessType(val) }, val));
    }

    static DbType GuessType(object v)
    {
        if (v is long || v is int || v is bool) return DbType.Integer;
        if (v is double || v is float) return DbType.Real;
        return DbType.Text;
    }

    // ——— 取值辅助：JSON 反序列化后数值可能是 long/double/string，Dao 不该关心 ———

    public static long Long(DbRow r, string col, long fallback = 0)
    {
        if (r == null || !r.TryGetValue(col, out var v) || v == null) return fallback;
        try { return Convert.ToInt64(v, CultureInfo.InvariantCulture); }
        catch { return fallback; }
    }

    public static int Int(DbRow r, string col, int fallback = 0)
        => (int)Long(r, col, fallback);

    public static double Dbl(DbRow r, string col, double fallback = 0d)
    {
        if (r == null || !r.TryGetValue(col, out var v) || v == null) return fallback;
        try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); }
        catch { return fallback; }
    }

    public static string Str(DbRow r, string col, string fallback = "")
    {
        if (r == null || !r.TryGetValue(col, out var v) || v == null) return fallback;
        return v is string s ? s : v.ToString();
    }
}
