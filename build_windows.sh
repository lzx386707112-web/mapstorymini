#!/bin/bash
# 一键出 Windows 桌面版（在 macOS 上交叉构建，Mono 后端）。
#
# 产物：Builds/Windows/MapleDash.exe（+ MapleDash_Data/、UnityPlayer.dll、MonoBleedingEdge/…）
# 资源不进安装包：目标机上把 MapleDashResources.zip / 全部分包 / 已展开的 Data 放到 D:\mapstory 即可，
# 客户端启动时只确认 Data 就绪（没有联网更新检查，也没有屏上虚拟按键）。
#
# 用法：
#   ./build_windows.sh           # 只出包
#   ./build_windows.sh --zip     # 出包后压成 Builds/MapleDash-Windows.zip，方便整包拷给测试机
set -e

UNITY="/Applications/Unity/Unity.app/Contents/MacOS/Unity"
# 工程根目录 = 本脚本所在目录，仓库移动/换机器都不必再改路径
PROJ="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LOG="/tmp/build_windows.log"
OUT="$PROJ/Builds/Windows"
EXE="$OUT/MapleDash.exe"
ZIP="$PROJ/Builds/MapleDash-Windows.zip"
T0=$(date +%s)

MAKE_ZIP=0
[ "$1" = "--zip" ] && MAKE_ZIP=1

# ---------- 0/4 前置检查（早失败，省得白等）----------
echo "== 0/4 前置检查 =="

# 同一工程同时只能有一个 Unity 进程：Unity 退出会清空 Temp/，并发时另一个实例
# 会把构建中的 Temp/StagingArea 删掉，报 EditorToUnityLinkerData.json 找不到。
LOCK="$PROJ/tools/unity-lock.sh"
if [ -x "$LOCK" ]; then
  echo "   检查工程占用…"
  "$LOCK" wait 900 || exit 1
else
  if pgrep -f "Unity.app/Contents/MacOS/Unity" >/dev/null 2>&1; then
    echo "❌ 有 Unity 进程占用工程，请先退出后重跑。"; exit 1
  fi
fi

if [ ! -d "$PROJ/Assets" ]; then
  echo "❌ 找不到工程：${PROJ}"; exit 1
fi

# Windows Build Support (Mono) 模块不在时，SwitchActiveBuildTarget 会直接失败，
# 报错又埋在日志里；这里先查目录，直接把「怎么装」说清楚。
UNITY_APP=""
if [ -d "$(dirname "$UNITY")/../.." ]; then
  UNITY_APP="$(cd "$(dirname "$UNITY")/../.." && pwd)"
fi
VER=$(awk '/m_EditorVersion:/{print $2}' "$PROJ/ProjectSettings/ProjectVersion.txt" 2>/dev/null)
WIN_MODULE=""
for dir in \
  "$UNITY_APP/Contents/PlaybackEngines/WindowsStandaloneSupport" \
  "$(dirname "$UNITY_APP")/PlaybackEngines/WindowsStandaloneSupport" \
  "$HOME/Applications/Unity/Hub/Editor/${VER}/PlaybackEngines/WindowsStandaloneSupport" \
  "/Applications/Unity/Hub/Editor/${VER}/PlaybackEngines/WindowsStandaloneSupport"
do
  [ -d "$dir" ] && WIN_MODULE="$dir" && break
done
if [ -z "$WIN_MODULE" ]; then
  echo "❌ 没装 Windows Build Support (Mono)，无法构建 Windows 目标。"
  echo "   装法：Unity Hub → Installs → 版本 ${VER} 右侧 ⋯ → Add modules → 勾"
  echo "        「Windows Build Support (Mono)」→ Install，装完重跑本脚本。"
  echo "   离线装法：Unity 下载页找 ${VER} 的 Windows Build Support (Mono) 模块包，装到同一目录。"
  exit 1
fi
echo "   Windows 构建模块：${WIN_MODULE}"

# ---------- 0.5/4 Schema 同步 ----------
# Unity 只把 .txt/.json/.bytes 当 TextAsset；Schema.sql 放 Assets/Scripts 下不会被 Resources.Load 找到。
SCHEMA_SRC="$PROJ/Assets/Scripts/Server/Db/Schema.sql"
SCHEMA_DST="$PROJ/Assets/Resources/Server/Db/Schema.txt"
if [ ! -f "$SCHEMA_SRC" ]; then
  echo "❌ 找不到 ${SCHEMA_SRC}"; exit 1
fi
mkdir -p "$(dirname "$SCHEMA_DST")"
if ! cmp -s "$SCHEMA_SRC" "$SCHEMA_DST"; then
  cp "$SCHEMA_SRC" "$SCHEMA_DST"
  echo "   已同步 Schema.sql → Assets/Resources/Server/Db/Schema.txt"
fi
if [ ! -s "$SCHEMA_DST" ]; then
  echo "❌ 同步后的 Schema.txt 为空"; exit 1
fi

# ---------- 1/4 切平台（单独一个进程）----------
# batchmode 下切平台要重载程序集，和随后的 BuildPlayer 放同一次运行里会读不到目标架构。
echo "== 1/4 切换到 Windows 平台 =="
cd "$PROJ"
env -u http_proxy -u https_proxy -u HTTP_PROXY -u HTTPS_PROXY -u all_proxy -u ALL_PROXY \
    "$UNITY" -batchmode -quit -projectPath "$PROJ" \
    -executeMethod BuildToDevice.SwitchToWindows -logFile "$LOG.switch" || true
if grep -q "切换平台=True" "$LOG.switch"; then
  echo "   已切到 Windows"
elif grep -q "活动平台已是 Windows" "$LOG.switch"; then
  echo "   活动平台本来就是 Windows"
else
  echo "❌ 切平台失败，日志关键行："
  grep -E "error|Exception|切换平台|\[Build\]" "$LOG.switch" | tail -15
  echo "   完整日志：$LOG.switch"
  exit 1
fi

# ---------- 2/4 构建 ----------
echo "== 2/4 构建（Windows x64 / Mono，约 1–4 分钟）=="
unity_build() {
  env -u http_proxy -u https_proxy -u HTTP_PROXY -u HTTPS_PROXY -u all_proxy -u ALL_PROXY \
      "$UNITY" -batchmode -quit -projectPath "$PROJ" \
      -executeMethod BuildToDevice.BuildWindows -logFile "$LOG" || true
}

unity_build

# 构建中被别的 Unity 实例清掉 Temp/StagingArea 时，等占用者退出、清缓存后重来一次。
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
  echo "   完整日志：${LOG}"
  exit 1
fi
# 编译错误也会让上一次的 exe 留在原地，这里顺手挡掉「拷走旧包」的假成功
if grep -q "error CS" "$LOG"; then
  echo "❌ 有 C# 编译错误，产物可能是上一次的残留："
  grep "error CS" "$LOG" | head -10
  exit 1
fi
grep "\[Build\] 结果" "$LOG" | tail -1

# ---------- 3/4 产物自检 ----------
echo "== 3/4 产物自检 =="
if [ ! -f "$EXE" ]; then
  echo "❌ 没生成 ${EXE}"; exit 1
fi
if [ ! -d "$OUT/MapleDash_Data" ]; then
  echo "❌ 缺少 MapleDash_Data 目录，包不完整"; exit 1
fi
echo "   可执行文件：$(ls -lh "$EXE" | awk '{print $5}')  $(basename "$EXE")"
du -sh "$OUT" | awk '{print "   整个目录：" $1}'

# ---------- 4/4 打压缩包（可选）----------
if [ "$MAKE_ZIP" = "1" ]; then
  echo "== 4/4 打包 =="
  rm -f "$ZIP"
  (cd "$PROJ/Builds" && zip -r -q -9 "$ZIP" Windows)
  ls -lh "$ZIP" | awk '{print "   " $9 "  " $5}'
fi

echo ""
echo "完成，用时 $(( $(date +%s) - T0 ))s"
echo "产物目录：${OUT}"
echo ""
echo "部署到 Windows 机器："
echo "  1) 整个 Builds/Windows 目录拷过去（缺 MapleDash_Data 或 UnityPlayer.dll 都起不来）"
echo "  2) 资源放到 D:\\mapstory\\，三选一："
echo "       - MapleDashResources.zip（整包）"
echo "       - MapleDashResources.part01.zip、part02.zip…（全部分包，缺一包会在解压前报错）"
echo "       - 已展开的 Data\\ 目录（直接从别的机器拷，能省一次解压）"
echo "  3) 双击 MapleDash.exe；启动页会显示实际读取的资源目录"
echo ""
echo "注意：构建完活动平台是 Windows，接着出 APK 请直接跑 ./build_android.sh（脚本会自己切回 Android）。"
