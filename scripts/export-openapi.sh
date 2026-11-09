#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
mode=${1:-}
if [[ -n "$mode" && "$mode" != "--check" ]]; then echo 'Usage: scripts/export-openapi.sh [--check]' >&2; exit 2; fi
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
rm -f src/MuscleForge.Api/obj/MuscleForge.Api.OpenApiFiles.cache
# A clean environment prevents ambient owner, database, and key settings from reaching the export host.
env -i XDG_RUNTIME_DIR="$tmp" PATH="$PATH" HOME="$HOME" DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-$HOME}" \
  ASPNETCORE_ENVIRONMENT=Development DOTNET_ENVIRONMENT=Development \
  Owner__Email= Owner__Password= DataProtection__KeysPath= DataProtection__CertificatePath= DataProtection__CertificatePassword= \
  ConnectionStrings__Database='Host=127.0.0.1;Port=1;Database=export;Username=export' \
  dotnet build src/MuscleForge.Api/MuscleForge.Api.csproj --configuration Release --no-restore -m:1 \
  -p:OpenApiGenerateDocumentsOnBuild=true -p:OpenApiGenerateDocumentsOptions="--openapi-version OpenApi3_0" -p:OpenApiDocumentsDirectory="$tmp"
file="$tmp/MuscleForge.Api.json"
test -f "$file"
if [[ "$mode" == --check ]]; then
  cmp -s "$file" contracts/openapi.json || { echo 'OpenAPI drift: run scripts/export-openapi.sh and review the diff' >&2; exit 1; }
  echo 'OpenAPI matches the committed contract'
else
  cp "$file" contracts/openapi.json
  echo 'Exported contracts/openapi.json'
fi
