#!/bin/bash
set -e
S=${PIXELY_WASM_WORK:-$HOME/.pixely-wasm}
. "$S/emenv.sh"
cmake --build "$S/build-image" 2>&1 | tail -25
ls -l "$S/build-image"/*.a
