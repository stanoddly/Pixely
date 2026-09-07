namespace Pixely.Input;

/// <summary>
/// A clipboard that holds nothing. What an element falls back on when no real clipboard reached it:
/// copying discards, pasting produces nothing, and a cut still deletes because deleting is the
/// editor's own business.
/// </summary>
public sealed class NullClipboardService : IClipboardService
{
    public static readonly NullClipboardService Instance = new();

    private NullClipboardService()
    {
    }

    public bool HasText => false;

    public string? GetText() => null;

    public void SetText(string text)
    {
    }
}
