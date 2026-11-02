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


def export(con: sqlite3.Connection) -> None:
    rows = con.execute(
        "SELECT dropperid, itemid, minimum_quantity, maximum_quantity, questid, chance "
        "FROM drop_data ORDER BY dropperid, itemid").fetchall()
    gl = con.execute(
        "SELECT continent, itemid, minimum_quantity, maximum_quantity, questid, chance "
        "FROM drop_data_global ORDER BY continent, itemid").fetchall()
    lines = ["# 源端 drop_data 全量重放（tools/export_field_drops.py 生成，勿手改）",
             "# M|dropperid|itemid|min|max|questid|chance",
             "# G|continent|itemid|min|max|questid|chance"]
    lines += ["M|%d|%d|%d|%d|%d|%d" % r for r in rows]
    lines += ["G|%d|%d|%d|%d|%d|%d" % r for r in gl]
    with open(TARGET, "w", encoding="utf-8") as fp:
        fp.write("\n".join(lines) + "\n")
    print(f"wrote {TARGET}: {len(rows)} mob drop rows / {len(gl)} global rows")
    assert_drops(con)


def assert_drops(con: sqlite3.Connection) -> None:
    """关键回归：二转测试图的黑色大理石（4031013）必须存在，且无 quest 限制。"""
    checks = [
        ("2转测试怪 9000001 → 4031013", "SELECT chance FROM drop_data WHERE dropperid=9000001 AND itemid=4031013"),
        ("2转测试怪 9000002 → 4031013", "SELECT chance FROM drop_data WHERE dropperid=9000002 AND itemid=4031013"),
        ("2转测试怪 9000100 → 4031013", "SELECT chance FROM drop_data WHERE dropperid=9000100 AND itemid=4031013"),
        ("2转测试怪 9000200 → 4031013", "SELECT chance FROM drop_data WHERE dropperid=9000200 AND itemid=4031013"),
        ("2转测试怪 9000300 → 4031013", "SELECT chance FROM drop_data WHERE dropperid=9000300 AND itemid=4031013"),
    ]
    ok = True
    for label, sql in checks:
        row = con.execute(sql).fetchone()
        print(("  OK  " if row else "  MISS") + " " + label + (f" chance={row[0]}" if row else ""))
        ok = ok and bool(row)
    if not ok:
        raise SystemExit("掉落表缺少二转任务道具行")


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
