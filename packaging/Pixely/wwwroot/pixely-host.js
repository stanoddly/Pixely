// The page side of Pixely.App.BrowserHost: the WebGPU device SDL adopts, and the requestAnimationFrame loop.

// What SDL's WebGPU backend requires of a device (SDL_gpu_webgpu.c in stanoddly/SDL_wgpu), and what it uses when present.
const requiredFeatures = ['depth32float-stencil8', 'rg11b10ufloat-renderable', 'indirect-first-instance', 'depth-clip-control', 'bgra8unorm-storage', 'float32-filterable'];
const optionalFeatures = ['float32-blendable', 'texture-compression-astc', 'texture-compression-astc-sliced-3d', 'texture-compression-bc', 'texture-compression-bc-sliced-3d', 'texture-formats-tier1', 'texture-formats-tier2', 'clip-distances'];

let deviceLoss = null;

// dotnet.js registers the runtime globally once it is created; Module is the Emscripten module the WebGPU binding lives on.
function runtimeModule() {
    const runtime = globalThis.getDotnetRuntime?.(0);
    if (!runtime) {
        throw new Error('The .NET runtime is not running');
    }
    return runtime.Module;
}

// Requests an adapter and a device from the page, imports them into emdawnwebgpu beneath an instance of its own, and returns
// the three pointers for SDL_CreateGPUDeviceWithProperties. The chain matters: emdawnwebgpu completes a future through the
// object's parents, and SDL waits on its fences that way. SDL cannot install callbacks on an adopted device, so the loss and
// error handlers live here. Nothing releases the device: it lives as long as the page, which is where the browser frees it.
export async function createGpuDevice() {
    const Module = runtimeModule();
    const webgpu = Module.WebGPU;
    if (!webgpu || !Module._wgpuCreateInstance) {
        throw new Error('The runtime was linked without the WebGPU binding; publish with PixelyBrowserWebGpu=true (docs/hosting.md)');
    }
    if (!navigator.gpu) {
        throw new Error('WebGPU is not available in this browser');
    }
    const adapter = await navigator.gpu.requestAdapter();
    if (!adapter) {
        throw new Error('No WebGPU adapter is available');
    }
    const missing = requiredFeatures.filter(feature => !adapter.features.has(feature));
    if (missing.length > 0) {
        throw new Error(`The WebGPU adapter lacks features SDL requires: ${missing.join(', ')}`);
    }
    const device = await adapter.requestDevice({
        requiredFeatures: [...requiredFeatures, ...optionalFeatures.filter(feature => adapter.features.has(feature))]
    });

    const instance = Module._wgpuCreateInstance(0);
    const adapterPtr = webgpu.importJsAdapter(adapter, instance);
    const devicePtr = webgpu.importJsDevice(device, adapterPtr);
    device.lost.then(info => {
        deviceLoss = new Error(`The WebGPU device was lost (${info.reason}): ${info.message}`);
        deviceLoss.name = 'GpuDeviceLostError';
        deviceLoss.reason = info.reason;
        console.error(deviceLoss.message);
    });
    device.addEventListener('uncapturederror', event => console.error('WebGPU error', event.error?.message ?? event.error));

    return { instance, adapter: adapterPtr, device: devicePtr };
}

// The loss the current device reported, or null: an Error with the WebGPU reason ("unknown" or "destroyed") and the browser's
// message. BrowserHost.RunAsync turns it into GpuDeviceLostException, and main.js reads it to word the message it shows.
export function readDeviceLoss() {
    return deviceLoss;
}

// Owns the requestAnimationFrame loop for Pixely.App.BrowserHost. The managed callback is marshalled once, here, not per frame,
// and its proxy is disposed when the loop ends so the delegate and the app it targets stop being rooted by a GC handle.
// Everything sits inside the try so the promise rejects instead of the error stopping at the browser console: an exception thrown
// inside a requestAnimationFrame callback never reaches the awaiting managed Main by itself. A lost device ends the loop the
// same way, since SDL keeps recording against a dead device without noticing.
export function runFrameLoop(runFrame) {
    return new Promise((resolve, reject) => {
        const finish = (settle, value) => {
            runFrame.dispose?.();
            settle(value);
        };
        const tick = () => {
            try {
                if (deviceLoss) {
                    finish(reject, deviceLoss);
                } else if (runFrame()) {
                    globalThis.requestAnimationFrame(tick);
                } else {
                    finish(resolve);
                }
            } catch (error) {
                finish(reject, error);
            }
        };
        try {
            globalThis.requestAnimationFrame(tick);
        } catch (error) {
            finish(reject, error);
        }
    });
}

// The runtime files a publish precompresses that are worth decompressing in the page: dotnet.native.wasm, the assemblies and the ICU data.
const brotliTypes = new Set(['dotnetwasm', 'assembly', 'globalization']);
const brotliStreams = supportsBrotliStreams();
// google/brotli's JavaScript decoder (MIT), pinned by its hash: the CDN serves the file of the release tag, and fetch rejects other bytes.
const brotliDecoderUrl = 'https://cdn.jsdelivr.net/gh/google/brotli@v1.2.0/js/decode.min.js';
const brotliDecoderIntegrity = 'sha256-ilNI2/saKHPby5pNU/Si2ZY4wROIi2WwPLuO2Gx/fCc=';
let brotliDecoder = null;
// The runtime's fetch policy while the publish turns the loader on, otherwise null.
let brotliFetchPolicy = null;

// An onConfigLoaded callback for dotnet.withModuleConfig, which runs before the runtime downloads any file. The SDK puts
// PixelyBrowserBrotli in a publish's boot config as extensions.pixely.brotli; a build has no .br files and no extension. The loader
// follows the config's integrity and cache policy as the runtime's own fetch does.
export function configureCompressedResources(config) {
    brotliFetchPolicy = config.extensions?.pixely?.brotli === true
        ? { integrity: !config.disableIntegrityCheck, cache: config.disableNoCacheFetch ? undefined : 'no-cache' }
        : null;
}

// A loader for dotnet.withResourceLoader that fetches the Brotli copy a publish writes beside each runtime file and decompresses it
// in the page. A static host such as GitHub Pages serves foo.wasm.br as an opaque file and compresses foo.wasm with gzip at best,
// since it sets no Content-Encoding: br. DecompressionStream keeps compilation streaming where it decodes Brotli; elsewhere, Chrome
// among them, google/brotli's decoder from a CDN decodes the whole file once it has arrived. The runtime checks the integrity only
// of files it fetches itself, so the decompressed ones go without; their names carry a hash of their content.
export function loadCompressedResource(type, name, defaultUri, integrity) {
    if (!brotliFetchPolicy || !brotliTypes.has(type)) {
        return undefined;
    }
    // A path the page cannot resolve, as without a document or location, is left to the runtime.
    let brotliUrl;
    try {
        brotliUrl = new URL(defaultUri, globalThis.document?.baseURI ?? globalThis.location?.href);
    } catch {
        return undefined;
    }
    // CORS hides Content-Encoding from the page unless the other origin exposes it, so a server that negotiates Brotli would have its
    // files decompressed twice; the runtime fetches a file from another origin itself. Node has no location and no CORS.
    if (globalThis.location && brotliUrl.origin !== globalThis.location.origin) {
        return undefined;
    }
    brotliUrl.pathname += '.br';
    if (!brotliStreams) {
        // The decoder downloads while the first file does.
        loadBrotliDecoder().catch(() => {});
    }
    const original = { cache: brotliFetchPolicy.cache, integrity: brotliFetchPolicy.integrity ? integrity : undefined };
    return fetchBrotli(type, brotliUrl, defaultUri, original);
}

// Whatever keeps the .br copy from being used, the original file is fetched as the runtime would: a missing copy, as in a build
// that was not published, a failed request, or a decoder that did not load, such as one a Content-Security-Policy blocks.
async function fetchBrotli(type, brotliUrl, defaultUri, original) {
    try {
        const decompressed = await fetchDecompressed(type, brotliUrl, defaultUri, original);
        if (decompressed) {
            return decompressed;
        }
    } catch (error) {
        console.warn(`Pixely fetches ${defaultUri} without Brotli`, error);
    }
    return fetch(defaultUri, original);
}

async function fetchDecompressed(type, brotliUrl, defaultUri, original) {
    const compressed = await fetch(brotliUrl, { cache: original.cache });
    // A static host answers a missing file with 404; a development server that falls back to index.html answers with the page.
    if (!compressed.ok || compressed.headers.get('Content-Type')?.toLowerCase().startsWith('text/html')) {
        await compressed.body?.cancel();
        return null;
    }
    const headers = { 'Content-Type': type === 'dotnetwasm' ? 'application/wasm' : 'application/octet-stream' };
    // A server that labels the file Content-Encoding: br has had the browser decompress it already. Codings are case-insensitive
    // tokens in a list.
    if (/(^|,)\s*br\s*(,|$)/i.test(compressed.headers.get('Content-Encoding') ?? '')) {
        return new Response(resumeFromOriginal(compressed.body, defaultUri, original), { headers });
    }
    if (brotliStreams) {
        return new Response(resumeFromOriginal(compressed.body.pipeThrough(new DecompressionStream('brotli')), defaultUri, original), { headers });
    }
    let decoder;
    try {
        decoder = await loadBrotliDecoder();
    } catch (error) {
        await compressed.body?.cancel();
        throw error;
    }
    return new Response(decoder.BrotliDecode(new Int8Array(await compressed.arrayBuffer())), { headers });
}

// The runtime reads a streamed file after the loader has returned it, so a copy that fails partway, truncated or corrupt, cannot fall
// back as a whole. The stream continues from the original file instead, skipping the bytes it has delivered, since the original holds
// the decompressed bytes.
function resumeFromOriginal(stream, defaultUri, original) {
    let reader = stream.getReader();
    let resumed = false;
    let delivered = 0;
    let skip = 0;
    return new ReadableStream({
        async pull(controller) {
            for (;;) {
                let chunk;
                try {
                    chunk = await reader.read();
                } catch (error) {
                    if (resumed) {
                        throw error;
                    }
                    resumed = true;
                    console.warn(`Pixely continues ${defaultUri} from the original after ${delivered} bytes`, error);
                    const response = await fetch(defaultUri, original);
                    if (!response.ok) {
                        throw new Error(`${defaultUri} answered ${response.status}`);
                    }
                    reader = response.body.getReader();
                    skip = delivered;
                    continue;
                }
                if (chunk.done) {
                    controller.close();
                    return;
                }
                const skipped = Math.min(skip, chunk.value.byteLength);
                skip -= skipped;
                if (skipped < chunk.value.byteLength) {
                    const bytes = chunk.value.subarray(skipped);
                    delivered += bytes.byteLength;
                    controller.enqueue(bytes);
                    return;
                }
            }
        },
        cancel(reason) {
            return reader.cancel(reason);
        },
    });
}

// One import for every file; a failed one is forgotten, so the next file, or the runtime's retry of this one, imports it again.
function loadBrotliDecoder() {
    brotliDecoder ??= importBrotliDecoder().catch(error => {
        brotliDecoder = null;
        throw error;
    });
    return brotliDecoder;
}

// import() checks no integrity, so the verified source is imported from a data URL, which node imports too.
async function importBrotliDecoder() {
    const response = await fetch(brotliDecoderUrl, { integrity: brotliDecoderIntegrity });
    if (!response.ok) {
        throw new Error(`${brotliDecoderUrl} answered ${response.status}`);
    }
    return import(`data:text/javascript,${encodeURIComponent(await response.text())}`);
}

function supportsBrotliStreams() {
    try {
        new DecompressionStream('brotli');
        return true;
    } catch {
        return false;
    }
}
