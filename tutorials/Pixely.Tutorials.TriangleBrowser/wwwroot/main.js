import { dotnet } from './_framework/dotnet.js'

// SDL's Emscripten backend reads Module.canvas. The .NET host owns the Module, so the canvas has
// to be handed over before the runtime starts.
const canvas = document.getElementById('canvas');

const runtime = await dotnet
    .withModuleConfig({ canvas: canvas })
    .create();

const exports = await runtime.getAssemblyExports('Pixely.Tutorials.TriangleBrowser.dll');
const program = exports.Pixely.Tutorials.TriangleBrowser.Program;

// SDL_Init, SDL_CreateWindow and SDL_ClaimWindowForGPUDevice never block, so they stay in managed
// code. Only SDL_CreateGPUDevice suspends, waiting on the WebGPU adapter and device futures.
const bootError = program.BeginBoot();
if (bootError !== '') {
    console.error('BeginBoot failed:', bootError);
    throw new Error(bootError);
}

// Called from here rather than from managed code because under JSPI a suspension unwinds to the
// nearest promising frame, and only the exports named in JSPI_EXPORTS are promising. Entered
// straight from JavaScript, SDL_CreateGPUDevice is itself that frame, with no Mono frame beneath
// it. The arguments are SDL_GPU_SHADERFORMAT_WGSL, debug mode, and a NULL driver name.
const device = await runtime.Module.wasmExports.SDL_CreateGPUDevice(64, 1, 0);
console.log('boot: SDL_CreateGPUDevice returned', device);

const startError = program.Start(device);
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
