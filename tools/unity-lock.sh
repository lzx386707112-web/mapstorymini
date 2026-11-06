#!/bin/bash
# 同一 MapleDash 工程在同一时刻只允许一个 Unity 进程。
#
# 原因：Unity 退出时会清空 Temp/。两个实例并发时，正在构建的 A 的
# Temp/StagingArea 会被 B 删掉，日志表现为（顺序固定）：
#   Failed to save scene Temp/__Backupscenes/0.backup
#   Not rebuilding Data files -- no changes
#   CreateDirectory '.../Temp/StagingArea/Data/Managed' failed: No such file or directory
#   Could not write ... Temp/StagingArea/res.*.png        （24 条图标写入失败）
#   DirectoryNotFoundException: .../EditorToUnityLinkerData.json
# 清 Temp 或 Library/BuildPlayerData 只能治一次；只要还有并发就必然复发。
#
# 用法：
#   tools/unity-lock.sh wait [超时秒]         等其它 Unity 实例退出（默认 900）
#   tools/unity-lock.sh run <cmd> [args...]   等待 -> 持锁执行 -> 释放
#   tools/unity-lock.sh holders               只列占用本工程的 Unity PID
#   tools/unity-lock.sh status                查看占用者
set -u

PROJ="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LOCKDIR="$PROJ/Library/.unity-exclusive.lock"
UNITY_MARK="Unity.app/Contents/MacOS/Unity"

# 只统计命令行里明确带 -projectPath <本工程> 的 Unity 主进程。
# 不用 ps（本机受限），pgrep -fl 已经带完整命令行。
holders() {
  pgrep -fl "$UNITY_MARK" 2>/dev/null \
    | grep -F -- "-projectPath $PROJ" \
    | awk '{print $1}' || true
}

# 返回 0 = 锁目录存在但持有者已死，可以清掉；返回 1 = 没有锁或锁还活着。
stale_lock() {
  [ -d "$LOCKDIR" ] || return 1
  local owner pid
  owner="$LOCKDIR/owner"
  [ -f "$owner" ] || return 0
  pid=$(awk '{print $1}' "$owner" 2>/dev/null)
  [ -z "$pid" ] && return 0
  kill -0 "$pid" 2>/dev/null && return 1
  return 0
}

wait_idle() {
  local limit="${1:-900}" waited=0 n pids
  while :; do
    if stale_lock; then rm -rf "$LOCKDIR" 2>/dev/null || true; fi
    pids=$(holders)
    n=$(printf '%s' "$pids" | grep -c '[0-9]' || true)
    [ -d "$LOCKDIR" ] && n=$((n + 1))
    if [ "$n" = "0" ]; then return 0; fi
    if [ "$waited" -eq 0 ]; then
      echo "   等待其它 Unity 实例退出（PID: ${pids:-锁持有者}）…"
    fi
    if [ "$waited" -ge "$limit" ]; then
      echo "❌ 超时：仍有 ${n} 个 Unity 实例占用工程，已取消。" >&2
      return 1
    fi
    sleep 5
    waited=$((waited + 5))
  done
}

run() {
  if [ "$#" -eq 0 ]; then
    echo "用法: tools/unity-lock.sh run <cmd> [args...]" >&2
    exit 2
  fi
  wait_idle "${UNITY_LOCK_TIMEOUT:-900}" || exit 1
  local i=0
  while ! mkdir "$LOCKDIR" 2>/dev/null; do
    i=$((i + 1))
    if [ "$i" -gt 60 ]; then
      echo "❌ 60 秒内拿不到工程锁，已取消。" >&2
      exit 1
    fi
    sleep 1
  done
  echo "$$ $(date +%s) $*" > "$LOCKDIR/owner"
  trap 'rm -rf "$LOCKDIR"' EXIT INT TERM
  "$@"
}

status() {
  local pids
  pids=$(holders)
  echo "工程：${PROJ}"
  if [ -n "$pids" ]; then
    echo "占用中的 Unity PID: $(echo "$pids" | tr '\n' ' ')"
  else
    echo "没有 Unity 进程占用本工程"
  fi
  if [ -d "$LOCKDIR" ]; then
    echo "工程锁：$(cat "$LOCKDIR/owner" 2>/dev/null || echo 未知持有者)"
  else
    echo "工程锁：空闲"
  fi
}

case "${1:-status}" in
  wait)    shift; wait_idle "${1:-900}" ;;
  run)     shift; run "$@" ;;
  holders) holders ;;
  status)  status ;;
  *)       echo "用法: tools/unity-lock.sh {wait|run|holders|status}" >&2; exit 2 ;;
esac
