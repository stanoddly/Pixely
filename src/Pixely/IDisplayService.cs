namespace Pixely;

// The connected displays. Each call reads them again, because the user can connect, disconnect, move or rescale a display while the
// app runs. Main thread only.
public interface IDisplayService
{
    Display GetPrimaryDisplay();

    IReadOnlyList<Display> GetDisplays();
}
