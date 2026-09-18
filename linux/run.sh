#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
if ! command -v dotnet >/dev/null 2>&1; then
  printf 'The .NET 10 SDK is required. See linux/README.md.\n' >&2
  exit 1
fi
exec dotnet run --project ../src/Myoken.Linux/Myoken.Linux.csproj --configuration Release -- "$@"
