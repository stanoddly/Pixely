// The page side of Pixely.App.BrowserHost: the WebGPU device SDL adopts, and the requestAnimationFrame loop.

// What SDL's WebGPU backend requires of a device (SDL_gpu_webgpu.c in stanoddly/SDL_wgpu), and what it uses when present.
const requiredFeatures = ['depth32float-stencil8', 'rg11b10ufloat-renderable', 'indirect-first-instance', 'depth-clip-control', 'bgra8unorm-storage', 'float32-filterable'];
const optionalFeatures = ['float32-blendable', 'texture-compression-astc', 'texture-compression-astc-sliced-3d', 'texture-compression-bc', 'texture-compression-bc-sliced-3d', 'texture-formats-tier1', 'texture-formats-tier2', 'clip-distances'];

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
    // A second preparation, from a Main that runs twice, must not orphan the first device.
    releaseGpuDevice();
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

    const handles = { adapter, device, instance: 0, adapterPtr: 0, devicePtr: 0, destroying: false };
    try {
        handles.instance = Module._wgpuCreateInstance(0);
        handles.adapterPtr = webgpu.importJsAdapter(adapter, handles.instance);
        handles.devicePtr = webgpu.importJsDevice(device, handles.adapterPtr);
    } catch (error) {
        // A failed import leaves the device requested and any earlier handle created; release them as a teardown would.
        gpu = handles;
        releaseGpuDevice();
        throw error;
    }
    deviceLoss = null;
    device.lost.then(info => {
        // The loss destroyGpuDevice causes itself is the expected end of the device, not an error.
        if (handles.destroying) {
            return;
        }
        deviceLoss = new Error(`The WebGPU device was lost (${info.reason}): ${info.message}`);
        deviceLoss.name = 'GpuDeviceLostError';
        deviceLoss.reason = info.reason;
        console.error(deviceLoss.message);
    });
    device.addEventListener('uncapturederror', event => console.error('WebGPU error', event.error?.message ?? event.error));

    gpu = handles;
    return { instance: handles.instance, adapter: handles.adapterPtr, device: handles.devicePtr };
}

// The loss the current device reported, or null: an Error with the WebGPU reason ("unknown" or "destroyed") and the browser's
// message. BrowserHost.RunAsync turns it into GpuDeviceLostException, and main.js reads it to word the message it shows.
export function readDeviceLoss() {
    return deviceLoss;
}

// Awaited by BrowserHost.RunAsync once the frame loop has ended, so that SDL_DestroyGPUDevice, which spins until every
// submission has completed, finds them complete. Never rejects: a lost device has nothing left to wait for.
export async function waitForGpuIdle() {
    try {
        await gpu?.device.queue.onSubmittedWorkDone();
    } catch (error) {
        console.error('The WebGPU queue did not report idle', error);
    }
}

// Drops the page's references to the imported objects and destroys the WebGPU device, after SDL dropped its own.
export function releaseGpuDevice() {
    const current = gpu;
    gpu = null;
    if (!current) {
        return;
    }
    current.destroying = true;
    try {
        const Module = runtimeModule();
        // A handle a failed import never created is 0; the release calls do not check for null.
        if (current.devicePtr) {
            Module._wgpuDeviceRelease(current.devicePtr);
        }
        if (current.adapterPtr) {
            Module._wgpuAdapterRelease(current.adapterPtr);
        }
        if (current.instance) {
            Module._wgpuInstanceRelease(current.instance);
        }
    } catch (error) {
        console.error('Pixely could not release the WebGPU handles', error);
    }
    current.device.destroy();
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
