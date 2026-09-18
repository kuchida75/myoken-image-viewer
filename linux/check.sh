#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
baseline=ea785b3770383278f1a3bf67762d35014e8bda26
git cat-file -e "$baseline^{commit}"
git diff --exit-code "$baseline" -- ../src/ZonerInspiredViewer ../version.json ../BUILD_HISTORY.json ../BUILD_HISTORY.md ../CHANGELOG.md ../tools ../docs/BUILDING.md
if ! command -v dotnet >/dev/null 2>&1; then
  printf 'The .NET 10 SDK is required. See linux/README.md.\n' >&2
  exit 1
fi
dotnet build ../src/Myoken.Linux/Myoken.Linux.csproj --configuration Release
dotnet run --project ../tests/Myoken.Linux.Tests/Myoken.Linux.Tests.csproj --configuration Release
