#!/usr/bin/env python3
"""扫描 gms-server 脚本，统计脚本用到但 NpcApi/Scripting 未绑定的 API。"""
import re, sys, os, json
from collections import Counter, defaultdict

SRC = "/Users/lizixian/Documents/mxd/BeiDou-Server/gms-server/scripts-zh-CN"
BOUND = set(open('/tmp/bound_all.txt').read().split())
OUT_JSON = "/Users/lizixian/Documents/mxd/mxd-android/MapleDash/tools/api_scan_report.json"

# JS 语言内建 / 解释器自带的成员，不算服务端 API
BUILTIN = set("""
push pop shift unshift splice join concat slice indexOf lastIndexOf includes find findIndex
forEach map filter some every reduce reduceRight sort reverse fill flat flatMap keys values entries
split substring substr replace replaceAll charAt charCodeAt at toLowerCase toUpperCase trim trimStart trimEnd
padStart padEnd repeat startsWith endsWith localeCompare match matchAll test exec compile
toString toFixed toPrecision toExponential valueOf hasOwnProperty isPrototypeOf propertyIsEnumerable
call apply bind constructor name length prototype __proto__
floor ceil round abs trunc sign pow sqrt cbrt log log2 log10 exp min max random hypot
E LN2 LN10 LOG2E LOG10E PI SQRT1_2 SQRT2
parse stringify stringify
UTC now parse
getFullYear getMonth getDate getDay getHours getMinutes getSeconds getMilliseconds
setFullYear setMonth setDate setHours setMinutes setSeconds getTime getTimezoneOffset
source flags global ignoreCase multiline lastIndex
type
next hasNext close iterator
delete clear get set has size add
message stack
""".split())

call_re = re.compile(r'\.([A-Za-z_][A-Za-z0-9_]*)\s*\(')
owner_re = re.compile(r'\b(cm|qm|pi|im|player)\.([A-Za-z_][A-Za-z0-9_]*)\s*\(')

def strip_code(text):
    """去掉 // /* 注释和字符串/模板串内容，避免误报。"""
    out, i, n = [], 0, len(text)
    while i < n:
        c = text[i]
        if c == '/' and i+1 < n and text[i+1] == '/':
            j = text.find('\n', i)
            i = n if j < 0 else j
        elif c == '/' and i+1 < n and text[i+1] == '*':
            j = text.find('*/', i+2)
            i = n if j < 0 else j+2
        elif c in ('"', "'"):
            q, i = c, i+1
            while i < n:
                if text[i] == '\\': i += 2; continue
                if text[i] == q: i += 1; break
                i += 1
        elif c == '`':
            i += 1
            while i < n:
                if text[i] == '\\': i += 2; continue
                if text[i] == '`': i += 1; break
                i += 1
        else:
            out.append(c); i += 1
    return ''.join(out)

missing = Counter()          # name -> 次数
files_by_name = defaultdict(set)
owners = defaultdict(Counter)  # name -> {cm: n, qm: n, chained: n}
total_files = 0

for sub in ('npc', 'quest', 'portal', 'BeiDouSpecial'):
    d = os.path.join(SRC, sub)
    if not os.path.isdir(d): continue
    for fn in sorted(os.listdir(d)):
        if not fn.endswith('.js'): continue
        total_files += 1
        text = strip_code(open(os.path.join(d, fn), encoding='utf-8', errors='replace').read())
        seen = set()
        for m in call_re.finditer(text):
            name = m.group(1)
            if name in BUILTIN or name in BOUND: continue
            seen.add(name)
        for name in seen:
            missing[name] += 1
            files_by_name[name].add(sub + '/' + fn)
        # 记录直接宿主前缀
        for m in owner_re.finditer(text):
            name = m.group(2)
            if name not in BUILTIN and name not in BOUND:
                owners[name][m.group(1)] += 1

report = []
for name, cnt in missing.most_common():
    report.append({
        'api': name,
        'files': cnt,
        'sample': sorted(files_by_name[name])[:5],
        'owners': dict(owners[name]),
    })
json.dump(report, open(OUT_JSON, 'w'), ensure_ascii=False, indent=1)
print(f"files scanned: {total_files}, missing APIs: {len(report)}")
for r in report:
    o = ','.join(f"{k}:{v}" for k, v in r['owners'].items()) or 'chained'
    print(f"{r['api']:<32} {r['files']:>4} files  [{o}]  e.g. {r['sample'][0] if r['sample'] else ''}")
