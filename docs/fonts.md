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

## Scaled fonts

`Font.GetScaled(factor)` returns the same font at `factor` times its size, which a UI that scales smoothly draws its text in (see [ui.md](ui.md), Scale modes):

```csharp
Font body = fonts.Load("fonts/Montserrat-Medium.ttf", 16);
Font large = body.GetScaled(1.5f)!;
```

- The scaled font rasterises at 24 px here, and its `Measure` and `CreateTextSprite` are in pixels of that size. A fractional size is kept, so 16 px at 1.33 rasterises at 21.28 px. `Size` stays 16, and `Scale` is 1.5.
- It shares the loaded font's data and settings, through `TTF_CopyFont`. FreeType keeps a size in 64ths of a point, so the size is rounded to one, and the font system caches the scaled font per size: two factors that land on the same 64th return the same `Font`, whose `Scale` is the rounded one. Scaling a scaled font multiplies the factors, and a factor that lands on the loaded size returns the loaded font.
- It is released with the loaded font. Disposing it releases only it, and the next `GetScaled` to its size scales again. Until then it stays loaded, so a zoom through many sizes keeps a FreeType face for each size it passed.
- A bitmap font returns null, because it comes only in its strike sizes.
- The factor must be finite and above 0.

`IFont.GetScaled` returns null unless an implementation overrides it, so a font of an application's own that does not scale is stretched by the UI rather than refused. A font system reaches the same through the internal `IFontSystem.GetScaled`, which only Pixely's font systems implement, because only Pixely creates a `Font`.

## Styles

`FontStyle` is a flags enum, so styles combine: `FontStyle.Bold | FontStyle.Underline`.

- `Underline` and `Strikethrough` draw a line across the text. A scalable font provides the underline's position and the thickness of both lines in its metrics. The strikethrough always sits at half the font's height. A bitmap font has no such metrics, so both lines are 1 px thick and the underline starts 1 px above half the font's descent below the baseline.
- `Bold` and `Italic` are synthesized by SDL_ttf. Bold thickens each glyph and widens it by a tenth of the font size, rounded down to whole pixels. Italic slants the glyph outline. A real bold or italic font file looks better when one exists.
- Italic has no effect on a bitmap font, because SDL_ttf slants only outline glyphs.
- SDL_ttf does not synthesize `Bold` or `Italic` when the font file is already bold or italic. `Font.Style` still reports the style passed to `Load`.

`Font.Measure` accounts for the style. Bold text measures wider, and underlined or struck-through text measures taller when the line would fall below the font's normal height. The sprite from `Font.CreateTextSprite` has the measured size.
