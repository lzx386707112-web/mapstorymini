#!/bin/sh
# 起一个静态服务器跑 WebGL 构建（浏览器调试用）。
# 用法：tools/serve_webgl.sh [端口]，默认 8099。
# 注意：不能双击 index.html 直接开（file:// 下 WebGL 的 fetch / wasm 会被浏览器拦掉）。
set -e
PORT="${1:-8099}"
DIR="$(cd "$(dirname "$0")/.." && pwd)/Builds/WebGL"

if [ ! -f "$DIR/index.html" ]; then
  echo "找不到 $DIR/index.html，先构建："
  echo "  Unity -batchmode -quit -projectPath . -executeMethod BuildWebGL.SwitchToWebGL"
  echo "  Unity -batchmode -quit -projectPath . -executeMethod BuildWebGL.Build"
  exit 1
fi

echo "http://localhost:$PORT/  ← 打开这个（Ctrl+C 停止）"
cd "$DIR"
exec python3 -m http.server "$PORT"
