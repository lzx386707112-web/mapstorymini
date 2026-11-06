#!/bin/bash
# 一键：构建 APK → 有可用手机时安装并启动（资源由用户单独传输）
# 用法：
#   ./build_android.sh           # 构建；手机未连接时自动跳过安装与启动
#   ./build_android.sh --no-install   # 只出 APK，不碰手机
set -e

UNITY="/Applications/Unity/Unity.app/Contents/MacOS/Unity"
# 工程根目录 = 本脚本所在目录，仓库移动/换机器都不必再改路径
PROJ="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ADB="$HOME/Library/Android/sdk/platform-tools/adb"
SDK="$HOME/Library/Android/sdk"
NDK="$SDK/ndk/23.1.7779620"
LOG="/tmp/build_android.log"
APP_ID="com.mapledash.game"
T0=$(date +%s)

NO_INSTALL=0
SKIP_INSTALL_REASON="--no-install"
[ "$1" = "--no-install" ] && NO_INSTALL=1

# ---------- 0/5 前置检查（早失败，省得白等三分钟）----------
echo "== 0/5 前置检查 =="

# 同一工程同时只能有一个 Unity 进程：Unity 退出会清空 Temp/，并发时另一个实例
# 会把构建中的 Temp/StagingArea 删掉，报 EditorToUnityLinkerData.json 找不到。
# 这里等待而不是 pkill —— 构建过程中别的任务启动 Unity 是没法靠启动瞬间检查挡住的。
LOCK="$PROJ/tools/unity-lock.sh"
if [ -x "$LOCK" ]; then
  echo "   检查工程占用…"
  "$LOCK" wait 900 || exit 1
else
  # 锁脚本不在时退回原来的单点检查（不能防止构建中途被别的 Unity 撞上）
  if pgrep -f "Unity.app/Contents/MacOS/Unity" >/dev/null 2>&1; then
    echo "❌ 有 Unity 进程占用工程，请先退出后重跑。"; exit 1
  fi
fi

if [ ! -d "$PROJ/Assets" ]; then
  echo "❌ 找不到工程：$PROJ"; exit 1
fi

if [ "$NO_INSTALL" = "0" ]; then
  STATE=$("$ADB" get-state 2>/dev/null || echo "none")
  # adb 守护进程偶尔会因端口占用/旧进程残留起不来，先自愈一次
  if [ "$STATE" != "device" ]; then
    echo "   adb 状态异常（${STATE}），重启 adb 服务…"
    "$ADB" kill-server >/dev/null 2>&1 || true
    sleep 2
    "$ADB" start-server >/dev/null 2>&1 || true
    sleep 1
    STATE=$("$ADB" get-state 2>/dev/null || echo "none")
  fi
  if [ "$STATE" != "device" ]; then
    echo "   手机未连接或不可用（adb 状态：${STATE}），继续构建 APK，跳过安装与启动。"
    NO_INSTALL=1
    SKIP_INSTALL_REASON="无可用手机"
  else
    echo "设备：$("$ADB" shell getprop ro.product.model 2>/dev/null | tr -d '\r')"
  fi
fi

# ---------- 0.5/5 Schema 同步 ----------
# Unity 只把 .txt/.json/.bytes 等已知扩展名当 TextAsset 收录，Schema.sql 放在
# Assets/Scripts 下不会被 Resources.Load 找到，真机会抛「Schema.sql 缺失」。
# Assets/Editor/SchemaSync.cs 会在编辑器内自动跟随，这里再兜一道，批处理构建也稳。
SCHEMA_SRC="$PROJ/Assets/Scripts/Server/Db/Schema.sql"
SCHEMA_DST="$PROJ/Assets/Resources/Server/Db/Schema.txt"
if [ ! -f "$SCHEMA_SRC" ]; then
  echo "❌ 找不到 $SCHEMA_SRC"; exit 1
fi
mkdir -p "$(dirname "$SCHEMA_DST")"
if ! cmp -s "$SCHEMA_SRC" "$SCHEMA_DST"; then
  cp "$SCHEMA_SRC" "$SCHEMA_DST"
  echo "   已同步 Schema.sql → Assets/Resources/Server/Db/Schema.txt"
fi
if [ ! -s "$SCHEMA_DST" ]; then
  echo "❌ 同步后的 Schema.txt 为空"; exit 1
fi

# ---------- 1/5 构建 ----------
echo "== 1/5 构建（IL2CPP / ARM64，约 2–6 分钟）=="
cd "$PROJ"
# 代理会让 Gradle 下载 dl.google.com 时 TLS 握手被中断，这里显式去掉
unity_build() {
  env -u http_proxy -u https_proxy -u HTTP_PROXY -u HTTPS_PROXY \
      -u all_proxy -u ALL_PROXY \
      ANDROID_NDK_ROOT="$NDK" ANDROID_NDK_HOME="$NDK" \
      ANDROID_HOME="$SDK" ANDROID_SDK_ROOT="$SDK" \
      "$UNITY" -batchmode -quit -projectPath "$PROJ" \
      -executeMethod BuildToDevice.BuildIl2cpp -logFile "$LOG" || true
}

unity_build

# 上一轮打过 WebGL 包时活动平台会是 WebGL，此时 BuildToDevice 只切平台就退出了
# （同进程切平台会让 BuildPlayer 读不到架构），这里补一次切换再构建，不用手工重跑。
if ! grep -q "结果=Succeeded" "$LOG" && grep -q "活动平台不是 Android" "$LOG"; then
  echo "   活动平台不是 Android：切平台要单独一个进程，切完自动接着构建…"
  env -u http_proxy -u https_proxy -u HTTP_PROXY -u HTTPS_PROXY \
      -u all_proxy -u ALL_PROXY \
      ANDROID_NDK_ROOT="$NDK" ANDROID_NDK_HOME="$NDK" \
      ANDROID_HOME="$SDK" ANDROID_SDK_ROOT="$SDK" \
      "$UNITY" -batchmode -quit -projectPath "$PROJ" \
      -executeMethod BuildToDevice.SwitchToAndroid -logFile "$LOG.switch" || true
  grep -E "\[Build\]" "$LOG.switch" | tail -2
  unity_build
fi

# 构建过程中别的 Unity 实例启动/退出会清掉 Temp/StagingArea，
# 表现为 Data/Managed 建不出来 -> EditorToUnityLinkerData.json 找不到。
# 单纯清缓存没用（下次仍会被撞），这里等占用者退出、清污染缓存后自动重来一次。
if ! grep -q "结果=Succeeded" "$LOG" &&
   grep -q -E "EditorToUnityLinkerData|CreateDirectory .*StagingArea.*failed" "$LOG"; then
  echo "⚠️  Temp/StagingArea 被并发的 Unity 实例清掉，等占用者退出后自动重来一次…"
  if [ -x "$LOCK" ]; then "$LOCK" wait 900 || exit 1; fi
  rm -rf "$PROJ/Temp" "$PROJ/Library/BuildPlayerData"
  unity_build
fi

if ! grep -q "结果=Succeeded" "$LOG"; then
  echo "❌ 构建失败，日志关键行："
  grep -E "error CS|Exception|FAILURE:|\[Build\]" "$LOG" | tail -20
  echo "   完整日志：$LOG"
  exit 1
fi
# 编译错误也会让上次的 APK 留在原地，这里顺手挡掉"装到旧包"的假成功
if grep -q "error CS" "$LOG"; then
  echo "❌ 有 C# 编译错误，APK 可能是上一次的残留："
  grep "error CS" "$LOG" | head -10
  exit 1
fi
grep "\[Build\] 结果" "$LOG" | tail -1
ls -lh "$PROJ/Builds/MapleDash.apk"

if [ "$NO_INSTALL" = "1" ]; then
  echo "已跳过安装（${SKIP_INSTALL_REASON}）。APK：$PROJ/Builds/MapleDash.apk"
  echo "APK 不含完整 Data；资源请自行放到 /sdcard/mapstory，安装可用 ./install_apk.sh。"
  exit 0
fi

# ---------- 2/5 安装 ----------
echo "== 2/5 安装 =="

# 屏幕尺寸提前取好。安装过程中绝不能再发任何 adb 命令——
# 并发会打断 streamed install，报 "protocol fault (couldn't read status): Connection reset by peer"
SIZE=$("$ADB" shell wm size 2>/dev/null | grep -o -E "[0-9]+x[0-9]+" | head -1)
W=$(echo "$SIZE" | cut -dx -f1)
H=$(echo "$SIZE" | cut -dx -f2)

INSTOK=0
for TRY in 1 2 3; do
  "$ADB" install -r -d "$PROJ/Builds/MapleDash.apk" > /tmp/install_apk.log 2>&1 &
  INSTALL_PID=$!
  WAITED=0
  TAPPED=0
  # 只轮询日志文件，不碰 adb
  while kill -0 $INSTALL_PID 2>/dev/null; do
    if [ $WAITED -ge 120 ]; then
      # 卡住了：绝大多数是厂商「继续安装」弹窗在等人点（红魔不会，OPPO/vivo 会）
      if [ "$TAPPED" = "0" ] && [ -n "$W" ] && [ -n "$H" ]; then
        echo "   安装无响应，尝试点掉厂商确认弹窗…"
        "$ADB" shell input tap $((W * 286 / 1000)) $((H * 968 / 1000)) >/dev/null 2>&1 || true
        TAPPED=1
        WAITED=0
        continue
      fi
      echo "   超时，终止本次安装"
      kill -9 $INSTALL_PID 2>/dev/null || true
      break
    fi
    sleep 1
    WAITED=$((WAITED + 1))
  done
  wait $INSTALL_PID 2>/dev/null || true
  cat /tmp/install_apk.log

  if grep -q "Success" /tmp/install_apk.log; then INSTOK=1; break; fi
  if [ $TRY -lt 3 ]; then
    echo "   第 $TRY 次失败，重启 adb 后重试…"
    "$ADB" kill-server >/dev/null 2>&1 || true
    sleep 2
    "$ADB" start-server >/dev/null 2>&1 || true
    sleep 1
  fi
done

if [ "$INSTOK" != "1" ]; then
  echo "❌ 安装失败（已重试 3 次）"
  echo "   排查：数据线换口 / 手机上重开 USB 调试 / adb kill-server 后重试"
  exit 1
fi

# ---------- 3/5 启动 ----------
echo "== 3/5 启动 =="
"$ADB" shell am force-stop "$APP_ID" 2>/dev/null || true
"$ADB" shell monkey -p "$APP_ID" -c android.intent.category.LAUNCHER 1 >/dev/null 2>&1
sleep 6

# ---------- 4/5 异常自检 ----------
echo "== 4/5 异常自检 =="
ERRS=$("$ADB" logcat -d -s Unity 2>/dev/null | grep -c "Exception" || true)
if [ "${ERRS:-0}" -gt 0 ]; then
  echo "⚠️  运行日志里有 $ERRS 条 Exception："
  "$ADB" logcat -d -s Unity 2>/dev/null | grep "Exception" | head -5
else
  echo "   无 Exception ✓"
fi

# ---------- 5/5 截图 ----------
echo "== 5/5 截图 =="
"$ADB" exec-out screencap -p > /tmp/game_latest.png 2>/dev/null || true
[ -s /tmp/game_latest.png ] && echo "   /tmp/game_latest.png"

echo ""
echo "全部完成，用时 $(( $(date +%s) - T0 ))s"
echo "看实时日志：$ADB logcat -s Unity"
