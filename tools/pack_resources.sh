#!/usr/bin/env bash
#
# pack_resources.sh —— 把原生资源打成手机端可直接解压的 MapleDashResources.zip
#
# 为什么不能直接用系统“压缩”来打：
#   手机端 WzResourceInstaller.Install 要求 ZIP 内部结构必须是：
#     - 根目录有 resource-manifest.json（资源清单）
#     - 文件入口为 Data/<相对路径>
#   系统压缩常常把整个文件夹包进一层顶层目录（MapleDashResources/...），
#   或只压了 Data 没压清单，installer 第一步就抛“资源包缺少清单”而失败。
#   本脚本固定用 build_resource_pack.py 生成正确结构，并做结构自检。
#
# 用法:
#   ./tools/pack_resources.sh            # 用本地已有 Data/ + 清单直接打包
#   ./tools/pack_resources.sh --regen    # 先从源端重建 Data + 清单（本地 Data 缺失时也会自动走这步）
#   ./tools/pack_resources.sh --apk      # 打包后顺带重新构建 APK（让 APK 内嵌清单与 zip 一致）
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJ="$(cd "$SCRIPT_DIR/.." && pwd)"                     # MapleDash 工程根（tools 的上一级）
RESOURCES="$PROJ/../MapleDashResources"                  # 资源根：Data/ + resource-manifest.json
MANIFEST="$PROJ/Assets/Resources/NativeAssetIndex.json"  # 打进 zip 的清单来源（= APK 内嵌清单）
SOURCE_DATA="$HOME/Documents/mxd/BeiDou-Server/clien/Data"
WZPY="$HOME/Documents/mxd/BeiDou-Server/tool/wz-python"
PY="${PY:-python3}"

REGEN=0; APK=0
for a in "$@"; do
  case "$a" in
    --regen) REGEN=1 ;;
    --apk)   APK=1 ;;
    -h|--help) echo "用法: $0 [--regen] [--apk]";
               echo "  --regen 先从源端重建 Data+清单 (package_native_assets.py)"
               echo "  --apk   打包后顺便重新构建 APK (build_android.sh)"
               exit 0 ;;
    *) echo "未知参数: $a" >&2; exit 2 ;;
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

# —— 2) 打 ZIP（固定结构：根目录 resource-manifest.json + Data/ 文件）——
echo "==> [2/3] 打成 MapleDashResources.zip"
"$PY" tools/build_resource_pack.py --resources "$RESOURCES"

# —— 3) 结构自检（根有清单、无顶层包裹目录、含 Data/ 条目）——
echo "==> [3/3] 校验 ZIP 结构"
"$PY" - "$RESOURCES/MapleDashResources.zip" <<'PY'
import sys, zipfile
zp = sys.argv[1]
with zipfile.ZipFile(zp) as z:
    names = z.namelist()
ok_manifest = "resource-manifest.json" in names
data_cnt = sum(1 for n in names if n.startswith("Data/"))
wrapped = any(n.startswith("MapleDashResources/") for n in names)
print(f"  ZIP 条目总数: {len(names)}")
print(f"  根目录 resource-manifest.json: {'OK' if ok_manifest else '缺失!!'}")
print(f"  Data/ 条目: {data_cnt}")
print(f"  顶层包裹目录(会导致失败): {'有!!' if wrapped else '无'}")
if not ok_manifest or wrapped:
    print("结构错误：手机端第一步解压会失败，请用本脚本打包。")
    sys.exit(1)
print("  结构校验通过")
PY

echo
echo "产物: $RESOURCES/MapleDashResources.zip"
ZS=$(stat -c %s "$RESOURCES/MapleDashResources.zip")
echo "大小: $("PY" -c "print('%.2f GiB' % ($ZS/1024**3))")"
echo
echo "⚠️  ZI P 内的 resource-manifest.json 来自: $MANIFEST"
echo "    若该清单与手机上 APK 内嵌的不一致，手机会报『资源包版本与当前游戏不匹配』。"
echo "    如果你刚改过清单/资源，请一并重新构建 APK（本脚本加 --apk，或另跑 build_android.sh）。"
echo "    传到手机后把旧的 MapleDashResources.zip 删掉再放新的，避免 installer 读到旧包。"

if [[ $APK -eq 1 ]]; then
  echo "==> 重新构建 APK"
  ./build_android.sh
fi
