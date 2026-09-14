using Pixely.App;

namespace Pixely.Hosting;

/// <summary>
/// The entry point Pixely owns. A project that sets <c>PixelyHosting</c> gets a generated
/// <c>Program.Main</c> that calls <see cref="Run"/> with the project's <c>Configure</c> and
/// <c>OnException</c>; see <c>docs/hosting.md</c>.
/// </summary>
public static class EntryPoint
{
    /// <summary>
    /// Builds the application from <paramref name="configure"/> and runs it to completion.
    /// </summary>
    /// <param name="args">The command-line arguments, passed through to <paramref name="configure"/>.</param>
    /// <param name="configure">Registers everything the application needs before it is built.</param>
    /// <param name="onException">
    /// Receives a failure from <paramref name="configure"/>, <c>Build</c> or <c>Run</c> before the application is
    /// disposed, and returns the exit code. When null the failure propagates.
    /// </param>
    /// <returns>The process exit code.</returns>
    public static int Run(string[] args, Action<PixelyAppBuilder, string[]> configure, Func<Exception, int>? onException = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(configure);

        PixelyAppBuilder builder = new();
        IPixelyApp? app = null;
        try
        {
            configure(builder, args);
            app = builder.Build();
            return app.Run();
        }
        catch (Exception exception) when (onException is not null)
        {
            return onException(exception);
        }
        finally
        {
            app?.Dispose();
        }
    }
}
