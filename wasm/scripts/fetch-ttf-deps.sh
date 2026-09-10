#!/bin/bash
# Fetches SDL_ttf's vendored freetype and harfbuzz.
#
# The Emscripten freetype port cannot be used: tools/ports/freetype.py:21-25 returns the
# "-legacysjlj" variant for any SUPPORT_LONGJMP=wasm link and ignores WASM_LEGACY_EXCEPTIONS,
# so a -sWASM_LEGACY_EXCEPTIONS=0 link (which is what .NET uses) gets a freetype built with
# legacy exception instructions and the browser rejects the mixed module. Building both from
# source puts their objects inside SDL3_ttf.a under flags we control.
set -e
S=${PIXELY_WASM_WORK:-$HOME/.pixely-wasm}
cd "$S/SDL_ttf_src"
git submodule update --init --depth 1 external/freetype external/harfbuzz
ls external/freetype | head -5
ls external/harfbuzz | head -5
