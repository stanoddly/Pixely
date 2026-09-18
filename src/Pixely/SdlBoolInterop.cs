using System.Runtime.InteropServices;
using SDL;

namespace Pixely;

// The SDL functions Pixely calls with a boolean argument, declared with byte instead of the bindings' SDLBool. SDLBool is a
// one-byte struct, and the Mono wasm interpreter passes such a struct to native code as a four-byte read of the slot that
// holds its single byte, so the C bool arrives with stale upper bytes: undefined behavior, observed as the argument acting
// as false. A byte is stored as a whole int and arrives clean. Both map to the same C declaration in the generated
// P/Invoke table, so these may coexist with the bindings' own declarations.
internal static unsafe class SdlBoolInterop
{
    internal static void SDL_SetGamepadEventsEnabled(bool enabled)
    {
        SetGamepadEventsEnabled(Convert.ToByte(enabled));
    }

    internal static IntPtr SDL_MapGPUTransferBuffer(SDL_GPUDevice* device, SDL_GPUTransferBuffer* transferBuffer, bool cycle)
    {
        return MapGPUTransferBuffer(device, transferBuffer, Convert.ToByte(cycle));
    }

    internal static void SDL_UploadToGPUTexture(SDL_GPUCopyPass* copyPass, SDL_GPUTextureTransferInfo* source, SDL_GPUTextureRegion* destination, bool cycle)
    {
        UploadToGPUTexture(copyPass, source, destination, Convert.ToByte(cycle));
    }

    internal static void SDL_UploadToGPUBuffer(SDL_GPUCopyPass* copyPass, SDL_GPUTransferBufferLocation* source, SDL_GPUBufferRegion* destination, bool cycle)
    {
        UploadToGPUBuffer(copyPass, source, destination, Convert.ToByte(cycle));
    }

    internal static SDLBool SDL_WaitForGPUFences(SDL_GPUDevice* device, bool waitAll, SDL_GPUFence** fences, uint numFences)
    {
        return WaitForGPUFences(device, Convert.ToByte(waitAll), fences, numFences);
    }

    internal static SDL_Keycode SDL_GetKeyFromScancode(SDL_Scancode scancode, SDL_Keymod modstate, bool keyEvent)
    {
        return GetKeyFromScancode(scancode, modstate, Convert.ToByte(keyEvent));
    }

    internal static SDLBool SDL_SetWindowRelativeMouseMode(SDL_Window* window, bool enabled)
    {
        return SetWindowRelativeMouseMode(window, Convert.ToByte(enabled));
    }

    internal static SDLBool SDL_SetWindowMouseGrab(SDL_Window* window, bool grabbed)
    {
        return SetWindowMouseGrab(window, Convert.ToByte(grabbed));
    }

    internal static SDLBool SDL_SetWindowAlwaysOnTop(SDL_Window* window, bool onTop)
    {
        return SetWindowAlwaysOnTop(window, Convert.ToByte(onTop));
    }

    internal static SDLBool SDL_SetWindowFullscreen(SDL_Window* window, bool fullscreen)
    {
        return SetWindowFullscreen(window, Convert.ToByte(fullscreen));
    }

    // The name is a null-terminated UTF-8 literal, as the bindings' SDL_PROP_* constants are.
    internal static SDLBool SDL_SetBooleanProperty(SDL_PropertiesID props, ReadOnlySpan<byte> name, bool value)
    {
        fixed (byte* namePointer = name)
        {
            return SetBooleanProperty(props, namePointer, Convert.ToByte(value));
        }
    }

    internal static void SDL_ShowOpenFileDialog(delegate* unmanaged[Cdecl]<IntPtr, byte**, int, void> callback, IntPtr userdata, SDL_Window* window, SDL_DialogFileFilter* filters, int filterCount, byte* defaultLocation, bool allowMany)
    {
        ShowOpenFileDialog(callback, userdata, window, filters, filterCount, defaultLocation, Convert.ToByte(allowMany));
    }

    internal static SDL_Surface* IMG_Load_IO(SDL_IOStream* source, bool closeStream)
    {
        return LoadImage(source, Convert.ToByte(closeStream));
    }

    internal static TTF_Font* TTF_OpenFontIO(SDL_IOStream* source, bool closeStream, float pointSize)
    {
        return OpenFont(source, Convert.ToByte(closeStream), pointSize);
    }

    [DllImport("SDL3", EntryPoint = "SDL_SetGamepadEventsEnabled")]
    private static extern void SetGamepadEventsEnabled(byte enabled);

    [DllImport("SDL3", EntryPoint = "SDL_MapGPUTransferBuffer")]
    private static extern IntPtr MapGPUTransferBuffer(SDL_GPUDevice* device, SDL_GPUTransferBuffer* transferBuffer, byte cycle);

    [DllImport("SDL3", EntryPoint = "SDL_UploadToGPUTexture")]
    private static extern void UploadToGPUTexture(SDL_GPUCopyPass* copyPass, SDL_GPUTextureTransferInfo* source, SDL_GPUTextureRegion* destination, byte cycle);

    [DllImport("SDL3", EntryPoint = "SDL_UploadToGPUBuffer")]
    private static extern void UploadToGPUBuffer(SDL_GPUCopyPass* copyPass, SDL_GPUTransferBufferLocation* source, SDL_GPUBufferRegion* destination, byte cycle);

    [DllImport("SDL3", EntryPoint = "SDL_WaitForGPUFences")]
    private static extern SDLBool WaitForGPUFences(SDL_GPUDevice* device, byte waitAll, SDL_GPUFence** fences, uint numFences);

    [DllImport("SDL3", EntryPoint = "SDL_GetKeyFromScancode")]
    private static extern SDL_Keycode GetKeyFromScancode(SDL_Scancode scancode, SDL_Keymod modstate, byte keyEvent);

    [DllImport("SDL3", EntryPoint = "SDL_SetWindowRelativeMouseMode")]
    private static extern SDLBool SetWindowRelativeMouseMode(SDL_Window* window, byte enabled);

    [DllImport("SDL3", EntryPoint = "SDL_SetWindowMouseGrab")]
    private static extern SDLBool SetWindowMouseGrab(SDL_Window* window, byte grabbed);

    [DllImport("SDL3", EntryPoint = "SDL_SetWindowAlwaysOnTop")]
    private static extern SDLBool SetWindowAlwaysOnTop(SDL_Window* window, byte onTop);

    [DllImport("SDL3", EntryPoint = "SDL_SetWindowFullscreen")]
    private static extern SDLBool SetWindowFullscreen(SDL_Window* window, byte fullscreen);

    [DllImport("SDL3", EntryPoint = "SDL_SetBooleanProperty")]
    private static extern SDLBool SetBooleanProperty(SDL_PropertiesID props, byte* name, byte value);

    [DllImport("SDL3", EntryPoint = "SDL_ShowOpenFileDialog")]
    private static extern void ShowOpenFileDialog(delegate* unmanaged[Cdecl]<IntPtr, byte**, int, void> callback, IntPtr userdata, SDL_Window* window, SDL_DialogFileFilter* filters, int filterCount, byte* defaultLocation, byte allowMany);

    [DllImport("SDL3_image", EntryPoint = "IMG_Load_IO")]
    private static extern SDL_Surface* LoadImage(SDL_IOStream* source, byte closeStream);

    [DllImport("SDL3_ttf", EntryPoint = "TTF_OpenFontIO")]
    private static extern TTF_Font* OpenFont(SDL_IOStream* source, byte closeStream, float pointSize);
}
