#!/usr/bin/env python3
"""Execute the production pointer mask with minimal geometry, without Unity native input."""
from pathlib import Path
import subprocess
import tempfile
import re

root = Path(__file__).resolve().parents[1]
source = (root / "Assets/Scripts/InputHub.cs").read_text()
mask = source.split("    public struct InputMask", 1)[1].split("    static float lastClickTime", 1)[0]
program = r"""
using System;
struct Vector2 {
    public float x,y;
    public Vector2(float a,float b) { x=a; y=b; }
}
struct Rect {
    public bool Contains(Vector2 p) => p.x>=10 && p.x<110 && p.y>=10 && p.y<110;
}
static class Pointer {
    public static Vector2 Pos, PressPosition;
    public static bool Down,Pressed,Released,Clicked,DoubleClicked,RightDown,RightPressed,RightReleased,Dragging,DragStarted,DragEnded;
    public struct InputMask
""" + mask + r"""
}
class Program {
    static int count;
    static void Check(bool ok,string label) { if(!ok) throw new Exception(label); count++; }
    static void Seed() {
        Pointer.Pos=new Vector2(30,40); Pointer.PressPosition=new Vector2(20,25);
        Pointer.Down=Pointer.Pressed=Pointer.Released=Pointer.Clicked=Pointer.DoubleClicked=true;
        Pointer.RightDown=Pointer.RightPressed=Pointer.RightReleased=true;
        Pointer.Dragging=Pointer.DragStarted=Pointer.DragEnded=true;
    }
    static bool All() => Pointer.Down && Pointer.Pressed && Pointer.Released && Pointer.Clicked && Pointer.DoubleClicked && Pointer.RightDown && Pointer.RightPressed && Pointer.RightReleased && Pointer.Dragging && Pointer.DragStarted && Pointer.DragEnded;
    static bool Any() => Pointer.Down || Pointer.Pressed || Pointer.Released || Pointer.Clicked || Pointer.DoubleClicked || Pointer.RightDown || Pointer.RightPressed || Pointer.RightReleased || Pointer.Dragging || Pointer.DragStarted || Pointer.DragEnded;
    static void Main() {
        Seed();
        var mask=new Pointer.InputMask(true);
        Check(!Any(),"underlay receives no button or drag events");
        Check(!Pointer.HitsWindow(new Rect()),"both hover and press origin hidden");
        mask.Dispose();
        Check(All(),"owner receives original complete gesture");
        Check(Pointer.Pos.x==30 && Pointer.Pos.y==40 && Pointer.PressPosition.x==20 && Pointer.PressPosition.y==25,"original geometry restored");
        Pointer.Clicked=false; mask.Dispose();
        Check(!Pointer.Clicked,"second restore cannot resurrect owner-consumed click");
        Seed(); var inactive=new Pointer.InputMask(false);
        Check(All(),"uncovered windows retain input");
        Pointer.Clicked=false; inactive.Dispose();
        Check(!Pointer.Clicked,"inactive mask cannot resurrect consumed click");
        Seed(); Pointer.Pos=new Vector2(200,200);
        Check(Pointer.HitsWindow(new Rect()),"drag/release remains with press-origin window");
        Pointer.Down=Pointer.Released=Pointer.DragEnded=false;
        Check(!Pointer.HitsWindow(new Rect()),"old press origin does not block idle hover elsewhere");
        Seed();
        try { using(var failing=new Pointer.InputMask(true)) { throw new Exception("draw failed"); } } catch {}
        Check(All(),"exception restores owner input");
        Console.WriteLine($"Pointer masking: {count} behavioral checks PASS");
    }
}
"""
matrix = (root / "Assets/Scripts/NativeVMatrixWindow.cs").read_text()
gesture = matrix[matrix.index("        if (Pointer.Pressed) specialOwnsGesture"):
                 matrix.index("        // 抓手 =")]
program = program.replace("        Seed();\n        var mask", "        SpecialGesture.Run();\n        Seed();\n        var mask", 1)
program += """
static class SpecialGesture {
    static bool specialOwnsGesture, specialOpen = true;
    static Rect SpecialRect() => new Rect();
    static bool Step() {
""" + gesture + r"""
        return specialLayer;
    }
    static void Check(bool ok, string label) { if (!ok) throw new Exception(label); }
    public static void Run() {
        Pointer.Pos = new Vector2(30,40);
        Pointer.Pressed = Pointer.Down = true; Pointer.Released = false;
        Check(Step(), "special press belongs to overlay");
        Pointer.Pressed = Pointer.Down = false; Pointer.Released = Pointer.Clicked = true;
        Pointer.Pos = new Vector2(200,200);
        Check(Step(), "special release outside must not reach underlying core");
        Pointer.Released = Pointer.Clicked = false;
        Check(!Step(), "idle clears special gesture ownership");
        Pointer.Pos = new Vector2(30,40);
        Check(Step(), "special hover hides underlying core tooltip");
        Pointer.Pos = new Vector2(200,200); Pointer.Pressed = Pointer.Down = true;
        Check(!Step(), "main panel press remains interactive");
        Pointer.Pressed = false; Pointer.Pos = new Vector2(30,40);
        Check(Step(), "dragging into overlay cannot activate covered main core");
        specialOpen = false;
        Check(!Step(), "closed special overlay cannot mask main panel");
        Console.WriteLine("Special core overlay: 7 gesture checks PASS");
    }
}
"""
# Execute the real DrawAll routing with instrumented draw endpoints. In particular,
# the matrix endpoint consumes even disabled clicks, reproducing the reported regression.
windows = (root / "Assets/Scripts/GameWindows.cs").read_text()


def braces(text):
    depth = 0
    for end, char in enumerate(text):
        if char == "{": depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0: return text[:end + 1]
    raise AssertionError("unbalanced braces")


body = braces(windows.split("public static void DrawAll()", 1)[1])
# 绘制链按 zOrder 排，真正的窗口调用在 DrawPanel 的 switch 里，一起展开才能插桩。
draw_body = braces(windows.split("static void DrawPanel(Panel p)", 1)[1])
panel_enum = "enum Panel { " + ", ".join(dict.fromkeys(re.findall(r"case Panel\.(\w+):", draw_body))) + " }"
z_start = windows.index("static readonly List<Panel> zOrder")
z_order = windows[z_start:windows.index("};", z_start) + 2].replace("List<Panel>", "System.Collections.Generic.List<Panel>")
classes = {}
for name, method in re.findall(r"\b([A-Z]\w*)\.(Draw\w*|Tick|Reset|Flush|Emit)\(\)", body + draw_body):
    if name != "NativeUtilPrompt": classes.setdefault(name, set()).add(method)
program += "static class GUI { public static bool enabled = true; }\n"
program += "static class GameWindows { static void TrackPanels() {} static bool raisedPending; static Panel raisedPanel;"
program += " static void BringToFront(Panel p) {} static Panel? TopPanelAtPointer() => null;\n"
program += panel_enum + "\n" + z_order + "\n"
program += "static void DrawPanel(Panel p) " + draw_body + "\n"
program += "public static void DrawAll() " + body + "}\n"
for name, methods in classes.items():
    program += f"static class {name} {{ public static bool AtPointer => Probe.Matrix==\"{name}\" && Pointer.HitsWindow(new Rect());"
    for method in methods:
        call = f'Probe.Draw("{name}");' if method.startswith("Draw") else ""
        program += f"public static void {method}() {{ {call} }}"
    program += "}\n"
program += r"""
static class Probe {
    public static string Matrix;
    public static int UnderlayClicks;
    public static void Draw(string name) {
        if (!Pointer.Clicked) return;
        UnderlayClicks++;
        // This matches the matrix's end-of-draw blank-area consumption, independent of GUI.enabled.
        if (name==Matrix) Pointer.Clicked=Pointer.DoubleClicked=Pointer.Released=false;
    }
}
static class NativeUtilPrompt {
    public static bool Visible;
    public static int Target, Answers;
    public static Action<bool> Callback;
    public static void Draw() {
        if (!Visible) return;
        if (Pointer.Clicked && Target<2) { Visible=false; Answers++; Callback(Target==0); }
        Pointer.Clicked=Pointer.DoubleClicked=Pointer.Released=Pointer.Pressed=false;
    }
}
static class LayerChecks {
    public static void Run() {
        int count=0;
        foreach(string owner in new[]{"NativeVMatrixWindow","NativeHexaMatrixWindow"}) {
            for(int target=0;target<3;target++) {
                Probe.Matrix=owner; Probe.UnderlayClicks=0;
                NativeUtilPrompt.Visible=true; NativeUtilPrompt.Target=target; NativeUtilPrompt.Answers=0;
                int callback=0;
                NativeUtilPrompt.Callback=ok=>{callback+=ok?1:10;};
                Pointer.Pos=Pointer.PressPosition=new Vector2(30,40);
                Pointer.Clicked=Pointer.Released=true;
                GUI.enabled=true;
                GameWindows.DrawAll();
                if(Probe.UnderlayClicks!=0 || Pointer.Clicked || !GUI.enabled) throw new Exception(owner+": prompt click reached a lower layer");
                int expected=target==0?1:target==1?10:0;
                if(callback!=expected || NativeUtilPrompt.Visible!=(target==2)) throw new Exception(owner+": confirm/cancel/background routing failed");
                GameWindows.DrawAll();
                if(callback!=expected) throw new Exception("callback repeated without a second click");
                count++;
            }
        }
        Console.WriteLine($"Production DrawAll modal routing: {count} scenarios PASS");
    }
}
"""
program = program.replace('Console.WriteLine($"Pointer masking: {count} behavioral checks PASS");', 'Console.WriteLine($"Pointer masking: {count} behavioral checks PASS"); LayerChecks.Run();')

with tempfile.TemporaryDirectory(prefix="matrix-pointer-") as directory:
    task = Path(directory)
    (task / "Check.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>')
    command = ["rtk", "proxy", "dotnet", "run", "--project", str(task / "Check.csproj"), "--verbosity", "quiet", "-p:NuGetAudit=false", "-p:RestoreSources="]
    old_program = program.replace("var promptPointer = new Pointer.InputMask(NativeUtilPrompt.Visible);", "").replace("promptPointer.Dispose();", "")
    (task / "Program.cs").write_text(old_program)
    failed = subprocess.run(command, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    assert failed.returncode != 0 and "prompt click reached a lower layer" in failed.stdout, failed.stdout
    print("Before fix: reproduced matrix consuming the prompt click", flush=True)
    (task / "Program.cs").write_text(program)
    subprocess.run(command, check=True)
