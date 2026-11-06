#!/usr/bin/env bash
# 打「增量补丁包」：和上次发到手机上的清单比，只收新增/修改文件 + 完整目标清单 + 补丁标记。
#
# 和全量包用同一套分包规则：超过 --part-size（默认 2G）自动切成
#   MapleDashResources.patch.part01.zip / .part02.zip …
# 每个分包仍是完整 ZIP，第 1 包带 resource-manifest.json + resource-patch.json。
#
# 用法:
#   ./pack_resource_patch.sh /绝对路径/releases/v1.manifest.json
#   ./pack_resource_patch.sh releases/v1.manifest.json --part-size 2G
#   ./pack_resource_patch.sh releases/v1.manifest.json --resources /别的资源目录
set -euo pipefail
PROJ="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RESOURCES="$(cd "$PROJ/../MapleDashResources" 2>/dev/null && pwd || echo "$PROJ/../MapleDashResources")"
PY="${PY:-python3}"

usage() {
  echo "用法: $0 <上次发布的 manifest.json> [--part-size 2G] [--resources 资源目录]" >&2
  echo "  --part-size  单个分包上限，默认 2G；0 = 不分包" >&2
  echo "  --resources  Data 所在目录，默认 ${RESOURCES}" >&2
}

BASE=""
PART_SIZE="2G"
while [[ $# -gt 0 ]]; do
  case "$1" in
    --part-size)   PART_SIZE="${2:?--part-size 需要参数（例如 2G / 0）}"; shift 2 ;;
    --part-size=*) PART_SIZE="${1#*=}"; shift ;;
    --resources)   RESOURCES="${2:?--resources 需要目录}"; shift 2 ;;
    --resources=*) RESOURCES="${1#*=}"; shift ;;
    -h|--help) usage; exit 0 ;;
    -*) echo "未知参数: $1" >&2; usage; exit 2 ;;
    *) if [[ -n "$BASE" ]]; then echo "只能给一个基线清单" >&2; usage; exit 2; fi
       BASE="$1"; shift ;;
  esac
done
if [[ -z "$BASE" ]]; then usage; exit 2; fi
if [[ ! -f "$BASE" ]]; then echo "基线清单不存在: $BASE" >&2; exit 2; fi

echo "==> 打增量补丁包（基线: ${BASE}，单个分包上限 ${PART_SIZE}）"
"$PY" "$PROJ/tools/build_resource_pack.py" --base "$BASE" --resources "$RESOURCES" --part-size "$PART_SIZE"

echo "==> 校验补丁包结构"
"$PY" "$PROJ/tools/check_resource_pack.py" --resources "$RESOURCES" --patch --limit "$PART_SIZE"

echo
echo "补丁包在: $RESOURCES"
echo "  - 手动部署：把 MapleDashResources.patch.zip（或全部分包）放进 /sdcard/mapstory/ 后重启游戏。"
echo "  - 在线发布：分包逐个上传到可直链下载的位置；只有一个包时 resUpdate.json 用 resUrl，"
echo "    多个分包时用 resParts 数组按包号顺序列出。别把不同版本的整包和补丁放在一起。"
