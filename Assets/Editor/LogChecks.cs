using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// 日志落盘 / 轮转 / 导出的自检。
// 日志系统最容易出的错是「以为写了其实没写」（回调没挂上、目录不可写、共享读被写句柄挡掉），
// 这几条只能真跑一遍才知道，所以单独做一个可批处理执行的检查。
//
// 批处理：Unity -batchmode -quit -executeMethod LogChecks.Run -projectPath .
// 编辑器：菜单 Tools/检查/日志落盘与分享
public static class LogChecks
{
    static int passed, failed;

    static void Check(bool ok, string message)
    {
        if (ok) { passed++; Debug.Log("[LogCheck] ✓ " + message); }
        else { failed++; Debug.LogError("[LogCheck] ✗ " + message); }
    }

    [MenuItem("Tools/检查/日志落盘与分享")]
    public static void Run()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[LogCheck] 日志检查要在非播放模式下跑（播放时日志正被游戏写入，结果不可信）");
            return;
        }

        string dir = FileLog.Dir;
        string backup = dir + ".check-backup";
        bool moved = false;
        int savedPref = PlayerPrefs.GetInt(FileLog.PrefKey, 1);

        // 用独立的干净目录跑，测完把用户原来的日志放回去
        try
        {
            if (Directory.Exists(dir))
            {
                if (Directory.Exists(backup)) Directory.Delete(backup, true);
                Directory.Move(dir, backup);
                moved = true;
            }
            PlayerPrefs.DeleteKey(FileLog.PrefKey);   // 回到「出厂默认」，才能验证默认是开

            FileLog.Boot();
            Check(FileLog.Enabled, "日志默认开启（PlayerPrefs 缺省 = 开）");

            string main = Path.Combine(dir, "client.txt");
            Check(File.Exists(main), "启动即建日志文件");

            // 1) 普通日志落盘
            string mark = "CHECK-" + Guid.NewGuid().ToString("N").Substring(0, 10);
            Debug.Log("落盘测试 " + mark);
            FileLog.FlushNow();
            string text = FileLog.ReadShared(main, 0);
            Check(text.Contains(mark), "Debug.Log 内容已写入文件");
            Check(text.Contains("会话开始"), "会话头已写入（含时间与设备信息）");

            // 2) 错误日志带堆栈
            string errMark = "ERR-" + Guid.NewGuid().ToString("N").Substring(0, 10);
            Debug.LogError("错误测试 " + errMark);
            FileLog.FlushNow();
            text = FileLog.ReadShared(main, 0);
            Check(text.Contains(errMark), "LogError 内容已写入");
            Check(text.Contains(" E "), "错误级别标记正确");

            // 3) 导出（此时日志还很小，应该整份带走）
            string exported = LogShare.Export();
            Check(!string.IsNullOrEmpty(exported) && File.Exists(exported), "导出文件已生成");
            if (!string.IsNullOrEmpty(exported) && File.Exists(exported))
            {
                string bundled = File.ReadAllText(exported);
                Check(bundled.Contains("MapleDash 客户端日志"), "导出内容含标题头");
                Check(bundled.Contains(mark), "导出内容含本次会话日志");
                Check(bundled.Contains("屏幕"), "导出内容含设备/屏幕信息");
            }

            // 4) 关闭开关后不再写入
            FileLog.SetEnabled(false);
            Check(!FileLog.Enabled, "开关可关闭");
            string offMark = "OFF-" + Guid.NewGuid().ToString("N").Substring(0, 10);
            Debug.Log("关闭后不应写入 " + offMark);
            FileLog.FlushNow();
            Check(!FileLog.ReadShared(main, 0).Contains(offMark), "关闭后日志不再落盘");

            // 5) 重新打开能续写
            FileLog.SetEnabled(true);
            string onMark = "ON-" + Guid.NewGuid().ToString("N").Substring(0, 10);
            Debug.Log("重新打开后写入 " + onMark);
            FileLog.FlushNow();
            Check(FileLog.ReadShared(main, 0).Contains(onMark), "重新打开后继续写入");

            // 6) 轮转：灌到 3MB 上限以上，旧内容应滚到 client.1.txt
            for (int i = 0; i < 10; i++) FileLog.Note(new string('=', 500000));
            FileLog.FlushNow();
            Check(File.Exists(Path.Combine(dir, "client.1.txt")), "超过 3MB 触发轮转");
            Check(new FileInfo(main).Length < 3 * 1024 * 1024 + 1024, "当前文件不超过大小上限");

            // 7) 历史文件顺序：旧 → 新，最后一个必须是当前文件
            var history = FileLog.HistoryFiles();
            Check(history.Count >= 2, "历史文件列表包含轮转文件");
            Check(history.Count > 0 && history[history.Count - 1] == Path.Combine(dir, "client.txt"),
                  "历史列表按旧→新排序，末尾是当前文件");

            Debug.Log("[LogCheck] 通过 " + passed + " 失败 " + failed);
        }
        catch (Exception e)
        {
            failed++;
            Debug.LogError("[LogCheck] 异常中断：" + e);
        }
        finally
        {
            FileLog.SetEnabled(false);
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                if (moved && Directory.Exists(backup)) Directory.Move(backup, dir);
            }
            catch (Exception) { }
            PlayerPrefs.SetInt(FileLog.PrefKey, savedPref);
            PlayerPrefs.Save();
        }

        if (Application.isBatchMode) EditorApplication.Exit(failed > 0 ? 1 : 0);
    }
}
