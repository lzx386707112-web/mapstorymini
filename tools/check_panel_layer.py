#!/usr/bin/env python3
"""Run the production panel z-order (click-to-front) outside Unity.

绘制层级 = zOrder 的下标（后画在上）。这里只执行纯排序逻辑：
- zOrder 必须与 DrawPanel 的 case 一一对应，否则该面板会被静默漏画 / 画出不存在的层
- BringToFront 必须把目标搬到末尾，且附属窗（机器人换装 / 公会阶级）始终紧跟宿主
"""
from pathlib import Path
import re
import subprocess
import tempfile

root = Path(__file__).resolve().parents[1]
source = (root / "Assets/Scripts/GameWindows.cs").read_text()

enum_start = source.index("    enum Panel")
enum_body = source[enum_start:source.index("    }", enum_start) + 6]
z_start = source.index("    static readonly List<Panel> zOrder")
z_end = source.index("    // 窗口矩形来自各窗口自己的 RegisterWindow 登记")
z_body = source[z_start:z_end]
draw_body = source[source.index("    static void DrawPanel(Panel p)"):]

z_order = re.findall(r"Panel\.(\w+)", z_body.split("};")[0])
draw_cases = re.findall(r"case Panel\.(\w+):", draw_body)

assert len(z_order) == len(set(z_order)), "zOrder 有重复项"
missing_draw = [p for p in z_order if p not in draw_cases]
if missing_draw:
    raise SystemExit("zOrder 里的面板没有 DrawPanel 分支（会被漏画）: " + ", ".join(missing_draw))
orphan = [p for p in draw_cases if p not in z_order]
if orphan:
    raise SystemExit("DrawPanel 有分支但不在 zOrder 里（永远画不到）: " + ", ".join(orphan))

program = r"""
using System;
using System.Collections.Generic;
static class GameWindows {
""" + enum_body.replace("    enum Panel", "    public enum Panel") + "\n" \
    + z_body.replace("static void BringToFront", "public static void BringToFront") + r"""
    public static int Count => zOrder.Count;
    public static int At(Panel p) => zOrder.IndexOf(p);
    public static Panel Last => zOrder[zOrder.Count - 1];
    public static Panel[] Snapshot() => zOrder.ToArray();
}
class Program {
    static int n;
    static void Check(bool ok, string label) { if (!ok) throw new Exception("layer: " + label); n++; }
    static void Main() {
        string[] all = Enum.GetNames(typeof(GameWindows.Panel));
        var before = GameWindows.Snapshot();
        Check(before.Length > 40, "zOrder seeded with every drawable layer");

        // 每个面板都能被点击置顶，且不丢失、不重复
        foreach (var name in all) {
            var p = (GameWindows.Panel)Enum.Parse(typeof(GameWindows.Panel), name);
            if (Array.IndexOf(before, p) < 0) continue;   // PetExclusion 只服务 Esc，没有绘制层
            GameWindows.BringToFront(p);
            Check(GameWindows.Count == before.Length, name + " keeps the layer count");
            Check(new List<GameWindows.Panel>(GameWindows.Snapshot()).Count == before.Length, name + " no duplicate layer");
        }

        // 普通窗口：点它就排到最后
        GameWindows.BringToFront(GameWindows.Panel.Stat);
        Check(GameWindows.Last == GameWindows.Panel.Stat, "Stat raised to the top");
        GameWindows.BringToFront(GameWindows.Panel.KeyConfig);
        Check(GameWindows.Last == GameWindows.Panel.KeyConfig, "KeyConfig raised above the skill window");

        // 附属窗：跟着宿主一起搬，顺序不能拆
        GameWindows.BringToFront(GameWindows.Panel.Equip);
        Check(GameWindows.At(GameWindows.Panel.Android) == GameWindows.At(GameWindows.Panel.Equip) + 1, "Android stays right after Equip");
        GameWindows.BringToFront(GameWindows.Panel.Android);
        Check(GameWindows.At(GameWindows.Panel.Android) == GameWindows.At(GameWindows.Panel.Equip) + 1, "clicking Android raises its host too");
        GameWindows.BringToFront(GameWindows.Panel.Guild);
        Check(GameWindows.At(GameWindows.Panel.GuildGrade) == GameWindows.At(GameWindows.Panel.Guild) + 1, "GuildGrade stays right after Guild");

        // 幂等：已经在上面的再点一次不打乱顺序
        GameWindows.BringToFront(GameWindows.Panel.Equip);
        var once = GameWindows.Snapshot();
        GameWindows.BringToFront(GameWindows.Panel.Equip);
        Check(string.Join(",", once) == string.Join(",", GameWindows.Snapshot()), "repeated raise is idempotent");

        Console.WriteLine($"Panel z-order: {n} checks PASS");
    }
}
"""

with tempfile.TemporaryDirectory(prefix="panel-layer-") as tmp:
    task = Path(tmp)
    (task / "Check.csproj").write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
        '<TargetFramework>net8.0</TargetFramework><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>')
    (task / "Program.cs").write_text(program)
    subprocess.run(["dotnet", "run", "--project", str(task / "Check.csproj"), "--verbosity", "quiet"],
                   check=True)
