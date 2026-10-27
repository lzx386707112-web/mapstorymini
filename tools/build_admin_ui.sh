#!/usr/bin/env bash
# 构建后台前端并打包进 Unity Resources。
#
#   ./tools/build_admin_ui.sh            # 装依赖（首次）+ 构建 + 打包
#   ./tools/build_admin_ui.sh --skip-install
#
# 产物：Assets/Resources/AdminUi/adminui.bytes
# 运行期由 AdminHost 读取，浏览器打开 http://127.0.0.1:8686/ 即可。
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UI="$ROOT/AdminUi"
SKIP_INSTALL=0
[[ "${1:-}" == "--skip-install" ]] && SKIP_INSTALL=1

# 优先用工程约定的受管 Node，找不到再退回 PATH 里的 node
if [[ -x "$HOME/.workbuddy-ai/binaries/node/versions/22.22.2-6/bin/node" ]]; then
  export PATH="$HOME/.workbuddy-ai/binaries/node/versions/22.22.2-6/bin:$PATH"
fi

command -v node >/dev/null || { echo "找不到 node，请先安装 Node 18+"; exit 1; }
echo "node $(node -v)"

# 某些宿主（IDE/Agent 终端）会注入 NODE_OPTIONS=--require=... 的预加载钩子，
# 长构建里钩子的 IPC 断开会让 vite 以 EPIPE 崩掉，这里清掉。
unset NODE_OPTIONS

cd "$UI"
if [[ $SKIP_INSTALL -eq 0 && ! -d node_modules ]]; then
  echo "== 安装依赖 =="
  npm install --no-audit --no-fund
fi

echo "== 构建 =="
npx vite build --config ./config/vite.config.embed.ts

echo "== 打包 =="
python3 "$ROOT/tools/pack_admin_ui.py"
