#!/usr/bin/env python3
"""Run production Esc stack with instrumented window endpoints outside Unity."""
from pathlib import Path
import re
import subprocess
import tempfile
root = Path(__file__).resolve().parents[1]
s = (root / "Assets/Scripts/GameWindows.cs").read_text()
body = s[s.index("    enum Panel"):s.index("    public static void DrawAll()")]
visible = re.findall(r"case Panel\.(\w+): return (\w+)\.(\w+);", body)
classes = {}
for cls, member in re.findall(r"\b((?:Native|\w+Window)\w*)\.(\w+)", body):
    classes.setdefault(cls, set()).add(member)
code = "using System; using System.Collections.Generic;\nstatic class GameWindows {" + body + "}\n"
for cls, members in classes.items():
    props = {prop for _, c, prop in visible if c == cls}
    # HasModal / AtPointer 这类也是 bool 属性，不能当成 void 方法生成
    props.update(x for x in members if x in ("Visible", "Busy", "HasModal"))
    code += "static class " + cls + " {" + "".join("public static bool " + prop + ";" for prop in props)
    matrix = cls in ("NativeVMatrixWindow", "NativeHexaMatrixWindow")
    if matrix:
        # Escape() 现在会按「手上拿着的核心 → 弹窗 → 子窗」逐层收，替身类要补齐同名字段与空方法。
        code += ("public enum Dialog { None, Popup }; public static Dialog dialog;"
                 " public static bool carrying, miniOpen, specialOpen, specialBarDrag, specialOwnsGesture;"
                 " public static int specialScroll, specialScrollStart; public static float specialBarGrab;"
                 " public static void CancelCarry() {}"
                 " public static void Close() {Visible=false; miniOpen=false; specialOpen=false; specialBarDrag=false;}")
    for method in members - props:
        if matrix and method == "Escape":
            source = (root / ("Assets/Scripts/" + cls + ".cs")).read_text()
            escape = source.split("    public static void Escape()", 1)[1].split("    public static void Close()", 1)[0]
            code += "public static void Escape()" + escape
            continue
        extra = "ShopWindow.Visible=false;" if cls == "NativeCashWindow" and method == "Reset" else ""
        code += "public static void " + method + "() {" + extra + "".join(prop + "=false;" for prop in props if prop != "Busy") + "}"
    code += "}\n"
code += "class Program { static int n; static void Check(bool ok) {if(!ok) throw new Exception(\"Esc check \"+n); n++;} static void Main() {"
for panel, cls, prop in visible:
    code += f"{cls}.{prop}=true; GameWindows.TrackPanels(); Check(GameWindows.CloseTop()); Check(!{cls}.{prop}); Check(!GameWindows.CloseTop());"
code += "StatWindow.Visible=true; GameWindows.TrackPanels(); NativeHyperStatWindow.Visible=true; GameWindows.TrackPanels(); Check(GameWindows.CloseTop() && !NativeHyperStatWindow.Visible && StatWindow.Visible); Check(GameWindows.CloseTop() && !StatWindow.Visible);"
code += "NativeVMatrixWindow.Visible=true; GameWindows.TrackPanels(); NativeUtilPrompt.Visible=true; Check(GameWindows.CloseTop() && !NativeUtilPrompt.Visible && NativeVMatrixWindow.Visible); Check(GameWindows.CloseTop() && !NativeVMatrixWindow.Visible);"
code += "NativeVegaWindow.Visible=true; NativeVegaWindow.Busy=true; GameWindows.TrackPanels(); Check(GameWindows.CloseTop() && NativeVegaWindow.Visible); NativeVegaWindow.Busy=false; Check(GameWindows.CloseTop() && !NativeVegaWindow.Visible); Check(!GameWindows.CloseTop());"
for cls in ("NativeVMatrixWindow", "NativeHexaMatrixWindow"):
    code += f"{cls}.Visible=true; {cls}.dialog={cls}.Dialog.Popup; GameWindows.TrackPanels(); Check(GameWindows.CloseTop() && {cls}.Visible && {cls}.dialog=={cls}.Dialog.None); Check(GameWindows.CloseTop() && !{cls}.Visible);"
# 子窗（V 的特殊核心 / HEXA 的索尔艾尔达迷你窗）也是主窗里的一层：
# 一次 Esc 只收子窗，主窗必须还在；再一次才收主窗。
for cls, field in (("NativeVMatrixWindow", "specialOpen"), ("NativeHexaMatrixWindow", "miniOpen")):
    code += (f"{cls}.Visible=true; {cls}.{field}=true; GameWindows.TrackPanels();"
             f" Check(GameWindows.CloseTop() && {cls}.Visible && !{cls}.{field});"
             f" Check(GameWindows.CloseTop() && !{cls}.Visible);")
code += 'Console.WriteLine($"Esc stack: {n} checks PASS"); }}'
with tempfile.TemporaryDirectory(prefix="escape-panels-") as tmp:
    tmp = Path(tmp)
    (tmp / "Check.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>')
    (tmp / "Program.cs").write_text(code)
    subprocess.run(["dotnet", "run", "--project", str(tmp / "Check.csproj")], check=True)
