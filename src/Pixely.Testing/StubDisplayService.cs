using XdlSharp;

namespace Pixely.Testing;

// One display of 1024 x 768 at scale 1, as SDL's dummy video driver reports it, on any thread.
internal sealed class StubDisplayService : IDisplayService
{
    internal static readonly Display Display = new(new DisplayId((SDL_DisplayID)1), new Rectangle(0, 0, 1024, 768), new Rectangle(0, 0, 1024, 768), 1, 1);

    public Display GetPrimaryDisplay() => Display;

    public IReadOnlyList<Display> GetDisplays() => [Display];
}
