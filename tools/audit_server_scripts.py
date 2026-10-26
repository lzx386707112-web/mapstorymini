#!/usr/bin/env python3
"""全量脚本兼容诊断：标准 JS 语法、内嵌引擎解析、静态 Java/API 依赖。

静态依赖清单不等于执行覆盖率；冒烟使用模拟宿主，不等于玩法完成。
"""
import json
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
PACK = ROOT / "Assets/Resources/Scripts/npc_scripts.json"
REPORT = ROOT / "docs/script-compatibility.json"


def run(command, **kwargs):
    return subprocess.run(["rtk", "proxy", *command], cwd=ROOT, text=True, capture_output=True, **kwargs)


def main():
    scripts = json.loads(PACK.read_text())
    standard = run(["node", "-"], input="""
const fs=require('fs'),vm=require('vm');
const scripts=JSON.parse(fs.readFileSync('Assets/Resources/Scripts/npc_scripts.json','utf8'));
let bad=[];
for(const [name,source] of Object.entries(scripts)) {
 try { new vm.Script(source,{filename:name}); }
 catch(error) { bad.push({name,error:error.message}); }
}
process.stdout.write(JSON.stringify(bad));
""")
    if standard.returncode:
        raise SystemExit(standard.stderr)
    invalid = json.loads(standard.stdout)
    packed = run(["dotnet", "run", "-c", "Release", "--project", "tools/script-harness", "--", "--pack", str(PACK)])
    if packed.returncode not in (0, 1) or "pack=" not in packed.stdout:
        raise SystemExit(packed.stdout + packed.stderr)
    parse_errors = []
    for line in packed.stdout.splitlines():
        match = re.match(r"  PARSE (.*?) :: (.*)", line)
        if match:
            parse_errors.append({"name": match[1], "error": match[2]})
    source_bad = {item["name"] for item in invalid}
    engine_bad = {item["name"] for item in parse_errors}
    dependencies, api_calls = {}, {}
    for name, source in scripts.items():
        for clazz in set(re.findall(r"Java\.type\(\s*['\"]([^'\"]+)", source)):
            dependencies.setdefault(clazz, []).append(name)
        for owner, method in set(re.findall(r"\b(cm|qm|pi)\.([\w$]+)\s*\(", source)):
            api_calls.setdefault(method, []).append(name)
    api_source = (ROOT / "Assets/Scripts/Npc/NpcApi.cs").read_text()
    bound = set(re.findall(r'(?:cm|host)\.Set\("([^"\n]+)"', api_source))
    report = {
        "scriptCount": len(scripts), "parseable": len(scripts)-len(parse_errors),
        "standardJavaScriptInvalid": invalid, "embeddedParserInvalid": parse_errors,
        "validJavaScriptRejectedByEmbeddedParser": sorted(engine_bad-source_bad),
        "sourceInvalidAcceptedByEmbeddedParser": sorted(source_bad-engine_bad),
        "javaTypeDependencies": dict(sorted(dependencies.items())),
        "directConversationMethodsNotExplicitlyBound": {key: value for key, value in sorted(api_calls.items()) if key not in bound},
        "limitations": [
            "Java type strings and method names are static dependency candidates, including comments; no dynamic target inference.",
            "An explicit binding can still be a stub or partial implementation. This report does not claim gameplay completeness.",
            "Mock-based harness execution does not validate rewards, inventory, quests, event scheduling, persistence or all branches.",
            "Syntax repairs authorized by the user are recorded in docs/source-script-exceptions.md; source synchronization remains exact."
        ]
    }
    REPORT.write_text(json.dumps(report, ensure_ascii=False, indent=2)+"\n")
    print("scripts="+str(len(scripts))+" parseable="+str(report["parseable"])+" sourceInvalid="+str(len(invalid))+" validSourceRejected="+str(len(engine_bad-source_bad)))
    print("Java classes="+str(len(dependencies))+" conversation methods without explicit bindings="+str(len(report["directConversationMethodsNotExplicitlyBound"])))
    print(REPORT)
    return 1 if engine_bad-source_bad or source_bad-engine_bad else 0


if __name__ == "__main__":
    sys.exit(main())
