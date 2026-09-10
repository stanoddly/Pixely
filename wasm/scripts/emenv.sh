# Source this to get the .NET 11 workload's Emscripten 6.0.2 on PATH.
# The .NET packs ship a frozen, read-only cache; ports need a writable one, so the
# pack cache is copied to a scratchpad location on first use.

EM_PACK=/home/stanoddly/.dotnet/packs/Microsoft.NET.Runtime.Emscripten.6.0.2.Sdk.linux-x64/11.0.0-rc.1.26425.128/tools
EM_CACHE_PACK=/home/stanoddly/.dotnet/packs/Microsoft.NET.Runtime.Emscripten.6.0.2.Cache.linux-x64/11.0.0-rc.1.26425.128/tools/emscripten/cache
EM_NODE_PACK=/home/stanoddly/.dotnet/packs/Microsoft.NET.Runtime.Emscripten.6.0.2.Node.linux-x64/11.0.0-rc.1.26425.128/tools/bin
SCRATCH=${PIXELY_WASM_WORK:-$HOME/.pixely-wasm}

export EMSDK_PATH="$EM_PACK"
export DOTNET_EMSCRIPTEN_LLVM_ROOT="$EM_PACK/bin"
export DOTNET_EMSCRIPTEN_BINARYEN_ROOT="$EM_PACK"
export DOTNET_EMSCRIPTEN_NODE_JS="$EM_NODE_PACK/node"
export EM_CONFIG="$EM_PACK/emscripten/.emscripten"

# bool('') is False in Python; any non-empty value, "0" included, would freeze the cache
export FROZEN_CACHE=""

export EM_CACHE="$SCRATCH/emcache"
if [ ! -d "$EM_CACHE" ]; then
  mkdir -p "$EM_CACHE"
  cp -r "$EM_CACHE_PACK/." "$EM_CACHE/"
fi

export PATH="$EM_PACK/emscripten:$EM_PACK/bin:$EM_NODE_PACK:$PATH"
