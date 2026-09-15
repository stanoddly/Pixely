using System.Numerics;

namespace Pixely.Ui;

/// <summary>
/// An element that takes the mouse wheel. The root owns hit testing and finds the target, so an
/// implementer only decides how far it can move.
/// </summary>
/// <remarks>
/// <para>
/// The wheel goes to the topmost pointer or scroll target under the pointer, and from there to
/// its ancestors: a wheel over a button inside a list reaches the list, and a modal backdrop that
/// is a pointer target keeps it from the list beneath, while a plain panel is as transparent to
/// it as to the pointer. Each target along the way is offered what is left of the delta and takes
/// the axes it can use; the rest carries on upward, and then to whatever is outside the UI.
/// </para>
/// <para>
/// Nothing is captured and nothing is remembered between wheels. A target that wants to
/// accumulate — fractions from a touchpad too small to move a pixel — keeps that itself.
/// </para>
/// </remarks>
public interface IScrollTarget
{
    /// <param name="delta">
    /// How far the user asked to scroll, in wheel notches. Positive Y asks for what is above the
    /// current position, which is what a conventional wheel rolled away from the user produces, and
    /// positive X asks for what is to the right of it, which is a wheel tilted to the right.
    /// Fractional from a touchpad. The value is what the platform delivers after the user's own
    /// settings, natural scrolling included.
    /// </param>
    /// <returns>
    /// The axes on which this element took the delta. Taking it includes banking a fraction too
    /// small to move a pixel yet; what is refused is a component that points past an end the
    /// element is already at, or along an axis it does not scroll. A refused component reaches
    /// the ancestors, while a taken one stops here.
    /// </returns>
    ScrollAxes OnScroll(Vector2Int position, Vector2 delta);
}
