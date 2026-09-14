namespace Pixely.Ui;

/// <summary>
/// Which kinds of input an element in the hit list takes. One list serves both the pointer and
/// the wheel, since what they share is paint order and clipping, and the flag is what lets a hit
/// test for one kind step over an element that only takes the other.
/// </summary>
[Flags]
internal enum HitKind : byte
{
    None = 0,
    Pointer = 1,
    Scroll = 2
}
