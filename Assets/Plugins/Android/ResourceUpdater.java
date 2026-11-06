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
                String url = next.optString("resUrl", "");
                org.json.JSONArray parts = next.optJSONArray("resParts");
                if (parts != null && parts.length() == 0) parts = null;
                if (parts == null && url.isEmpty()) throw new IOException("资源地址为空");
                if (parts != null && parts.length() > 99) throw new IOException("资源分包过多");
                remote = new JSONObject().put("versionCode", next.getLong("versionCode")).put("resUrl", url);
                if (parts != null) remote.put("resParts", parts);
                final int partCount = parts == null ? 1 : parts.length();
                host.runOnUiThread(() -> dialog("发现资源更新", "资源版本 " + remote.optLong("versionCode") +
                    (partCount > 1 ? "（共 " + partCount + " 个分包）" : "") +
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
    /** resParts（多分包）优先，否则退回单个 resUrl。 */
    private static String[] downloadUrls() throws Exception {
        org.json.JSONArray parts = remote.optJSONArray("resParts");
        if (parts != null && parts.length() > 0) {
            String[] urls = new String[parts.length()];
            for (int i = 0; i < urls.length; i++) urls[i] = parts.getString(i);
            return urls;
        }
        return new String[] { remote.getString("resUrl") };
    }

    /** 与打包脚本的分包命名对齐：MapleDashResources.patch.part01.zip … */
    private static String partName(int index, int count) {
        return count <= 1 ? "MapleDashResources.patch.zip"
            : String.format(java.util.Locale.ROOT, "MapleDashResources.patch.part%02d.zip", index + 1);
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
            final java.util.List<File> staged = new java.util.ArrayList<>();
            try {
                String[] urls = downloadUrls();
                long total = 0;
                for (int i = 0; i < urls.length; i++) {
                    // 先全部落成 *.zip.part：中途被杀或失败时，C# 看不到正式分包，不会拿缺包的集合去安装。
                    File part = new File(ROOT, partName(i, urls.length) + ".part");
                    staged.add(part);
                    total += transfer(i, urls[i], urls.length, part);
                }
                if (total <= 0) throw new IOException("资源下载不完整");
                publish(staged, urls.length);
                state = 1;
            } catch (Exception e) {
                for (File file : staged) file.delete();
                // 改名到一半失败会留下缺包的正式文件，宁可全删也不要让 C# 装一份残包。
                dropStaleParts();
                Log.w("ResourceUpdate", "下载或复制失败", e);
                activity.runOnUiThread(() -> { progress.dismiss(); error(e.getMessage()); });
            }
        }, "ResourceDownload").start();
    }

    /** 沙盒下载完成后复制到 /sdcard/mapstory，长度一致并落盘才算成功；返回字节数。 */
    private static long transfer(int index, String url, int count, File stage) throws Exception {
        File sandbox = new File(activity.getCacheDir(), "resource-update-" + index + ".zip");
        String label = count > 1 ? "第 " + (index + 1) + "/" + count + " 包" : "资源包";
        try {
            if (sandbox.exists() && !sandbox.delete()) throw new IOException("无法清理旧资源下载");
            HttpsURLConnection c = AppUpdater.connect(url);
            long received = 0;
            try {
                long length = c.getContentLengthLong();
                if (length > 0 && activity.getCacheDir().getUsableSpace() < length + 16777216L)
                    throw new IOException("沙盒存储空间不足");
                long last = 0;
                try (InputStream in = c.getInputStream(); FileOutputStream out = new FileOutputStream(sandbox)) {
                    byte[] buffer = new byte[65536]; int n;
                    while ((n = in.read(buffer)) != -1) {
                        out.write(buffer, 0, n); received += n;
                        long now = android.os.SystemClock.elapsedRealtime();
                        if (now - last > 200) {
                            last = now; final long done = received; final long all = length;
                            activity.runOnUiThread(() -> {
                                bar.setIndeterminate(all <= 0);
                                if (all > 0) {
                                    int value = (int) Math.min(100, done * 100 / all);
                                    bar.setProgress(value); percent.setText(value + "%");
                                }
                                detail.setText(String.format(java.util.Locale.ROOT, "%s %.1f MB%s", label,
                                    done / 1048576.0, all > 0
                                        ? String.format(java.util.Locale.ROOT, " / %.1f MB", all / 1048576.0) : ""));
                            });
                        }
                    }
                    out.getFD().sync();
                }
                if (received == 0 || (length > 0 && received != length)) throw new IOException("资源下载不完整");
            } finally { c.disconnect(); }
            activity.runOnUiThread(() -> { bar.setIndeterminate(true); detail.setText("正在复制" + label + "…"); percent.setText("请保持应用打开"); });
            if (!ROOT.isDirectory() && !ROOT.mkdirs()) throw new IOException("无法创建资源目录，请检查存储权限");
            if (ROOT.getUsableSpace() < sandbox.length() + 16777216L) throw new IOException("资源目录空间不足");
            // 文件不整包进入内存；复制完成并同步落盘后才算这一包到位。
            try (InputStream in = new FileInputStream(sandbox); FileOutputStream out = new FileOutputStream(stage)) {
                byte[] buffer = new byte[65536]; int n;
                while ((n = in.read(buffer)) != -1) out.write(buffer, 0, n);
                out.getFD().sync();
            }
            if (stage.length() != sandbox.length()) throw new IOException("资源复制不完整");
            return received;
        } catch (Exception e) {
            stage.delete();
            throw e;
        } finally { sandbox.delete(); }
    }

    /** 所有分包都到位后一次性改名成正式文件名，并清掉上一次残留的整包/多余分包。 */
    private static void publish(java.util.List<File> staged, int count) throws IOException {
        dropStaleParts();
        for (File stage : staged) {
            String name = stage.getName();
            File target = new File(ROOT, name.substring(0, name.length() - ".part".length()));
            if (target.exists() && !target.delete()) throw new IOException("无法清理旧资源补丁");
            if (!stage.renameTo(target)) throw new IOException("无法保存资源补丁");
        }
    }

    /** 上一次可能留下整包或更多分包；不清掉会让手机端「包数对不上」直接拒绝解压。 */
    private static void dropStaleParts() {
        String[] names = ROOT.list();
        if (names == null) return;
        for (String name : names)
            if (name.equals("MapleDashResources.patch.zip") ||
                (name.startsWith("MapleDashResources.patch.part") && name.endsWith(".zip")))
                new File(ROOT, name).delete();
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
