namespace Pixely;

/// <summary>Overrides <see cref="PixelyConfig"/> from environment variables so automation can switch an app to headless mode or another GPU backend without changing its code.</summary>
internal static class PixelyConfigEnvironment
{
    internal const string GpuBackendVariable = "PIXELY_GRAPHICS";
    internal const string HeadlessVariable = "PIXELY_HEADLESS";
    internal const string SdlLoggingVariable = "PIXELY_SDL_LOGGING";
    internal const string GpuValidationVariable = "PIXELY_GPU_VALIDATION";

    public static void Apply(PixelyConfig config, Func<string, string?> getVariable)
    {
        config.GpuBackend = ResolveGpuBackend(config.GpuBackend, getVariable(GpuBackendVariable));
        config.Headless = ResolveBoolean(config.Headless, HeadlessVariable, getVariable(HeadlessVariable));
        config.EnableSdlLogging = ResolveBoolean(config.EnableSdlLogging, SdlLoggingVariable, getVariable(SdlLoggingVariable));
        config.EnableGpuValidation = ResolveBoolean(config.EnableGpuValidation, GpuValidationVariable, getVariable(GpuValidationVariable));
    }

    internal static GpuBackend ResolveGpuBackend(GpuBackend configuredBackend, string? environmentBackend)
    {
        if (string.IsNullOrWhiteSpace(environmentBackend))
        {
            return configuredBackend;
        }

        return environmentBackend.Trim().ToLowerInvariant() switch
        {
            "automatic" => GpuBackend.Automatic,
            "vulkan" => GpuBackend.Vulkan,
            "direct3d12" => GpuBackend.Direct3D12,
            "metal" => GpuBackend.Metal,
            "webgpu" => GpuBackend.WebGpu,
            _ => throw new InvalidOperationException(
                $"Unsupported {GpuBackendVariable} value '{environmentBackend}'. " +
                "Expected one of: automatic, vulkan, direct3d12, metal, webgpu.")
        };
    }

    internal static bool ResolveBoolean(bool configuredValue, string variable, string? environmentValue)
    {
        if (string.IsNullOrWhiteSpace(environmentValue))
        {
            return configuredValue;
        }

        return environmentValue.Trim().ToLowerInvariant() switch
        {
            "1" or "true" => true,
            "0" or "false" => false,
            _ => throw new InvalidOperationException(
                $"Unsupported {variable} value '{environmentValue}'. Expected one of: 1, true, 0, false.")
        };
    }
}
