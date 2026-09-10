#!/bin/bash
set -e
S=${PIXELY_WASM_WORK:-$HOME/.pixely-wasm}
. "$S/emenv.sh"
cmake --build "$S/build-ttf" 2>&1 | tail -30
echo "=== artifacts ==="
ls -l "$S/build-ttf"/*.a
