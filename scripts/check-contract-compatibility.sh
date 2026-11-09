#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
baseline=${1:-contracts/baselines/mf11-provisional.json}
current=${2:-contracts/openapi.json}
# Mount inputs read-only so callers can also compare temporary fixtures.
# v1.37.0, pinned to its image manifest digest.
baseline=$(realpath "$baseline")
current=$(realpath "$current")
docker run --rm -v "$baseline:/baseline.json:ro" -v "$current:/current.json:ro" \
  -v "$PWD/contracts/baselines/mf11-oasdiff-ignore.txt:/ignore.txt:ro" \
  tufin/oasdiff@sha256:ff02af37a254b5199b9d72e7bba93dae5837a9119d59459168d1398893b47aea breaking /baseline.json /current.json --fail-on WARN --err-ignore /ignore.txt
