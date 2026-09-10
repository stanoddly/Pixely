#!/bin/bash
# Builds SDL3_ttf for wasm against the SDL3 fork in build-wasm.
#
# freetype and harfbuzz are vendored rather than taken from the Emscripten ports, because
# tools/ports/freetype.py:21-25 hands any SUPPORT_LONGJMP=wasm link the "-legacysjlj" variant
# whatever WASM_LEGACY_EXCEPTIONS says. The .NET link is "-fwasm-exceptions -sWASM_LEGACY_EXCEPTIONS=0",
# so that variant's legacy instructions make the browser reject the whole module with
# "module uses a mix of legacy and new exception handling instructions".
#
# Vendoring also means SDL3_ttf.a is self-contained: for a static build SDL_ttf's CMake puts the
# freetype and harfbuzz objects into the archive itself, so the consuming link needs no --use-port.
set -e
S=${PIXELY_WASM_WORK:-$HOME/.pixely-wasm}
. "$S/emenv.sh"

EHFLAGS="-fwasm-exceptions -sWASM_LEGACY_EXCEPTIONS=0"
# harfbuzz promotes its own warnings to errors through #pragma GCC diagnostic, which a newer clang
# than it expects turns into a failing build. Emscripten's own harfbuzz port disables the same two.
HBFLAGS="-DHB_NO_PRAGMA_GCC_DIAGNOSTIC_ERROR -DHB_NO_PRAGMA_GCC_DIAGNOSTIC_WARNING"
rm -rf "$S/build-ttf"

emcmake cmake -S "$S/SDL_ttf_src" -B "$S/build-ttf" -G Ninja \
  -DCMAKE_BUILD_TYPE=Release \
  -DBUILD_SHARED_LIBS=OFF \
  -DSDL3_DIR="$S/build-wasm" \
  -DSDLTTF_VENDORED=ON \
  -DSDLTTF_HARFBUZZ=ON \
  -DSDLTTF_PLUTOSVG=OFF \
  -DSDLTTF_SAMPLES=OFF \
  -DSDLTTF_INSTALL=OFF \
  -DCMAKE_C_FLAGS="$EHFLAGS" \
  -DCMAKE_CXX_FLAGS="$EHFLAGS $HBFLAGS" \
  > "$S/ttf-configure.log" 2>&1

grep -E 'backends|enabled|disabled|Using' "$S/ttf-configure.log" | head -20
cmake --build "$S/build-ttf" 2>&1 | tail -5
cp "$S/build-ttf/libSDL3_ttf.a" "$S/SDL3_ttf.a"
ls -l "$S/SDL3_ttf.a"
