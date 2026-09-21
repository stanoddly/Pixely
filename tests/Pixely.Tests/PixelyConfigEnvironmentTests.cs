using Pixely.App;
using Pixely.DependencyInjection;

namespace Pixely.Tests;

public class PixelyConfigEnvironmentTests
{
    private static Func<string, string?> Variables(params (string Name, string? Value)[] variables)
    {
        Dictionary<string, string?> lookup = variables.ToDictionary(variable => variable.Name, variable => variable.Value);
        return name => lookup.GetValueOrDefault(name);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Apply_WithoutVariables_KeepsConfig(string? value)
    {
        PixelyConfig config = new(EnableSdlLogging: true, EnableGpuValidation: false, GpuBackend: GpuBackend.Direct3D12, Headless: true);
        PixelyConfig original = config with { };
        Func<string, string?> variables = Variables(
            (PixelyConfigEnvironment.GpuBackendVariable, value),
            (PixelyConfigEnvironment.HeadlessVariable, value),
            (PixelyConfigEnvironment.SdlLoggingVariable, value),
            (PixelyConfigEnvironment.GpuValidationVariable, value));

        PixelyConfigEnvironment.Apply(config, variables);

        Assert.That(config, Is.EqualTo(original));
    }

    [TestCase("automatic", GpuBackend.Automatic)]
    [TestCase("vulkan", GpuBackend.Vulkan)]
    [TestCase("direct3d12", GpuBackend.Direct3D12)]
    [TestCase("metal", GpuBackend.Metal)]
    [TestCase("webgpu", GpuBackend.WebGpu)]
    [TestCase(" VULKAN ", GpuBackend.Vulkan)]
    public void Apply_WithSupportedGpuBackend_OverridesGpuBackend(string value, GpuBackend expected)
    {
        PixelyConfig config = new();

        PixelyConfigEnvironment.Apply(config, Variables((PixelyConfigEnvironment.GpuBackendVariable, value)));

        Assert.That(config.GpuBackend, Is.EqualTo(expected));
    }

    [Test]
    public void Apply_WithUnsupportedGpuBackend_Throws()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => PixelyConfigEnvironment.Apply(new PixelyConfig(), Variables((PixelyConfigEnvironment.GpuBackendVariable, "opengl"))))!;

        Assert.That(exception.Message, Does.Contain("PIXELY_GRAPHICS"));
        Assert.That(exception.Message, Does.Contain("opengl"));
    }

    [TestCase("1", true)]
    [TestCase("true", true)]
    [TestCase(" TRUE ", true)]
    [TestCase("0", false)]
    [TestCase("false", false)]
    public void Apply_WithBooleanVariables_OverridesEachFlag(string value, bool expected)
    {
        PixelyConfig config = new(EnableSdlLogging: !expected, EnableGpuValidation: !expected, Headless: !expected);
        Func<string, string?> variables = Variables(
            (PixelyConfigEnvironment.HeadlessVariable, value),
            (PixelyConfigEnvironment.SdlLoggingVariable, value),
            (PixelyConfigEnvironment.GpuValidationVariable, value));

        PixelyConfigEnvironment.Apply(config, variables);

        Assert.That(config.Headless, Is.EqualTo(expected));
        Assert.That(config.EnableSdlLogging, Is.EqualTo(expected));
        Assert.That(config.EnableGpuValidation, Is.EqualTo(expected));
    }

    [Test]
    public void Apply_WithUnsupportedBoolean_Throws()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => PixelyConfigEnvironment.Apply(new PixelyConfig(), Variables((PixelyConfigEnvironment.HeadlessVariable, "yes"))))!;

        Assert.That(exception.Message, Does.Contain("PIXELY_HEADLESS"));
        Assert.That(exception.Message, Does.Contain("yes"));
    }

    [Test]
    public void Apply_LeavesUnrelatedFields()
    {
        PixelyConfig config = new(ApplicationIdentifier: "com.example.app", TaskbarIconPath: "icon.png", DeliverActivatingMouseClicks: false);
        PixelyConfig expected = config with { Headless = true };

        PixelyConfigEnvironment.Apply(config, Variables((PixelyConfigEnvironment.HeadlessVariable, "1")));

        Assert.That(config, Is.EqualTo(expected));
    }

    [Test]
    public void PixelyAppBuilder_RegistersConfigWithEnvironmentApplied()
    {
        string? previous = Environment.GetEnvironmentVariable(PixelyConfigEnvironment.HeadlessVariable);
        Environment.SetEnvironmentVariable(PixelyConfigEnvironment.HeadlessVariable, "1");
        try
        {
            PixelyAppBuilder appBuilder = new();
            PixelyConfig registered = new(ApplicationIdentifier: "com.example.app");
            appBuilder.AddSingleton(registered);
            using ServiceProvider provider = appBuilder.BuildServiceProvider();

            PixelyConfig config = provider.GetRequiredService<PixelyConfig>();
            Assert.That(config, Is.SameAs(registered));
            Assert.That(config.Headless, Is.True);
            Assert.That(config.ApplicationIdentifier, Is.EqualTo("com.example.app"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(PixelyConfigEnvironment.HeadlessVariable, previous);
        }
    }
}
