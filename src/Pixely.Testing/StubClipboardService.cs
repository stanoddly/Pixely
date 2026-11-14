using Pixely.Input;

namespace Pixely.Testing;

// A clipboard of the app's own, so a test never reads or replaces the machine's. Empty text clears it, as SDL's does.
internal sealed class StubClipboardService : IClipboardService
{
    private string _text = "";

    public bool HasText => _text.Length > 0;

    public string? GetText() => _text;

    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _text = text;
    }
}
