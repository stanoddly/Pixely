# Fonts

`IFontSystem.Load` opens a TrueType or OpenType font from virtual content at a pixel size:

```csharp
IFontSystem fonts = provider.GetRequiredService<IFontSystem>();
Font body = fonts.Load("fonts/Montserrat-Medium.ttf", 18);
Font link = fonts.Load("fonts/Montserrat-Medium.ttf", 18, style: FontStyle.Underline);
```

`Load` takes three optional settings after the size:

- `rasterizationMode`: `Blended` (antialiased, the default), `Solid` (no antialiasing) or `Lcd` (subpixel).
- `hintingMode`: how FreeType fits glyph outlines to the pixel grid.
- `style`: a combination of `FontStyle` flags.

The font system caches fonts by path, size and all three settings. Loading the same combination again returns the same `Font`, and a different style returns a separate `Font`. `Font.Dispose` releases the font, so the next `Load` opens it again.

A bitmap font must contain a strike of the requested size. `Load` throws otherwise and lists the sizes the font has.

## Styles

`FontStyle` is a flags enum, so styles combine: `FontStyle.Bold | FontStyle.Underline`.

- `Underline` and `Strikethrough` draw a line across the text. A scalable font provides the underline's position and the thickness of both lines in its metrics. The strikethrough always sits at half the font's height. A bitmap font has no such metrics, so both lines are 1 px thick and the underline starts 1 px above half the font's descent below the baseline.
- `Bold` and `Italic` are synthesized by SDL_ttf. Bold thickens each glyph and widens it by a tenth of the font size, rounded down to whole pixels. Italic slants the glyph outline. A real bold or italic font file looks better when one exists.
- Italic has no effect on a bitmap font, because SDL_ttf slants only outline glyphs.
- SDL_ttf does not synthesize `Bold` or `Italic` when the font file is already bold or italic. `Font.Style` still reports the style passed to `Load`.

`Font.Measure` accounts for the style. Bold text measures wider, and underlined or struck-through text measures taller when the line would fall below the font's normal height. The sprite from `Font.CreateTextSprite` has the measured size.
