#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
IMAGE_NAME="displayprofileswitcher-build:local"

if ! command -v docker >/dev/null 2>&1; then
    printf '%s\n' 'Docker is required but was not found in PATH.' >&2
    exit 1
fi

printf '%s\n' '=== Display Profile Switcher: Docker build ==='
docker build \
    --file "$ROOT_DIR/Dockerfile" \
    --tag "$IMAGE_NAME" \
    "$ROOT_DIR"

docker run --rm "$IMAGE_NAME"
printf '%s\n' 'Published executable verified: /out/DisplayProfileSwitcher.exe'
