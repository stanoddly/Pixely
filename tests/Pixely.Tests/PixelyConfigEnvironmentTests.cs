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
        Func<string, string?> variables = Variables(
            (PixelyConfigEnvironment.GpuBackendVariable, value),
            (PixelyConfigEnvironment.HeadlessVariable, value),
            (PixelyConfigEnvironment.SdlLoggingVariable, value),
            (PixelyConfigEnvironment.GpuValidationVariable, value));

        Assert.That(PixelyConfigEnvironment.Apply(config, variables), Is.EqualTo(config));
    }

    [TestCase("automatic", GpuBackend.Automatic)]
    [TestCase("vulkan", GpuBackend.Vulkan)]
    [TestCase("direct3d12", GpuBackend.Direct3D12)]
    [TestCase("metal", GpuBackend.Metal)]
    [TestCase(" VULKAN ", GpuBackend.Vulkan)]
    public void Apply_WithSupportedGpuBackend_OverridesGpuBackend(string value, GpuBackend expected)
    {
        PixelyConfig config = PixelyConfigEnvironment.Apply(new PixelyConfig(), Variables((PixelyConfigEnvironment.GpuBackendVariable, value)));

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

        PixelyConfig applied = PixelyConfigEnvironment.Apply(config, variables);

        Assert.That(applied.Headless, Is.EqualTo(expected));
        Assert.That(applied.EnableSdlLogging, Is.EqualTo(expected));
        Assert.That(applied.EnableGpuValidation, Is.EqualTo(expected));
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

        PixelyConfig applied = PixelyConfigEnvironment.Apply(config, Variables((PixelyConfigEnvironment.HeadlessVariable, "1")));

        Assert.That(applied, Is.EqualTo(config with { Headless = true }));
    }

    [Test]
    public void PixelyAppBuilder_RegistersConfigWithEnvironmentApplied()
    {
        string? previous = Environment.GetEnvironmentVariable(PixelyConfigEnvironment.HeadlessVariable);
        Environment.SetEnvironmentVariable(PixelyConfigEnvironment.HeadlessVariable, "1");
        try
        {
            PixelyAppBuilder appBuilder = new();
            appBuilder.AddSingleton(new PixelyConfig(ApplicationIdentifier: "com.example.app"));
            using ServiceProvider provider = appBuilder.BuildServiceProvider();

            PixelyConfig config = provider.GetRequiredService<PixelyConfig>();
            Assert.That(config.Headless, Is.True);
            Assert.That(config.ApplicationIdentifier, Is.EqualTo("com.example.app"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(PixelyConfigEnvironment.HeadlessVariable, previous);
        }
    }
}
