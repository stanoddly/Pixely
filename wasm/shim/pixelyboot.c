// Boot shim for the SDL3 WebGPU backend on browser-wasm.
//
// SDL_CreateGPUDevice busy-waits on a WebGPU future and calls SDL_DelayNS, which on
// Emscripten becomes emscripten_sleep. That has to suspend the wasm stack. Both stack
// switching mechanisms (Asyncify and JSPI) can only suspend frames that sit between the
// suspending import and a designated boundary, and the Mono interpreter frames a managed
// P/Invoke puts on the stack cannot cross either boundary.
//
// So the blocking part runs here instead, exported to JavaScript and declared suspending
// via -sJSPI_EXPORTS=pixely_gpu_boot. JavaScript calls it once, after the .NET runtime is
// up but with no managed frame on the stack, and awaits the promise it returns. The
// results land in these globals and the managed side picks them up through the plain,
// non-blocking getters below.

#include <SDL3/SDL.h>
#include <emscripten.h>

static SDL_Window *g_window;
static SDL_GPUDevice *g_device;

// 0 = not started, 1 = running, 2 = succeeded, -1 = failed
static int g_state;
static char g_error[512];

static int pixely_fail(const char *stage)
{
    SDL_snprintf(g_error, sizeof(g_error), "%s: %s", stage, SDL_GetError());
    g_state = -1;
    return -1;
}

EMSCRIPTEN_KEEPALIVE int pixely_gpu_boot(void)
{
    if (g_state != 0)
    {
        return g_state;
    }
    g_state = 1;

    if (!SDL_Init(SDL_INIT_VIDEO | SDL_INIT_EVENTS))
    {
        return pixely_fail("SDL_Init");
    }

    // Pixely builds a GamepadService as part of its event service. Failure is not fatal: the
    // browser may expose no gamepad support at all.
    SDL_InitSubSystem(SDL_INIT_JOYSTICK | SDL_INIT_GAMEPAD);

    g_window = SDL_CreateWindow("pixely wasm probe", 640, 480, 0);
    if (g_window == NULL)
    {
        return pixely_fail("SDL_CreateWindow");
    }

    // The suspend happens inside here.
    g_device = SDL_CreateGPUDevice(SDL_GPU_SHADERFORMAT_WGSL, true, NULL);
    if (g_device == NULL)
    {
        return pixely_fail("SDL_CreateGPUDevice");
    }

    if (!SDL_ClaimWindowForGPUDevice(g_device, g_window))
    {
        return pixely_fail("SDL_ClaimWindowForGPUDevice");
    }

    g_state = 2;
    return 2;
}

EMSCRIPTEN_KEEPALIVE int pixely_gpu_state(void)
{
    return g_state;
}

EMSCRIPTEN_KEEPALIVE void *pixely_gpu_device(void)
{
    return g_device;
}

EMSCRIPTEN_KEEPALIVE void *pixely_gpu_window(void)
{
    return g_window;
}

EMSCRIPTEN_KEEPALIVE const char *pixely_gpu_error(void)
{
    return g_error;
}

EMSCRIPTEN_KEEPALIVE const char *pixely_gpu_driver(void)
{
    return g_device != NULL ? SDL_GetGPUDeviceDriver(g_device) : NULL;
}
