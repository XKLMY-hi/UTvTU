#!/usr/bin/env bash
set -euo pipefail

project_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet_cmd="${DOTNET_CMD:-dotnet}"

if [[ "$(uname -s)" != "Linux" ]]; then
  echo "Run this script on Linux." >&2
  exit 1
fi
if [[ "$("$dotnet_cmd" --version)" != 9.* ]]; then
  echo "This project requires .NET SDK 9." >&2
  exit 1
fi

cd -- "$project_root"
"$dotnet_cmd" restore OpenUtau/OpenUtau.csproj -r linux-x64 -m:1 -p:SelfContained=true
"$dotnet_cmd" publish OpenUtau/OpenUtau.csproj --no-restore -c Release \
  -r linux-x64 --self-contained true -m:1 -p:PublishReadyToRun=false \
  -o "$project_root/bin/linux-x64"

echo "Built: $project_root/bin/linux-x64/OpenUtau"
