#!/bin/bash
# Builds SDL3_image for wasm against the SDL3 fork in build-wasm.
# Every format needing an external library is off; what stays is decoded by the
# vendored stb_image and SDL_image's own loaders, so the archive is self-contained.
set -e
S=${PIXELY_WASM_WORK:-$HOME/.pixely-wasm}
. "$S/emenv.sh"

emcmake cmake -S "$S/SDL_image_src" -B "$S/build-image" -G Ninja \
  -DCMAKE_BUILD_TYPE=Release \
  -DBUILD_SHARED_LIBS=OFF \
  -DSDL3_DIR="$S/build-wasm" \
  -DSDLIMAGE_VENDORED=OFF \
  -DSDLIMAGE_DEPS_SHARED=OFF \
  -DSDLIMAGE_SAMPLES=OFF \
  -DSDLIMAGE_TESTS=OFF \
  -DSDLIMAGE_INSTALL=OFF \
  -DSDLIMAGE_BACKEND_STB=ON \
  -DSDLIMAGE_AVIF=OFF \
  -DSDLIMAGE_JXL=OFF \
  -DSDLIMAGE_TIF=OFF \
  -DSDLIMAGE_WEBP=OFF \
  -DSDLIMAGE_PNG_LIBPNG=OFF \
  -DCMAKE_C_FLAGS="-fwasm-exceptions"
