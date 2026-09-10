#!/bin/bash
set -e
SCRATCH=${PIXELY_WASM_WORK:-$HOME/.pixely-wasm}
. "$SCRATCH/emenv.sh"
cmake --build "$SCRATCH/build-wasm" --target SDL3-static 2>&1 | tail -20
cp "$SCRATCH/build-wasm/libSDL3.a" "$SCRATCH/SDL3.a"
ls -l "$SCRATCH/SDL3.a"
