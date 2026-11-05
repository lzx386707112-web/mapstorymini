#!/usr/bin/env python3
"""把源端 gms-server 的 drop_data / drop_data_global 迁移全量重放成一份运行期掉落表。

背景：本工程运行期只带 `Field/FieldDrops.json`（9 只怪的小样本），二转测试图
（108000100/108000200/108000300…）里的 9000001/9000002… 根本没有任何掉落行，
所以 4031013「黑色大理石」永远不掉。这里按源端迁移顺序把整张表重放出来，
导出成紧凑文本给 FieldCatalog 读（DB 里的行仍然可以覆盖，后台改掉落不受影响）。

实现：用 Python 内置 sqlite3 当执行引擎，把 MySQL 方言改写成 sqlite 能跑的形态。
只跑涉及 drop_data / drop_data_global 的语句（外加它们依赖的临时表），
其它表（shops/shopitems…）与本次导出无关，直接跳过。

用法：
    python3 tools/export_field_drops.py            # 重新导出
    python3 tools/export_field_drops.py --verify   # 只校验现成产物
"""
from __future__ import annotations

import glob
import os
import re
import sqlite3
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SERVER = os.path.expanduser("~/Documents/mxd/BeiDou-Server/gms-server")
MIGRATIONS = os.path.join(SERVER, "src/main/resources/db/migration")
TARGET = os.path.join(ROOT, "Assets/Resources/Field/FieldDropTable.txt")

SCHEMA = [
    """CREATE TABLE drop_data (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        dropperid INTEGER NOT NULL,
        itemid INTEGER NOT NULL DEFAULT 0,
        minimum_quantity INTEGER NOT NULL DEFAULT 1,
        maximum_quantity INTEGER NOT NULL DEFAULT 1,
        questid INTEGER NOT NULL DEFAULT 0,
        chance INTEGER NOT NULL DEFAULT 0,
        UNIQUE (dropperid, itemid))""",
    """CREATE TABLE drop_data_global (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        continent INTEGER NOT NULL DEFAULT -1,
        itemid INTEGER NOT NULL DEFAULT 0,
        minimum_quantity INTEGER NOT NULL DEFAULT 1,
        maximum_quantity INTEGER NOT NULL DEFAULT 1,
        questid INTEGER NOT NULL DEFAULT 0,
        chance INTEGER NOT NULL DEFAULT 0,
        comments TEXT,
        UNIQUE (continent, itemid))""",
]


def split_statements(sql: str) -> list[str]:
    """按 ; 切语句，跳 -- / /* */ 注释，保留引号内容。"""
    out, cur = [], []
    i, n = 0, len(sql)
    quote = None
    while i < n:
        ch = sql[i]
        if quote:
            cur.append(ch)
            if ch == quote:
                quote = None
            i += 1
            continue
        if ch in "'\"`":
            quote = ch
            cur.append(ch)
            i += 1
            continue
        if sql.startswith("--", i) or ch == "#":
            j = sql.find("\n", i)
            i = n if j < 0 else j
            continue
        if sql.startswith("/*", i):
            j = sql.find("*/", i + 2)
            i = n if j < 0 else j + 2
            continue
        if ch == ";":
            out.append("".join(cur))
            cur = []
            i += 1
            continue
        cur.append(ch)
        i += 1
    if "".join(cur).strip():
        out.append("".join(cur))
    return out


def mysql_to_sqlite(stmt: str) -> str:
    s = stmt.strip()
    s = re.sub(r"(?i)^insert\s+ignore\s+into", "INSERT OR IGNORE INTO", s)
    s = re.sub(r"(?i)^insert\s+into", "INSERT INTO", s)
    s = re.sub(r"(?i)^update\s+ignore", "UPDATE OR IGNORE", s)
    s = re.sub(r"(?i)^replace\s+into", "REPLACE INTO", s)
    # 多表 JOIN 删除 → EXISTS 子查询
    m = re.match(r"(?is)^delete\s+target\s+from\s+`?(\w+)`?\s+as\s+target\s+inner\s+join\s+`?(\w+)`?\s+as\s+removed\s+on\s+(.+)$", s)
    if m:
        table, joined, cond = m.group(1), m.group(2), m.group(3).strip()
        cond = cond.replace("target.itemid", f"{table}.itemid")
        cond = cond.replace("target.", f"{table}.")
        cond = cond.replace("removed.", f"{joined}.")
        return f"DELETE FROM {table} WHERE EXISTS (SELECT 1 FROM {joined} WHERE {cond})"
    s = re.sub(r"(?i)\bLEAST\s*\(", "min(", s)
    s = re.sub(r"(?i)\bGREATEST\s*\(", "max(", s)
    s = re.sub(r"(?i)\bIFNULL\s*\(", "ifnull(", s)
    s = re.sub(r"(?i)\bNOW\s*\(\s*\)", "CURRENT_TIMESTAMP", s)
    s = re.sub(r"(?i)^drop\s+temporary\s+table", "DROP TABLE", s)
    # MySQL 的 ON DUPLICATE KEY UPDATE → sqlite upsert
    m = re.search(r"(?is)on\s+duplicate\s+key\s+update\s+", s)
    if m:
        head, tail = s[:m.start()], s[m.end():]
        if re.search(r"(?is)\bselect\b", head):
            # INSERT ... SELECT 后面紧跟 ON CONFLICT 会被 sqlite 当成歧义语法；
            # 这类语句的 SELECT 都给了全部 6 列，直接整行替换即等价。
            head = re.sub(r"(?i)^insert\s+into", "INSERT OR REPLACE INTO", head)
            s = head
        else:
            tail = re.sub(r"(?i)values\s*\(\s*`?(\w+)`?\s*\)", r"excluded.\1", tail)
            s = head + "ON CONFLICT DO UPDATE SET " + tail
    return s


def relevant(stmt: str, temps: set[str] | None = None) -> bool:
    temps = temps or set()
    if re.match(r"(?is)\s*create\s+(temporary\s+)?table", stmt):
        return True  # 临时表本身照建，后续语句要用
    if not re.search(r"(?i)\b(drop_data|drop_data_global|temp_data|_removed_\w+|" + "|".join(map(re.escape, temps)) + r")\b", stmt):
        return False
    # 只跑以掉落表（或本次用到的临时表）为目标的语句，别去碰 shopitems 等无关表
    m = re.match(r"(?is)\s*(insert(?:\s+or\s+ignore)?|replace|update(?:\s+or\s+ignore)?|delete(?:\s+target)?)\b.*?"
                 r"(?:into|from|update|join)\s+`?(\w+)`?", stmt)
    if not m:
        return True
    target = m.group(2).lower()
    return target in ("drop_data", "drop_data_global", "temp_data") or target.startswith("_removed_") or target in temps


def build(verbose: bool = False) -> sqlite3.Connection:
    con = sqlite3.connect(":memory:")
    con.row_factory = None
    for ddl in SCHEMA:
        con.execute(ddl)

    files = sorted(glob.glob(os.path.join(MIGRATIONS, "*.sql")))
    failures: list[tuple[str, str, str]] = []
    applied = 0
    temps: set[str] = set()
    for path in files:
        raw = open(path, encoding="utf-8", errors="replace").read()
        if "drop_data" not in raw:
            continue
        for stmt in split_statements(raw):
            if not relevant(stmt, temps):
                continue
            mtemp = re.match(r"(?is)\s*create\s+(?:temporary\s+)?table\s+(?:if\s+not\s+exists\s+)?`?(\w+)`?", stmt)
            if mtemp:
                temps.add(mtemp.group(1).lower())
            sql = mysql_to_sqlite(stmt)
            head = re.match(r"(?i)\s*(\w+\s+\w+|\w+)", sql)
            kind = (head.group(1) if head else "").upper()
            if re.match(r"(?is)^\s*create\s+table", sql):
                # drop_data / drop_data_global 的建表由 SCHEMA 接管，只放行临时表
                if re.search(r"(?i)`?(drop_data|drop_data_global)`?\s*\(", sql):
                    continue
                if not re.search(r"(?i)if\s+not\s+exists", sql):
                    sql = re.sub(r"(?i)^create\s+table\s+", "CREATE TABLE IF NOT EXISTS ", sql)
            # 去掉 sqlite 不认识的列约束（MySQL 专用）
            sql = re.sub(r"(?i)\bunsigned\b", "", sql)
            sql = re.sub(r"(?i)\bauto_increment\b", "", sql)
            if re.match(r"(?is)^\s*create\s+table", sql):
                sql = re.sub(r"(?is)\)\s*ENGINE.*$", ")", sql)
            sql = re.sub(r"(?i)INT\(\d+\)", "INTEGER", sql)
            sql = re.sub(r"(?i)BIGINT\(\d+\)", "INTEGER", sql)
            sql = re.sub(r"(?i)TINYINT\(\d+\)", "INTEGER", sql)
            # 普通二级索引 sqlite 不认（主键/唯一约束保留）
            sql = re.sub(r"(?i),\s*(?:UNIQUE\s+)?(?:KEY|INDEX)\s+`?\w*`?\s*\([^)]*\)(\s*USING\s+\w+)?", "", sql)
            sql = re.sub(r"(?i),\s*(?:KEY|INDEX)\s*\([^)]*\)(\s*USING\s+\w+)?", "", sql)
            sql = re.sub(r"(?i)\s+USING\s+\w+", "", sql)
            # MySQL 允许 INSERT ... (SELECT ...)，sqlite 不允许括号
            if re.match(r"(?is)^\s*insert\s+into.*\)\s*\(\s*select", sql):
                sql = re.sub(r"(?is)(\))\s*\(\s*select", r"\1 SELECT", sql)
                sql = sql.rstrip().rstrip(")")
            try:
                con.execute(sql)
                applied += 1
            except sqlite3.Error as exc:
                failures.append((os.path.basename(path), kind, f"{exc}: {sql[:160]}"))
            if verbose and kind.startswith("DELETE"):
                pass
    if failures:
        print(f"!! {len(failures)} 条语句执行失败：")
        for f, k, msg in failures[:20]:
            print("  ", f, k, msg)
        raise SystemExit(1)
    if verbose:
        print(f"applied {applied} statements")
    return con


# 怪物结晶（etc 4260xxx 命名空间）禁止直接掉落：只可由原石经制作合成获得。
# 导出基线时也剔除，与运行时 DropStore.ExcludeMonsterCrystals 保持同一规则。
def is_monster_crystal(itemid: int) -> bool:
    return 4260000 <= itemid < 4270000


OVERRIDES = os.path.join(ROOT, "content/drop-overrides.tsv")


def apply_overrides(rows: list[tuple], gl: list[tuple]) -> tuple[list[tuple], list[tuple]]:
    """把 content/drop-overrides.tsv 叠到源端重放结果上。

    基线永远是"源端 drop_data 全量重放"，本项目自己加的玩法掉落（机器人零件、
    低档心臟）单独放一份 TSV，方便审计"哪些行不是源端来的"。
    同一 (怪, 道具) 覆盖基线；chance<=0 删行。
    """
    if not os.path.exists(OVERRIDES):
        return rows, gl
    mob = {(r[0], r[1]): r for r in rows}
    glob = {(r[0], r[1]): r for r in gl}
    added = replaced = removed = 0
    for lineno, line in enumerate(open(OVERRIDES, encoding="utf-8-sig"), 1):
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        f = [x.strip() for x in line.split("|")]
        if len(f) != 7:
            raise SystemExit(f"{OVERRIDES}:{lineno} 需要 7 列，实际 {len(f)}：{line}")
        kind = f[0]
        if kind not in ("M", "G"):
            raise SystemExit(f"{OVERRIDES}:{lineno} 未知行类型 {kind}")
        key = (int(f[1]), int(f[2]))
        table = mob if kind == "M" else glob
        had = key in table
        chance = int(f[6])
        if chance <= 0:
            if had:
                del table[key]
                removed += 1
            continue
        table[key] = tuple(int(x) for x in f[1:])
        if had:
            replaced += 1
        else:
            added += 1
    print(f"overrides: +{added} 覆盖 {replaced} 删除 {removed} ({os.path.basename(OVERRIDES)})")
    return sorted(mob.values()), sorted(glob.values())


def export(con: sqlite3.Connection) -> None:
    rows = con.execute(
        "SELECT dropperid, itemid, minimum_quantity, maximum_quantity, questid, chance "
        "FROM drop_data ORDER BY dropperid, itemid").fetchall()
    gl = con.execute(
        "SELECT continent, itemid, minimum_quantity, maximum_quantity, questid, chance "
        "FROM drop_data_global ORDER BY continent, itemid").fetchall()
    # 剔除怪物结晶的直接掉落行。
    rows = [r for r in rows if not is_monster_crystal(r[1])]
    gl = [r for r in gl if not is_monster_crystal(r[1])]
    rows, gl = apply_overrides(rows, gl)
    from ms282.gemstone_drops import replace_legacy
    rows, gl = replace_legacy(rows, gl)
    lines = ["# 源端 drop_data 全量重放 + content/drop-overrides.tsv 补充（tools/export_field_drops.py 生成，勿手改）",
             "# M|dropperid|itemid|min|max|questid|chance",
             "# G|continent|itemid|min|max|questid|chance",
             "# 注：怪物结晶（4260xxx）直接掉落已剔除，只可由原石经制作合成获得。",
             "# 核心宝石改用 TMS282 Mob.initCustomizedDrops + mob_drops.json；概率由 /10000 换算，8643012 保留两次独立抽取。"]
    lines += ["M|%d|%d|%d|%d|%d|%d" % r for r in rows]
    lines += ["G|%d|%d|%d|%d|%d|%d" % r for r in gl]
    with open(TARGET, "w", encoding="utf-8") as fp:
        fp.write("\n".join(lines) + "\n")
    print(f"wrote {TARGET}: {len(rows)} mob drop rows / {len(gl)} global rows")
    assert_drops(rows)


def assert_drops(rows: list[tuple]) -> None:
    """关键回归：二转测试图的黑色大理石（4031013）必须存在，且无 quest 限制；
    另外守住 content/drop-overrides.tsv 里的机器人玩法掉落，别被基线重放盖掉。"""
    chance = {(r[0], r[1]): r[5] for r in rows}
    checks = [
        ("2转测试怪 9000001 → 4031013", (9000001, 4031013)),
        ("2转测试怪 9000002 → 4031013", (9000002, 4031013)),
        ("2转测试怪 9000100 → 4031013", (9000100, 4031013)),
        ("2转测试怪 9000200 → 4031013", (9000200, 4031013)),
        ("2转测试怪 9000300 → 4031013", (9000300, 4031013)),
        ("女机器人 9300133 → 4000357 机器人零件", (9300133, 4000357)),
        ("未來機器人 8120103 → 4000357 机器人零件", (8120103, 4000357)),
        ("女机器人 9300133 → 1672000 銀之心", (9300133, 1672000)),
    ]
    ok = True
    for label, key in checks:
        value = chance.get(key)
        print(("  OK  " if value else "  MISS") + " " + label + (f" chance={value}" if value else ""))
        ok = ok and bool(value)
    if not ok:
        raise SystemExit("掉落表缺少必需行（二转任务道具或机器人玩法掉落）")


def verify() -> None:
    if not os.path.exists(TARGET):
        raise SystemExit(f"缺少产物 {TARGET}")
    mobs = globals_ = 0
    hit = 0
    for line in open(TARGET, encoding="utf-8"):
        if line.startswith("M|"):
            f = line.strip().split("|")
            mobs += 1
            if f[1] == "9000001" and f[2] == "4031013":
                hit = 1
        elif line.startswith("G|"):
            globals_ += 1
    print(f"{TARGET}: {mobs} mob rows / {globals_} global rows / 4031013@9000001={'yes' if hit else 'NO'}")
    if not hit:
        raise SystemExit(1)


if __name__ == "__main__":
    if "--verify" in sys.argv:
        verify()
    else:
        export(build(verbose="--verbose" in sys.argv))
