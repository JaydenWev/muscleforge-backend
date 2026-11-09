#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
before=tests/contract_tooling/fixtures/breaking-before.json
after=tests/contract_tooling/fixtures/breaking-after.json
scripts/check-contract-compatibility.sh "$before" "$before"
if scripts/check-contract-compatibility.sh "$before" "$after"; then
  echo 'Expected removal of an endpoint to fail compatibility' >&2
  exit 1
fi
echo 'Removed endpoint rejected as expected'
