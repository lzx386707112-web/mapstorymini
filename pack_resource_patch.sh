#!/usr/bin/env bash
# Compare against the manifest snapshot shipped to the phone last time.
set -euo pipefail
PROJ="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if [[ $# -lt 1 ]]; then
  echo "用法: $0 <上次发布的 manifest.json> [build_resource_pack.py 参数]" >&2
  exit 2
fi
BASE="$1"
shift
exec "${PY:-python3}" "$PROJ/tools/build_resource_pack.py" --base "$BASE" "$@"
