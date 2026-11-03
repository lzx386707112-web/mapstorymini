#!/usr/bin/env bash
set -euo pipefail
PROJ="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec bash "$PROJ/tools/pack_resources.sh" "$@"
