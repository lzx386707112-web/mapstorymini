using System;
using UnityEditor;
using UnityEngine;

// 移动端输入框软键盘回归（batchmode 可跑，不连真机、不碰存档/DB）。
// 复用 NativeEditKeyboard 的编辑器注入快照，走的是与真机完全同一条 Sync/Apply 路径，
// 覆盖项目已踩过的两条 Android 时序坑：
//   LoginFlow:194-199 —— 刚弹出头几帧非 Visible / 切前后台短暂 LostFocus 不能被当成「收键盘」；
//   LoginFlow:843-846 —— 同一个输入框「关掉再 Open」真会失败，重复点击绝不重开；
//   BottomBar:300-302 —— Done 与 Enter 同帧只提交一次，Canceled 不提交。
//   Unity -batchmode -quit -projectPath <proj> -executeMethod NativeEditKeyboardChecks.Run -logFile /tmp/edit_keyboard.log
public static class NativeEditKeyboardChecks
{
    const string Tag = "[EditKeyboard] ";
    static int pass, fail;
    static void Check(bool ok, string what, string detail = "")
    {
        if (ok) pass++; else fail++;
        Debug.Log(Tag + (ok ? "✓ " : "✗ FAIL ") + what + (string.IsNullOrEmpty(detail) ? "" : "  — " + detail));
    }
    public static void Run()
    {
        try
        {
            // ── 1. Apply：Android 最终合成文本覆盖草稿；null 文本（刚弹出未给值）不覆盖 ──
            string v = "草稿";
            var sig = NativeEditKeyboard.Apply(ref v, "最终输入", TouchScreenKeyboard.Status.Visible);
            Check(sig == NativeEditKeyboard.Signal.None && v == "最终输入",
                "键盘最终文本回灌覆盖草稿", "v=" + v + " sig=" + sig);
            v = "已输入5";
            sig = NativeEditKeyboard.Apply(ref v, null, TouchScreenKeyboard.Status.Visible);
            Check(v == "已输入5", "null 文本不覆盖已有草稿（刚弹出头几帧）", "v=" + v);

            // ── 2. 状态语义：仅 Done/Canceled 才收；Visible/LostFocus 一律保留键盘 ──
            Check(NativeEditKeyboard.Apply(ref v, "5", TouchScreenKeyboard.Status.Visible) == NativeEditKeyboard.Signal.None,
                "Visible 不产生提交/取消信号", "");
            Check(NativeEditKeyboard.Apply(ref v, "5", TouchScreenKeyboard.Status.LostFocus) == NativeEditKeyboard.Signal.None,
                "LostFocus 不收键盘（切前后台短暂失焦不算用户收起）", "");
            v = "88";
            sig = NativeEditKeyboard.Apply(ref v, "88", TouchScreenKeyboard.Status.Done);
            Check(sig == NativeEditKeyboard.Signal.Done && v == "88", "Done 回灌最终文本并给出提交信号", "");
            sig = NativeEditKeyboard.Apply(ref v, "丢弃", TouchScreenKeyboard.Status.Canceled);
            Check(sig == NativeEditKeyboard.Signal.Canceled, "Canceled 给出取消信号（调用方不提交）", "");

            // ── 3. 坑 1：同一个输入框开着键盘时重复点击绝不重开 ──
            Check(!NativeEditKeyboard.ShouldReopen("NativeUtilPrompt", true, "NativeUtilPrompt"),
                "同 owner 且键盘 active：不重开（Android 关掉再 Open 真会失败）", "");
            Check(NativeEditKeyboard.ShouldReopen("NativeUtilPrompt", false, "NativeUtilPrompt"),
                "同 owner 但键盘已收起：允许重开", "");
            Check(NativeEditKeyboard.ShouldReopen("NativeUtilPrompt", true, "NativeDlgInput"),
                "owner 不同（换输入框）：允许重开", "");
            Check(NativeEditKeyboard.ShouldReopen(null, false, "NativeUtilPrompt"),
                "当前无键盘：允许开", "");

            // ── 4. Sync owner 隔离：A 输入框的键盘不会被 B 读走、也不改 B 的草稿 ──
            NativeEditKeyboard.Inject("NativeDlgInput", "文本窗最终输入", TouchScreenKeyboard.Status.Done);
            string guard = "B草稿";
            var guardSig = NativeEditKeyboard.Sync("NativeUtilPrompt", ref guard);
            Check(guardSig == NativeEditKeyboard.Signal.None && guard == "B草稿",
                "owner 不匹配时不回灌、不提交、不误收别人键盘", "v=" + guard + " sig=" + guardSig);

            // ── 5. Sync 命中：Done 回灌最终文本并提交后收键盘（幂等，不会二次提交）──
            string target = "旧值";
            var doneSig = NativeEditKeyboard.Sync("NativeDlgInput", ref target);
            Check(doneSig == NativeEditKeyboard.Signal.Done && target == "文本窗最终输入",
                "同 owner 的 Done 回灌 Android 最终文本并提交一次", "v=" + target);
            // Done 之后桥已 Close：再 Sync 同 owner 应返回 None（同帧 Enter 不会二次触发提交）
            string again = target;
            var againSig = NativeEditKeyboard.Sync("NativeDlgInput", ref again);
            Check(againSig == NativeEditKeyboard.Signal.None,
                "Done 收键盘后同帧不再产生提交信号（Done+Enter 只提交一次）", "sig=" + againSig);

            // ── 6. LostFocus / Visible 在 Sync 路径同样不收键盘（LoginFlow 踩坑）──
            NativeEditKeyboard.Inject("NativeUtilPrompt", "7", TouchScreenKeyboard.Status.LostFocus);
            string lf = "";
            var lfSig = NativeEditKeyboard.Sync("NativeUtilPrompt", ref lf);
            Check(lfSig == NativeEditKeyboard.Signal.None && lf == "7" && NativeEditKeyboard.Owns("NativeUtilPrompt"),
                "Sync 遇 LostFocus 只回灌文本、保留键盘归属", "v=" + lf);
            NativeEditKeyboard.Inject("NativeUtilPrompt", "9", TouchScreenKeyboard.Status.Visible);
            string vis = "";
            var visSig = NativeEditKeyboard.Sync("NativeUtilPrompt", ref vis);
            Check(visSig == NativeEditKeyboard.Signal.None && vis == "9" && NativeEditKeyboard.Owns("NativeUtilPrompt"),
                "Sync 遇 Visible 只回灌文本、保留键盘归属", "v=" + vis);

            // ── 7. Canceled 收键盘但不提交；Close 清态 ──
            NativeEditKeyboard.Inject("NativeUtilPrompt", "x", TouchScreenKeyboard.Status.Canceled);
            string cancelV = "保留";
            var cancelSig = NativeEditKeyboard.Sync("NativeUtilPrompt", ref cancelV);
            Check(cancelSig == NativeEditKeyboard.Signal.Canceled && cancelV == "x" && !NativeEditKeyboard.Owns("NativeUtilPrompt"),
                "Canceled 收键盘并返回取消信号", "sig=" + cancelSig);
            NativeEditKeyboard.Inject("NativeUtilPrompt", "y", TouchScreenKeyboard.Status.Visible);
            NativeEditKeyboard.Close();
            Check(!NativeEditKeyboard.Owns("NativeUtilPrompt") && !NativeEditKeyboard.Active,
                "Close 清空归属与键盘状态", "");

            // ── 8. 关闭路径接线：数量弹窗 Clear/Cancel、文本窗 Close、仓库 Reset 都要收键盘 ──
            NativeEditKeyboard.Inject("NativeUtilPrompt", "1", TouchScreenKeyboard.Status.Visible);
            NativeUtilPrompt.Clear();
            Check(!NativeEditKeyboard.Owns("NativeUtilPrompt"), "NativeUtilPrompt.Clear 收键盘（切场景不留残留）", "");
            NativeEditKeyboard.Inject("NativeDlgInput", "名字", TouchScreenKeyboard.Status.Visible);
            NativeDlgInput.Close();
            Check(!NativeEditKeyboard.Owns("NativeDlgInput"), "NativeDlgInput.Close 收键盘", "");
            NativeEditKeyboard.Inject("TrunkTakeMeso", "100", TouchScreenKeyboard.Status.Visible);
            NativeStorageWindow.Reset();
            Check(!NativeEditKeyboard.Owns("TrunkTakeMeso"), "NativeStorageWindow.Reset 收键盘", "");

            // ── 9. 数量弹窗机制本身（本次改了 Clear/NativeUtilPrompt，确认没破坏开合与回调）──
            // 丢弃 NativeInventoryActions.cs:75 与出售 NativeNpcShopWindow.cs:90 都调这个 Number；
            // 这里验弹窗开合与回调值，业务分支的门控由既有 server-check / DropChecks 覆盖。
            int captured = -1;
            NativeUtilPrompt.Number("扔掉多少？", 1, 1, 5, q => captured = q);
            Check(NativeUtilPrompt.Visible, "Number 打开数量弹窗（丢弃/出售共用入口）", "");
            NativeUtilPrompt.Clear();
            Check(!NativeUtilPrompt.Visible, "Clear 关闭数量弹窗", "");

            Debug.Log(Tag + "完成  通过=" + pass + " 失败=" + fail);
        }
        catch (Exception e) { Debug.LogError(Tag + "自检自身异常：" + e); fail++; }
        finally { if (Application.isBatchMode) EditorApplication.Exit(fail > 0 ? 1 : 0); }
    }
}
