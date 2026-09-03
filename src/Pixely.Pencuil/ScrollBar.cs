using System.Numerics;
using Pixely.Gpu;

namespace Pixely.Pencuil;

// Track/thumb arithmetic kept free of Pencil so it can be exercised directly
internal static class ScrollBarGeometry
{
    internal static int MaximumOffset(int contentExtent, int viewportExtent) => Math.Max(0, contentExtent - viewportExtent);

    internal static int ClampOffset(int offset, int contentExtent, int viewportExtent)
    {
        return Math.Clamp(offset, 0, MaximumOffset(contentExtent, viewportExtent));
    }

    internal static bool IsScrollable(int contentExtent, int viewportExtent)
    {
        return contentExtent > viewportExtent && viewportExtent > 0;
    }

    internal static int ThumbLength(int trackLength, int contentExtent, int viewportExtent, int minimumThumbLength)
    {
        if (!IsScrollable(contentExtent, viewportExtent) || trackLength <= 0)
        {
            return 0;
        }

        int length = (int)((long)trackLength * viewportExtent / contentExtent);
        return Math.Clamp(length, Math.Min(minimumThumbLength, trackLength), trackLength);
    }

    internal static int ThumbStart(int trackLength, int thumbLength, int offset, int contentExtent, int viewportExtent)
    {
        int maximumOffset = MaximumOffset(contentExtent, viewportExtent);
        int travel = trackLength - thumbLength;
        if (maximumOffset <= 0 || travel <= 0)
        {
            return 0;
        }

        return (int)((long)travel * ClampOffset(offset, contentExtent, viewportExtent) / maximumOffset);
    }

    internal static int OffsetFromThumbStart(int trackLength, int thumbLength, int thumbStart, int contentExtent, int viewportExtent)
    {
        int maximumOffset = MaximumOffset(contentExtent, viewportExtent);
        int travel = trackLength - thumbLength;
        if (maximumOffset <= 0 || travel <= 0)
        {
            return 0;
        }

        long clampedThumbStart = Math.Clamp(thumbStart, 0, travel);
        // Round to nearest so dragging the thumb to either end reaches the exact bound
        return ClampOffset((int)((clampedThumbStart * maximumOffset + travel / 2) / travel), contentExtent, viewportExtent);
    }
}

public static class ScrollBarExtensions
{
    /// <summary>
    /// Draws a scrollbar that owns <paramref name="offset"/>. Returns true when the offset changed.
    /// </summary>
    public static bool ScrollBar(
        this Pencil pencil,
        int id,
        ref int offset,
        int contentExtent,
        int viewportExtent,
        int length,
        Orientation orientation = Orientation.Vertical)
    {
        GuiStyle style = pencil.Style;
        int thickness = style.ScrollBarThickness;
        Vector2Int size = orientation == Orientation.Vertical
            ? new Vector2Int(thickness, length)
            : new Vector2Int(length, thickness);

        Rectangle area = new Rectangle(pencil.CurrentPosition, size);
        pencil.CurrentSize = size;
        pencil.CurrentPosition = pencil.DetermineNextPosition(size);

        return ScrollBarCore(pencil, id, ref offset, contentExtent, viewportExtent, area, orientation);
    }

    internal static bool ScrollBarCore(
        Pencil pencil,
        int id,
        ref int offset,
        int contentExtent,
        int viewportExtent,
        Rectangle area,
        Orientation orientation)
    {
        GuiStyle style = pencil.Style;
        int startingOffset = offset;
        offset = ScrollBarGeometry.ClampOffset(offset, contentExtent, viewportExtent);

        pencil.AddRectangle(area, style.Background);
        pencil.AddScrollArea(area);

        int trackLength = orientation == Orientation.Vertical ? area.Height : area.Width;
        int thumbLength = ScrollBarGeometry.ThumbLength(trackLength, contentExtent, viewportExtent, style.MinimumThumbLength);

        if (thumbLength <= 0)
        {
            pencil.ReleaseCaptureIfHeldBy(id);
            return offset != startingOffset;
        }

        offset = ApplyWheel(pencil, offset, contentExtent, viewportExtent, area, orientation);

        int cursorAlongTrack = orientation == Orientation.Vertical
            ? pencil.CursorPosition.Y - area.Y
            : pencil.CursorPosition.X - area.X;
        int thumbStart = ScrollBarGeometry.ThumbStart(trackLength, thumbLength, offset, contentExtent, viewportExtent);

        if (pencil.IsCapturedBy(id))
        {
            pencil.CapturedControlSeenThisFrame = true;
            if (pencil.CursorPressed)
            {
                offset = ScrollBarGeometry.OffsetFromThumbStart(
                    trackLength, thumbLength, cursorAlongTrack - pencil.CaptureGrabOffset, contentExtent, viewportExtent);
                thumbStart = ScrollBarGeometry.ThumbStart(trackLength, thumbLength, offset, contentExtent, viewportExtent);
            }
        }
        else if (pencil.CursorJustPressed && pencil.IsCursorInWindow && pencil.Clip(area).Intersects(pencil.CursorPosition))
        {
            if (cursorAlongTrack >= thumbStart && cursorAlongTrack < thumbStart + thumbLength)
            {
                pencil.Capture(id, cursorAlongTrack - thumbStart);
            }
            else
            {
                // Clicking the bare track pages towards the cursor
                int page = cursorAlongTrack < thumbStart ? -viewportExtent : viewportExtent;
                offset = ScrollBarGeometry.ClampOffset(offset + page, contentExtent, viewportExtent);
                thumbStart = ScrollBarGeometry.ThumbStart(trackLength, thumbLength, offset, contentExtent, viewportExtent);
            }
        }

        Rectangle thumbArea = orientation == Orientation.Vertical
            ? new Rectangle(area.X, area.Y + thumbStart, area.Width, thumbLength)
            : new Rectangle(area.X + thumbStart, area.Y, thumbLength, area.Height);

        Color thumbColor = pencil.IsCapturedBy(id) ? style.ActiveColor : style.InactiveColor;
        pencil.AddHoverRectangle(thumbArea, thumbColor, thumbArea, style.ActiveColor);
        pencil.AddClickTest(area);

        return offset != startingOffset;
    }

    internal static int ApplyWheel(
        Pencil pencil,
        int offset,
        int contentExtent,
        int viewportExtent,
        Rectangle area,
        Orientation orientation)
    {
        Vector2 delta = pencil.PendingWheelDelta;
        if (delta == Vector2.Zero || !pencil.IsCursorInWindow || !pencil.Clip(area).Intersects(pencil.CursorPosition))
        {
            return offset;
        }

        // Wheel-up is positive in SDL while offsets grow downwards, so vertical scrolling inverts
        float steps = orientation == Orientation.Vertical ? -delta.Y : delta.X;
        if (steps == 0)
        {
            return offset;
        }

        pencil.ClearWheelDelta();
        return ScrollBarGeometry.ClampOffset(
            offset + (int)MathF.Round(steps * pencil.Style.ScrollStep), contentExtent, viewportExtent);
    }

    internal static void ReleaseCaptureIfHeldBy(this Pencil pencil, int id)
    {
        if (pencil.IsCapturedBy(id))
        {
            pencil.ReleaseCapture();
        }
    }
}
