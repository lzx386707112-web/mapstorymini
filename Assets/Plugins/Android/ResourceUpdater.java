package com.mapledash.game;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.Context;
import android.util.AtomicFile;
import android.util.Log;
import android.view.View;
import android.widget.LinearLayout;
import android.widget.ProgressBar;
import android.widget.TextView;
import org.json.JSONObject;
import javax.net.ssl.HttpsURLConnection;
import java.io.*;

/** 授权后由资源加载器调用；下载在沙盒，目标目录通过流式复制交付给现有安装器。 */
public final class ResourceUpdater {
    private static final String MANIFEST = "https://gitee.com/lzx_xzl/mxd/raw/master/resUpdate.json";
    private static final File ROOT = new File("/sdcard/mapstory");
    // 0=检查/下载/安装中，1=补丁就绪待 C# 安装，2=继续原资源加载。
    public static volatile int state;
    private static Activity activity;
    private static JSONObject remote;
    private static AlertDialog progress;
    private static ProgressBar bar;
    private static TextView detail, percent;

    public static void begin(Activity host) {
        activity = host; state = 0;
        AppUpdater.prepareUi(host);
        new Thread(() -> {
            try {
                // Data 中的回执随资源原子切换；补偿切换后、写沙盒前进程被终止的情况。
                syncInstalled();
                long local = 1;
                try { local = Math.max(1, read(new File(host.getFilesDir(), "resUpdate.json")).getLong("versionCode")); }
                catch (Exception e) { Log.i("ResourceUpdate", "资源版本默认 1"); }
                HttpsURLConnection c = AppUpdater.connect(MANIFEST + "?t=" + System.currentTimeMillis());
                JSONObject next;
                try (InputStream in = c.getInputStream(); ByteArrayOutputStream out = new ByteArrayOutputStream()) {
                    byte[] buffer = new byte[4096]; int n;
                    while ((n = in.read(buffer)) != -1) {
                        if (out.size() + n > 65536) throw new IOException("资源配置过大");
                        out.write(buffer, 0, n);
                    }
                    next = new JSONObject(new String(out.toByteArray(), "UTF-8"));
                } finally { c.disconnect(); }
                long latest = next.getLong("versionCode");
                if (latest <= local) { state = 2; return; }
                if (latest - local > 1) {
                    final long current = local;
                    host.runOnUiThread(() -> dialog("资源包需要手动更新",
                        "当前资源版本：" + current + "\n最新资源版本：" + latest +
                        "\n\n资源包存在跨版本更新，请联系群主手动更新资源包。")
                        .setPositiveButton("退出应用", (d, w) -> {
                            activity.finishAffinity(); android.os.Process.killProcess(android.os.Process.myPid());
                        }).show());
                    return;
                }
                next.getString("resUrl");
                remote = new JSONObject().put("versionCode", next.getLong("versionCode"))
                    .put("resUrl", next.getString("resUrl"));
                host.runOnUiThread(() -> dialog("发现资源更新", "资源版本 " + remote.optLong("versionCode") +
                    "，下载后将自动应用补丁。")
                    .setPositiveButton("下载更新", (d, w) -> download())
                    .setNegativeButton("暂不更新", (d, w) -> state = 2).show());
            } catch (Exception e) {
                Log.w("ResourceUpdate", "资源检查失败，继续加载现有资源", e); state = 2;
            }
        }, "ResourceCheck").start();
    }
    private static AlertDialog.Builder dialog(String title, String message) {
        return new AlertDialog.Builder(AppUpdater.dialogContext()).setTitle(title).setMessage(message).setCancelable(false);
    }
    private static JSONObject read(File file) throws Exception {
        try (InputStream in = new AtomicFile(file).openRead(); ByteArrayOutputStream out = new ByteArrayOutputStream()) {
            byte[] buffer = new byte[4096]; int n;
            while ((n = in.read(buffer)) != -1) {
                if (out.size() + n > 65536) throw new IOException("资源版本回执过大");
                out.write(buffer, 0, n);
            }
            return new JSONObject(new String(out.toByteArray(), "UTF-8"));
        }
    }
    private static void syncInstalled() throws Exception {
        File marker = new File(ROOT, "Data/.resUpdate.json");
        if (!marker.exists()) return;
        JSONObject installed = read(marker);
        if (installed.getLong("versionCode") < 1) throw new IOException("无效资源版本回执");
        AtomicFile target = new AtomicFile(new File(activity.getFilesDir(), "resUpdate.json"));
        FileOutputStream out = null;
        try {
            out = target.startWrite(); out.write(installed.toString().getBytes("UTF-8")); target.finishWrite(out);
        } catch (Exception e) { if (out != null) target.failWrite(out); throw e; }
    }
    private static void error(String message) {
        dialog("资源更新失败", message).setPositiveButton("重试", (d, w) -> download())
            .setNegativeButton("退出应用", (d, w) -> {
                activity.finishAffinity(); android.os.Process.killProcess(android.os.Process.myPid());
            }).show();
    }
    private static void download() {
        Context context = AppUpdater.dialogContext();
        LinearLayout content = new LinearLayout(context);
        content.setOrientation(LinearLayout.VERTICAL);
        content.setPadding(AppUpdater.dp(24), AppUpdater.dp(12), AppUpdater.dp(24), AppUpdater.dp(24));
        detail = new TextView(context);
        detail.setTextAppearance(context, android.R.style.TextAppearance_Material_Body1);
        detail.setText("正在连接下载服务器…");
        content.addView(detail);
        bar = new ProgressBar(context, null, android.R.attr.progressBarStyleHorizontal);
        bar.setMax(100); bar.setIndeterminate(true);
        LinearLayout.LayoutParams barLayout = new LinearLayout.LayoutParams(
            LinearLayout.LayoutParams.MATCH_PARENT, AppUpdater.dp(8));
        barLayout.topMargin = AppUpdater.dp(20);
        content.addView(bar, barLayout);
        percent = new TextView(context);
        percent.setTextAppearance(context, android.R.style.TextAppearance_Material_Caption);
        percent.setText("下载完成后自动安装资源");
        percent.setTextAlignment(View.TEXT_ALIGNMENT_VIEW_END);
        LinearLayout.LayoutParams captionLayout = new LinearLayout.LayoutParams(
            LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT);
        captionLayout.topMargin = AppUpdater.dp(12);
        content.addView(percent, captionLayout);
        progress = new AlertDialog.Builder(context)
            .setTitle("正在下载资源补丁").setView(content).setCancelable(false).create();
        progress.show();
        new Thread(() -> {
            File sandbox = new File(activity.getCacheDir(), "resource-update.zip");
            File copy = new File(ROOT, "MapleDashResources.patch.zip.part");
            File target = new File(ROOT, "MapleDashResources.patch.zip");
            try {
                if (sandbox.exists() && !sandbox.delete()) throw new IOException("无法清理旧资源下载");
                HttpsURLConnection c = AppUpdater.connect(remote.getString("resUrl"));
                try {
                    long total = c.getContentLengthLong();
                    if (total > 0 && activity.getCacheDir().getUsableSpace() < total + 16777216L)
                        throw new IOException("沙盒存储空间不足");
                    long received = 0, last = 0;
                    try (InputStream in = c.getInputStream(); FileOutputStream out = new FileOutputStream(sandbox)) {
                        byte[] buffer = new byte[65536]; int n;
                        while ((n = in.read(buffer)) != -1) {
                            out.write(buffer, 0, n); received += n;
                            long now = android.os.SystemClock.elapsedRealtime();
                            if (now - last > 200) {
                                last = now; final long done = received;
                                activity.runOnUiThread(() -> {
                                    bar.setIndeterminate(total <= 0);
                                    if (total > 0) {
                                        int value = (int) Math.min(100, done * 100 / total);
                                        bar.setProgress(value); percent.setText(value + "%");
                                    }
                                    detail.setText(String.format(java.util.Locale.ROOT, "%.1f MB%s", done / 1048576.0,
                                        total > 0 ? String.format(java.util.Locale.ROOT, " / %.1f MB", total / 1048576.0) : ""));
                                });
                            }
                        }
                        out.getFD().sync();
                    }
                    if (received == 0 || (total > 0 && received != total)) throw new IOException("资源下载不完整");
                } finally { c.disconnect(); }
                activity.runOnUiThread(() -> { bar.setIndeterminate(true); detail.setText("正在复制资源补丁…"); percent.setText("请保持应用打开"); });
                if (!ROOT.isDirectory() && !ROOT.mkdirs()) throw new IOException("无法创建资源目录，请检查存储权限");
                if (ROOT.getUsableSpace() < sandbox.length() + 16777216L) throw new IOException("资源目录空间不足");
                // 文件不整包进入内存；复制完成并同步落盘后再改为正式文件名。
                try (InputStream in = new FileInputStream(sandbox); FileOutputStream out = new FileOutputStream(copy)) {
                    byte[] buffer = new byte[65536]; int n;
                    while ((n = in.read(buffer)) != -1) out.write(buffer, 0, n);
                    out.getFD().sync();
                }
                if (copy.length() != sandbox.length()) throw new IOException("资源复制不完整");
                if (target.exists() && !target.delete()) throw new IOException("无法清理旧资源补丁");
                if (!copy.renameTo(target)) throw new IOException("无法保存资源补丁");
                sandbox.delete();
                state = 1;
            } catch (Exception e) {
                sandbox.delete(); copy.delete();
                Log.w("ResourceUpdate", "下载或复制失败", e);
                activity.runOnUiThread(() -> { progress.dismiss(); error(e.getMessage()); });
            }
        }, "ResourceDownload").start();
    }
    public static String installing() {
        state = 0;
        activity.runOnUiThread(() -> { bar.setIndeterminate(true); detail.setText("正在解压并安装资源…"); percent.setText("完成后自动继续启动"); });
        return remote.toString();
    }
    public static void installProgress(int completed, int total, String phase) {
        activity.runOnUiThread(() -> {
            if (progress == null || !progress.isShowing()) return;
            detail.setText(phase);
            bar.setIndeterminate(total <= 0);
            if (total > 0) {
                int done = Math.max(0, Math.min(completed, total));
                int value = (int) ((long) done * 100 / total);
                bar.setProgress(value);
                percent.setText(value + "%");
            } else percent.setText("正在准备解压…");
        });
    }
    public static void installed(String failure) {
        if (failure.isEmpty()) {
            try { syncInstalled(); }
            catch (Exception e) { failure = "资源安装成功，保存版本失败：" + e.getMessage(); }
        }
        final String message = failure;
        activity.runOnUiThread(() -> {
            progress.dismiss();
            if (message.isEmpty()) state = 2;
            else error(message);
        });
    }
}
