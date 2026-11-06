#!/usr/bin/env bash
#
# pack_resources.sh —— 把原生资源打成手机端可直接解压的资源包（超 2 GiB 自动分包）
#
# 为什么不能直接用系统“压缩”来打：
#   手机端 WzResourceInstaller 要求包内结构必须是：
#     - 根目录有 resource-manifest.json（资源清单）
#     - 文件入口为 Data/<相对路径>
#   系统压缩常常把整个文件夹包进一层顶层目录（MapleDashResources/...），
#   或只压了 Data 没压清单，installer 第一步就抛“资源包缺少清单”而失败。
#
# 为什么要分包：Data 已接近 10 GiB，单个 ZIP 会顶到传输/文件系统上限，
#   Android 侧解析超大 ZIP64 也不稳。脚本按 2 GiB 上限切成互相独立的
#   MapleDashResources.part01.zip / part02.zip ...，手机端按包号顺序逐包解压。
#
# 用法:
#   ./tools/pack_resources.sh                 # 用本地已有 Data/ + 清单打包（默认 2 GiB 分包）
#   ./tools/pack_resources.sh --regen         # 先从源端重建 Data + 清单（本地 Data 缺失时也会自动走这步）
#   ./tools/pack_resources.sh --part-size 0   # 不分包，打成一个整包
#   ./tools/pack_resources.sh --apk           # 打包后顺带重新构建 APK（让 APK 内嵌清单与包一致）
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJ="$(cd "$SCRIPT_DIR/.." && pwd)"                     # MapleDash 工程根（tools 的上一级）
RESOURCES="$(cd "$PROJ/../MapleDashResources" 2>/dev/null && pwd || echo "$PROJ/../MapleDashResources")"  # 资源根：Data/ + resource-manifest.json
MANIFEST="$PROJ/Assets/Resources/NativeAssetIndex.json"  # 打进包里的清单来源（= APK 内嵌清单）
SOURCE_DATA="$HOME/Documents/mxd/BeiDou-Server/clien/Data"
WZPY="$HOME/Documents/mxd/BeiDou-Server/tool/wz-python"
PY="${PY:-python3}"

REGEN=0; APK=0; PART_SIZE="2G"
while [[ $# -gt 0 ]]; do
  case "$1" in
    --regen) REGEN=1; shift ;;
    --apk)   APK=1; shift ;;
    --part-size) PART_SIZE="${2:?--part-size 需要参数（例如 2G / 0）}"; shift 2 ;;
    --part-size=*) PART_SIZE="${1#*=}"; shift ;;
    -h|--help) echo "用法: $0 [--regen] [--apk] [--part-size 2G]";
               echo "  --regen      先从源端重建 Data+清单 (package_native_assets.py)"
               echo "  --apk        打包后顺便重新构建 APK (build_android.sh)"
               echo "  --part-size  单个分包上限，默认 2G；0 = 不分包"
               exit 0 ;;
    *) echo "未知参数: $1" >&2; exit 2 ;;
  esac
done

cd "$PROJ"

# —— 1) 重建（可选 / 本地 Data 缺失时强制）——
if [[ $REGEN -eq 1 || ! -d "$RESOURCES/Data" ]]; then
  echo "==> [1/3] 从源端重建 Data + NativeAssetIndex.json + resource-manifest.json"
  "$PY" tools/package_native_assets.py --data "$SOURCE_DATA" --output "$RESOURCES" --wzpy "$WZPY" --all-maps
else
  echo "==> [1/3] 跳过重建（本地 $RESOURCES/Data 已存在）；如需从源端刷新请加 --regen"
fi

# —— 2) 打包（固定结构：清单 + Data/；超过上限自动切成多个独立 ZIP）——
echo "==> [2/3] 打包资源（单个分包上限 ${PART_SIZE}）"
"$PY" tools/build_resource_pack.py --resources "$RESOURCES" --part-size "$PART_SIZE"

# —— 3) 结构自检（清单/Data 条目/分包连续性/体积上限）——
echo "==> [3/3] 校验资源包结构"
"$PY" tools/check_resource_pack.py --resources "$RESOURCES" --limit "$PART_SIZE"

echo
echo "⚠️  包内的 resource-manifest.json 来自: $MANIFEST"
echo "    若该清单与手机上 APK 内嵌的不一致，手机会报『资源包版本与当前游戏不匹配』。"
echo "    如果你刚改过清单/资源，请一并重新构建 APK（本脚本加 --apk，或另跑 build_android.sh）。"
echo "    分包要全部传到 /sdcard/mapstory/（可以用 tools/sync_android_data.py --zip）；"
echo "    手机端按包号顺序逐包解压，缺任何一个包都会在开始解压前报错。"
echo "    安装成功后会自动删除所有分包；失败时保留分包供重试。"

if [[ $APK -eq 1 ]]; then
  echo "==> 重新构建 APK"
  ./build_android.sh
fi
