using Pixely.Content;

namespace Pixely.Input;

public interface ICursorService
{
    bool IsCursorVisible { get; set; }

    Cursor CreateSystemCursor(SystemCursor shape);
    Cursor CreateCursor(Image image, Vector2Int hotspot);
    Cursor CreateCursor(ReadOnlySpan<CursorFrame> frames, Vector2Int hotspot);

    void SetCursor(Cursor cursor);
    void ResetCursor();
}
