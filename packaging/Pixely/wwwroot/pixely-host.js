// The page side of Pixely.App.BrowserHost: the WebGPU device SDL adopts, and the requestAnimationFrame loop.

// What SDL's WebGPU backend requires of a device (SDL_gpu_webgpu.c in stanoddly/SDL_wgpu), and what it uses when present.
const requiredFeatures = ['depth32float-stencil8', 'rg11b10ufloat-renderable', 'indirect-first-instance', 'depth-clip-control', 'bgra8unorm-storage', 'float32-filterable'];
const optionalFeatures = ['texture-compression-bc', 'texture-compression-bc-sliced-3d', 'texture-compression-etc2', 'texture-compression-astc', 'texture-compression-astc-sliced-3d', 'clip-distances'];

let gpu = null;
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
// error handlers live here.
export async function createGpuDevice() {
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

    const Module = runtimeModule();
    const webgpu = Module.WebGPU;
    if (!webgpu || !Module._wgpuCreateInstance) {
        throw new Error('The runtime was linked without the WebGPU binding; publish with PixelyBrowserWebGpu=true (docs/hosting.md)');
    }
    const instance = Module._wgpuCreateInstance(0);
    const adapterPtr = webgpu.importJsAdapter(adapter, instance);
    const devicePtr = webgpu.importJsDevice(device, adapterPtr);

    const handles = { adapter, device, instance, adapterPtr, devicePtr, destroying: false };
    deviceLoss = null;
    device.lost.then(info => {
        // The loss destroyGpuDevice causes itself is the expected end of the device, not an error.
        if (handles.destroying) {
            return;
        }
        deviceLoss = new Error(`The WebGPU device was lost (${info.reason}): ${info.message}`);
        console.error(deviceLoss.message);
    });
    device.addEventListener('uncapturederror', event => console.error('WebGPU error', event.error?.message ?? event.error));

    gpu = handles;
    return { instance, adapter: adapterPtr, device: devicePtr };
}

// Destroying SDL's device spins until its submissions have drained, and the fences that drain them complete only after this
// event loop turns, so the queue is awaited first. destroy is the managed SDL_DestroyGPUDevice call. Never rejects: nothing
// awaits it, the app is already disposed.
export async function destroyGpuDevice(destroy) {
    const current = gpu;
    gpu = null;
    try {
        await current?.device.queue.onSubmittedWorkDone();
        destroy();
        if (current) {
            const Module = runtimeModule();
            Module._wgpuDeviceRelease(current.devicePtr);
            Module._wgpuAdapterRelease(current.adapterPtr);
            Module._wgpuInstanceRelease(current.instance);
            current.destroying = true;
            current.device.destroy();
        }
    } catch (error) {
        console.error('Pixely could not destroy the GPU device', error);
    } finally {
        destroy.dispose?.();
    }
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
