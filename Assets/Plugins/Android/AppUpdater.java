package com.mapledash.game;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.Context;
import android.view.ContextThemeWrapper;
import android.view.View;
import android.widget.LinearLayout;
import android.widget.ProgressBar;
import android.widget.TextView;
import android.content.Intent;
import android.content.pm.PackageInfo;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.os.Build;
import android.provider.Settings;
import android.util.Log;
import android.util.AtomicFile;
import java.security.MessageDigest;
import org.json.JSONObject;
import javax.net.ssl.HttpsURLConnection;
import java.net.URL;
import java.io.*;
import java.util.Arrays;

/** Android 原生启动更新；所有 UI 在主线程，网络与 APK 校验在工作线程。 */
public final class AppUpdater {
    private static final String MANIFEST = "https://gitee.com/lzx_xzl/mxd/raw/master/update.json";
    public static volatile boolean finished;
    private static Activity activity;
    private static JSONObject remote;
    private static long localVersion;
    private static File apk;
    private static boolean waitingPermission, waitingInstaller;

    // 资源更新先执行时也可复用弹窗主题，不会提前发起 APK 检查。
    static void prepareUi(Activity host) { activity = host; }

    public static void begin(Activity host) {
        prepareUi(host);
        finished = false;
        remote = null;
        waitingPermission = waitingInstaller = false;
        new Thread(() -> {
            try {
                try { confirmInstalledUpdate(); }
                catch (Exception e) { Log.w("AppUpdate", "安装回执同步失败，保留原版本继续检查", e); }
                localVersion = readLocalVersion();
                HttpsURLConnection conn = connect(MANIFEST + "?t=" + System.currentTimeMillis());
                byte[] bytes;
                try (InputStream in = conn.getInputStream(); ByteArrayOutputStream out = new ByteArrayOutputStream()) {
                    byte[] buffer = new byte[4096]; int count;
                    while ((count = in.read(buffer)) != -1) {
                        if (out.size() + count > 65536) throw new IOException("更新配置超过大小限制");
                        out.write(buffer, 0, count);
                    }
                    bytes = out.toByteArray();
                } finally { conn.disconnect(); }
                JSONObject next = new JSONObject(new String(bytes, "UTF-8"));
                if (next.getLong("versionCode") <= localVersion) { finished = true; return; }
                next.getString("versionName"); next.getBoolean("forceUpdate"); next.getString("updateLog");
                // 发现新版本但地址缺失也保留强更门禁，显示可重试错误。
                remote = next;
                host.runOnUiThread(AppUpdater::offer);
            } catch (Exception e) {
                Log.w("AppUpdate", "更新检查失败，继续启动", e);
                finished = true;
            }
        }, "UpdateCheck").start();
    }

    // 版本回执存 files 而非 cache，覆盖安装后保留；仅确认安装成功时推进。
    private static File stateFile(String name) { return new File(activity.getFilesDir(), name); }
    private static JSONObject readJson(File file) throws Exception {
        AtomicFile atomic = new AtomicFile(file);
        try (InputStream in = atomic.openRead(); ByteArrayOutputStream out = new ByteArrayOutputStream()) {
            byte[] buffer = new byte[4096]; int n;
            while ((n = in.read(buffer)) != -1) {
                if (out.size() + n > 131072) throw new IOException("本地更新配置过大");
                out.write(buffer, 0, n);
            }
            return new JSONObject(new String(out.toByteArray(), "UTF-8"));
        }
    }
    private static void writeJson(File file, JSONObject value) throws Exception {
        AtomicFile atomic = new AtomicFile(file);
        FileOutputStream out = null;
        try {
            out = atomic.startWrite();
            out.write(value.toString().getBytes("UTF-8"));
            atomic.finishWrite(out);
        } catch (Exception e) { if (out != null) atomic.failWrite(out); throw e; }
    }
    private static long readLocalVersion() {
        try { return Math.max(1, readJson(stateFile("update.json")).getLong("versionCode")); }
        catch (Exception e) {
            Log.i("AppUpdate", "沙盒版本不存在或无效，按默认版本 1 检查更新");
            return 1;
        }
    }
    private static String sha256(File file) throws Exception {
        MessageDigest digest = MessageDigest.getInstance("SHA-256");
        try (InputStream in = new FileInputStream(file)) {
            byte[] buffer = new byte[65536]; int n;
            while ((n = in.read(buffer)) != -1) digest.update(buffer, 0, n);
        }
        StringBuilder result = new StringBuilder();
        for (byte b : digest.digest()) result.append(String.format(java.util.Locale.ROOT, "%02x", b & 255));
        return result.toString();
    }
    private static void confirmInstalledUpdate() throws Exception {
        File pendingFile = stateFile("update-pending.json");
        JSONObject pending;
        try { pending = readJson(pendingFile); }
        catch (FileNotFoundException e) { return; }
        catch (Exception e) {
            Log.w("AppUpdate", "忽略损坏的待安装回执", e);
            new AtomicFile(pendingFile).delete(); return;
        }
        PackageInfo current = activity.getPackageManager().getPackageInfo(activity.getPackageName(), 0);
        // 取消/失败不会改变安装时间；哈希确认实际装上的就是已下载的目标 APK。
        if (current.lastUpdateTime == pending.getLong("previousInstallTime")) return;
        if (pending.getString("apkSha256").equals(sha256(new File(current.applicationInfo.sourceDir)))) {
            JSONObject manifest = pending.getJSONObject("manifest");
            if (manifest.getLong("versionCode") > readLocalVersion())
                writeJson(stateFile("update.json"), manifest);
        }
        new AtomicFile(pendingFile).delete();
    }

    private static boolean forced() { return remote.optBoolean("forceUpdate"); }
    private static void leave() {
        if (forced()) { activity.finishAffinity(); android.os.Process.killProcess(android.os.Process.myPid()); }
        else finished = true;
    }
    private static AlertDialog.Builder dialog(String title, String message) {
        return new AlertDialog.Builder(dialogContext()).setTitle(title).setMessage(message).setCancelable(false);
    }
    // 显式选择 Material，避免 Unity Activity 的旧主题让系统弹窗退回 Holo 样式。
    static Context dialogContext() {
        return new ContextThemeWrapper(activity, android.R.style.Theme_Material_Light_Dialog_Alert);
    }
    static int dp(int value) {
        return Math.round(value * activity.getResources().getDisplayMetrics().density);
    }
    private static void offer() {
        String log = remote.optString("updateLog").trim();
        dialog("发现新版本", "版本 " + remote.optString("versionName") +
            (forced() ? " · 需要更新后继续使用" : "") +
            (log.isEmpty() ? "" : "\n\n" + log))
            .setPositiveButton("下载更新", (d, w) -> download())
            .setNegativeButton(forced() ? "退出应用" : "暂不更新", (d, w) -> leave()).show();
    }
    private static void error(String message, Runnable retry) {
        dialog("更新失败", message).setPositiveButton("重试", (d, w) -> retry.run())
            .setNegativeButton(forced() ? "退出应用" : "暂不更新", (d, w) -> leave()).show();
    }
    static HttpsURLConnection connect(String address) throws Exception {
        URL url = new URL(address);
        // Gitee raw 与 Releases 附件会重定向；只接受 HTTPS，并限制跳转次数。
        for (int i = 0; i < 8; i++) {
            if (!"https".equalsIgnoreCase(url.getProtocol())) throw new IOException("下载地址必须使用 HTTPS");
            HttpsURLConnection c = (HttpsURLConnection) url.openConnection();
            c.setInstanceFollowRedirects(false);
            c.setConnectTimeout(15000); c.setReadTimeout(30000);
            c.setRequestProperty("User-Agent", "MapleDash-Android-Updater");
            c.setRequestProperty("Cache-Control", "no-cache");
            int status = c.getResponseCode();
            if (status == 301 || status == 302 || status == 303 || status == 307 || status == 308) {
                String target = c.getHeaderField("Location"); c.disconnect();
                if (target == null) throw new IOException("下载重定向缺少地址");
                url = new URL(url, target); continue;
            }
            if (status != 200) { c.disconnect(); throw new IOException("服务器返回 HTTP " + status); }
            return c;
        }
        throw new IOException("下载重定向次数过多");
    }
    private static void download() {
        Context context = dialogContext();
        LinearLayout content = new LinearLayout(context);
        content.setOrientation(LinearLayout.VERTICAL);
        content.setPadding(dp(24), dp(12), dp(24), dp(24));
        TextView detail = new TextView(context);
        detail.setTextAppearance(context, android.R.style.TextAppearance_Material_Body1);
        detail.setText("正在连接下载服务器…");
        content.addView(detail);
        ProgressBar bar = new ProgressBar(context, null, android.R.attr.progressBarStyleHorizontal);
        bar.setMax(100); bar.setIndeterminate(true);
        LinearLayout.LayoutParams barLayout = new LinearLayout.LayoutParams(
            LinearLayout.LayoutParams.MATCH_PARENT, dp(8));
        barLayout.topMargin = dp(20);
        content.addView(bar, barLayout);
        TextView percent = new TextView(context);
        percent.setTextAppearance(context, android.R.style.TextAppearance_Material_Caption);
        percent.setText("下载完成后将打开系统安装界面");
        percent.setTextAlignment(View.TEXT_ALIGNMENT_VIEW_END);
        LinearLayout.LayoutParams captionLayout = new LinearLayout.LayoutParams(
            LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT);
        captionLayout.topMargin = dp(12);
        content.addView(percent, captionLayout);
        AlertDialog progress = new AlertDialog.Builder(context)
            .setTitle("正在下载更新").setView(content).setCancelable(false).create();
        progress.show();
        new Thread(() -> {
            File part = new File(activity.getCacheDir(), "app-update.part");
            apk = new File(activity.getCacheDir(), "app-update.apk");
            try {
                if (apk.exists() && !apk.delete()) throw new IOException("无法清理旧安装包");
                HttpsURLConnection c = connect(remote.getString("apkUrl"));
                try {
                    long total = c.getContentLengthLong();
                    if (total > 0 && activity.getCacheDir().getUsableSpace() < total + 16 * 1024 * 1024)
                        throw new IOException("存储空间不足");
                    try (InputStream in = c.getInputStream(); FileOutputStream out = new FileOutputStream(part)) {
                        byte[] buffer = new byte[65536]; long received = 0, last = 0; int count;
                        while ((count = in.read(buffer)) != -1) {
                            out.write(buffer, 0, count); received += count;
                            long now = android.os.SystemClock.elapsedRealtime();
                            if (now - last > 200) {
                                last = now; final long done = received;
                                activity.runOnUiThread(() -> {
                                    bar.setIndeterminate(total <= 0);
                                    if (total > 0) {
                                        int value = (int) Math.min(100, done * 100 / total);
                                        bar.setProgress(value);
                                        percent.setText(value + "%");
                                    }
                                    detail.setText(String.format(java.util.Locale.ROOT, "%.1f MB%s", done / 1048576.0,
                                        total > 0 ? String.format(java.util.Locale.ROOT, " / %.1f MB", total / 1048576.0) : ""));
                                });
                            }
                        }
                        if (received == 0 || (total > 0 && received != total)) throw new IOException("安装包下载不完整");
                    }
                } finally { c.disconnect(); }
                activity.runOnUiThread(() -> {
                    bar.setIndeterminate(true);
                    detail.setText("下载完成，正在校验安装包…");
                    percent.setText("校验完成后将打开系统安装界面");
                });
                verify(part);
                if (!part.renameTo(apk)) throw new IOException("无法保存安装包");
                JSONObject pending = new JSONObject();
                pending.put("manifest", new JSONObject(remote.toString()));
                pending.put("apkSha256", sha256(apk));
                pending.put("previousInstallTime", activity.getPackageManager()
                    .getPackageInfo(activity.getPackageName(), 0).lastUpdateTime);
                writeJson(stateFile("update-pending.json"), pending);
                activity.runOnUiThread(() -> { progress.dismiss(); install(); });
            } catch (Exception e) {
                part.delete();
                Log.w("AppUpdate", "下载或校验失败", e);
                activity.runOnUiThread(() -> { progress.dismiss(); error(e.getMessage(), AppUpdater::download); });
            }
        }, "UpdateDownload").start();
    }
    @SuppressWarnings("deprecation")
    private static void verify(File file) throws Exception {
        PackageManager pm = activity.getPackageManager();
        int flags = Build.VERSION.SDK_INT >= 28 ? PackageManager.GET_SIGNING_CERTIFICATES : PackageManager.GET_SIGNATURES;
        PackageInfo candidate = pm.getPackageArchiveInfo(file.getAbsolutePath(), flags);
        PackageInfo current = pm.getPackageInfo(activity.getPackageName(), flags);
        if (candidate == null || !activity.getPackageName().equals(candidate.packageName))
            throw new IOException("下载文件不是本应用的有效 APK");
        android.content.pm.Signature[] oldSign, newSign;
        if (Build.VERSION.SDK_INT >= 28) {
            oldSign = current.signingInfo.getApkContentsSigners();
            newSign = candidate.signingInfo.getApkContentsSigners();
        } else { oldSign = current.signatures; newSign = candidate.signatures; }
        if (oldSign == null || newSign == null || oldSign.length != newSign.length ||
            !new java.util.HashSet<>(Arrays.asList(oldSign)).equals(new java.util.HashSet<>(Arrays.asList(newSign))))
            throw new IOException("APK 签名不一致，请使用同一签名发布更新");
    }
    private static void install() {
        try {
            if (Build.VERSION.SDK_INT >= 26 && !activity.getPackageManager().canRequestPackageInstalls()) {
                dialog("允许安装更新", "请在系统设置中允许此应用安装未知应用，返回后将继续安装。")
                    .setPositiveButton("前往设置", (d, w) -> {
                        try {
                            waitingPermission = true;
                            activity.startActivity(new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES,
                                Uri.parse("package:" + activity.getPackageName())));
                        } catch (Exception e) { waitingPermission = false; error(e.getMessage(), AppUpdater::install); }
                    }).setNegativeButton(forced() ? "退出应用" : "暂不更新", (d, w) -> leave()).show();
                return;
            }
            Uri uri = Uri.parse("content://" + activity.getPackageName() + ".updates/app-update.apk");
            Intent intent = new Intent(Intent.ACTION_VIEW).setDataAndType(uri, "application/vnd.android.package-archive")
                .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
            waitingInstaller = true;
            activity.startActivity(intent);
        } catch (Exception e) { waitingInstaller = false; error(e.getMessage(), AppUpdater::install); }
    }
    public static void resume() {
        if (activity == null || finished) return;
        activity.runOnUiThread(() -> {
            if (waitingPermission) { waitingPermission = false; install(); }
            else if (waitingInstaller) {
                waitingInstaller = false;
                dialog("安装更新", "若取消了安装，可再次安装。安装完成后请重新打开应用。")
                    .setPositiveButton("重新安装", (d, w) -> install())
                    .setNegativeButton(forced() ? "退出应用" : "暂不更新", (d, w) -> leave()).show();
            }
        });
    }
}
