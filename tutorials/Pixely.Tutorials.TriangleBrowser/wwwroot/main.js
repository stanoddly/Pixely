import { dotnet } from './_framework/dotnet.js'

// SDL's Emscripten backend reads Module.canvas. The .NET host owns the Module, so the canvas has
// to be handed over before the runtime starts.
const canvas = document.getElementById('canvas');

const runtime = await dotnet
    .withModuleConfig({ canvas: canvas })
    .create();

// The boot shim has to be entered from JavaScript. Under JSPI only frames between the promising
// export and the suspending import can be parked, and SDL_CreateGPUDevice parks inside SDL. Mono
// frames cannot sit in that region, so create() has already returned here and nothing of Mono's is
// below this call.
const bootState = await runtime.Module.wasmExports.pixely_gpu_boot();
console.log('boot: pixely_gpu_boot returned', bootState);

const exports = await runtime.getAssemblyExports('Pixely.Tutorials.TriangleBrowser.dll');
const program = exports.Pixely.Tutorials.TriangleBrowser.Program;

const startError = program.Start();
if (startError !== '') {
    console.error('Start failed:', startError);
    throw new Error(startError);
}

console.log('gpu driver as Pixely sees it:', program.GpuDriver());

// ?frames=N stops after N frames, so a run can be cut to a single frame without republishing.
const frameLimit = Number(new URLSearchParams(location.search).get('frames') ?? 0);
let frameCount = 0;

function frame() {
    const error = program.Frame();
    if (error !== '') {
        console.error('Frame failed:', error);
        return;
    }

    frameCount += 1;
    if (frameCount === 1 || frameCount === 60 || frameCount === 300) {
        console.log('frames rendered:', frameCount);
    }

    if (frameLimit > 0 && frameCount >= frameLimit) {
        console.log('frame limit reached, stopping at', frameCount);
        return;
    }

    requestAnimationFrame(frame);
}

requestAnimationFrame(frame);
