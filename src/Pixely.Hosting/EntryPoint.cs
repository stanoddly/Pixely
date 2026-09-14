using Pixely.App;

namespace Pixely.Hosting;

/// <summary>
/// The entry point Pixely owns. A project that sets <c>PixelyHosting</c> gets a generated
/// <c>Program.Main</c> that calls <see cref="Run"/> with the project's <c>Configure</c>; see
/// <c>docs/hosting.md</c>.
/// </summary>
public static class EntryPoint
{
    /// <summary>
    /// Builds the application from <paramref name="configure"/> and runs it to completion.
    /// </summary>
    /// <param name="args">The command-line arguments, passed through to <paramref name="configure"/>.</param>
    /// <param name="configure">Registers everything the application needs before it is built.</param>
    /// <returns>The process exit code.</returns>
    public static int Run(string[] args, Action<PixelyAppBuilder, string[]> configure)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(configure);

        PixelyAppBuilder builder = new();
        configure(builder, args);
        using IPixelyApp app = builder.Build();
        return app.Run();
    }
}
