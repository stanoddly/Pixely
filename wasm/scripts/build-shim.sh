#!/bin/bash
# Compiles the boot shim to an object file whose base name doubles as the P/Invoke module
# name, so DllImport("pixelyboot") resolves against it.
set -e
SCRATCH=${PIXELY_WASM_WORK:-$HOME/.pixely-wasm}
. "$SCRATCH/emenv.sh"

emcc -c -O2 -fwasm-exceptions \
  -I "$SCRATCH/SDL_wgpu/include" \
  "$SCRATCH/shim/pixelyboot.c" \
  -o "$SCRATCH/shim/pixelyboot.o"

echo "built:"
ls -l "$SCRATCH/shim/pixelyboot.o"
"$EMSDK_PATH/bin/llvm-nm" "$SCRATCH/shim/pixelyboot.o" | grep pixely
