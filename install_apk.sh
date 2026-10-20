#!/bin/bash
# 装到已连接的手机并启动。
# 用法：./install_apk.sh [apk路径]
set -e

APK="${1:-/Users/lizixian/MapleDash/Builds/MapleDash.apk}"
ADB="$HOME/Library/Android/sdk/platform-tools/adb"
PKG="com.mapledash.game"
ACTIVITY="com.unity3d.player.UnityPlayerActivity"

if [ ! -f "$APK" ]; then
  echo "找不到 APK：$APK"
  exit 1
fi

echo "== 设备 =="
"$ADB" devices -l

echo "== 安装（覆盖旧版本）=="
"$ADB" install -r -d "$APK"

echo "== 授权悬浮窗/后台运行提示可忽略，直接启动 =="
"$ADB" shell monkey -p "$PKG" -c android.intent.category.LAUNCHER 1 >/dev/null 2>&1 || \
  "$ADB" shell am start -n "$PKG/$ACTIVITY"

echo "== 已启动，看日志：adb logcat -s Unity =="
