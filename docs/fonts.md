# Fonts

`IFontSystem.Load` opens a TrueType or OpenType font from virtual content at a pixel size:

```csharp
IFontSystem fonts = provider.GetRequiredService<IFontSystem>();
Font body = fonts.Load("fonts/Montserrat-Medium.ttf", 18);
Font link = fonts.Load("fonts/Montserrat-Medium.ttf", 18, style: FontStyle.Underline);
```

`Load` takes these optional settings after the size:

- `rasterizationMode`: `Blended` (antialiased, the default), `Solid` (no antialiasing) or `Lcd` (subpixel).
- `hintingMode`: how FreeType fits glyph outlines to the pixel grid.
- `style`: a combination of `FontStyle` flags.
- `variations`: values for the axes of a variable font, such as its weight. See [Weights and other axes](#weights-and-other-axes).
- `features`: OpenType features, such as tabular figures. See [OpenType features](#opentype-features).
- `letterSpacing`: extra pixels after each character. See [Letter spacing](#letter-spacing).

The font system caches fonts by path, size and all settings. Loading the same combination again returns the same `Font`, and a different style returns a separate `Font`. `Font.Dispose` releases the font, so the next `Load` opens it again. `Font` reports each setting it was loaded with. `Font.Variations` and `Font.Features` come back sorted by tag, with only the last of a repeated tag.

Each combination opens the font file again and keeps its own copy of the file's data. Five weights of one variable font hold five copies of it.

A bitmap font must contain a strike of the requested size. `Load` throws otherwise and lists the sizes the font has.

The settings are per font. A label takes the font it draws with, so a label with tabular figures or wider spacing gets a font loaded with them: `new Label { Font = fonts.Load("fonts/Inter.ttf", 14, letterSpacing: 1) }`.

## Fractional sizes

The size is a `float`, so a size computed from a scale needs no rounding. With a scale of 1.33, `fonts.Load("fonts/Inter.ttf", 11 * 1.33f)` loads Inter at 14.63 px, and its text measures narrower than at 15 px. FreeType keeps a size to 1/64 of a pixel.

A bitmap font has only whole strike sizes, so a fractional size throws, as any size without a strike does. A size must be from 1 to 65535 pixels, or `Load` throws `ArgumentOutOfRangeException`. FreeType would draw a smaller size at 1 px.

## Weights and other axes

A variable font holds a range of weights, widths or other styles in one file. Each range is an axis with a four-letter tag, such as `wght` for weight and `wdth` for width. `variations` sets a value on each axis:

```csharp
Font light = fonts.Load("fonts/Inter-Variable.ttf", 14, variations: [new FontVariation("wght", 200)]);
Font semibold = fonts.Load("fonts/Inter-Variable.ttf", 14, variations: [new FontVariation("wght", 600)]);
Font condensed = fonts.Load("fonts/Inter-Variable.ttf", 14, variations: [new FontVariation("wght", 600), new FontVariation("wdth", 87.5f)]);
```

- Any value within an axis's range works, not only the weights the font names. A value outside the range is clamped to it.
- An axis that `variations` leaves out keeps the font's default.
- An axis the font does not have throws `PixelyException`, and so does a variation for a font without axes, such as a static or bitmap font.

`FontStyle.Bold` is different: SDL_ttf synthesizes it by thickening glyphs. A real weight from a variable font looks better, and both together thicken that weight further.

## OpenType features

OpenType features switch on alternate glyphs or behavior that a font contains. Each feature has a four-letter tag. `features` sets them:

```csharp
Font score = fonts.Load("fonts/Inter.ttf", 14, features: [new FontFeature("tnum")]);
Font code = fonts.Load("fonts/Inter.ttf", 14, features: [new FontFeature("liga", 0), new FontFeature("zero")]);
```

With `tnum`, tabular figures, every digit has the same width. A number that counts up then keeps its width, and a column of numbers aligns: "1111.11" and "8888.88" measure the same. Without it, many fonts use proportional figures, where a 1 is narrower than an 8.

- A value of 1, the default, turns a feature on and 0 turns it off. A value above 1 selects an alternate, as in `new FontFeature("salt", 2)`.
- A feature the font does not have changes nothing.
- A later feature with the same tag wins over an earlier one. The order of different tags does not matter, so lists that differ only in order load the same `Font`. The same holds for variations.
- Kerning is on by default. `new FontFeature("kern", 0)` turns it off.
- Commonly used tags: `tnum` tabular figures, `pnum` proportional figures, `lnum` lining figures, `onum` oldstyle figures, `zero` slashed zero, `liga` standard ligatures, `smcp` small capitals, `ss01` to `ss20` stylistic sets.

## Letter spacing

`letterSpacing` adds pixels after each character, and a negative value moves characters closer:

```csharp
Font tracked = fonts.Load("fonts/Inter.ttf", 11, letterSpacing: 0.5f);
Font tight = fonts.Load("fonts/Inter.ttf", 24, letterSpacing: -0.5f);
```

The spacing follows every character, the last one too. With 2 px, "Hello" measures 10 px wider. A letter with its combining accents counts as one character, so the accents stay over it. A ligature such as "ffi" counts as one too; `new FontFeature("liga", 0)` turns standard ligatures off. The spacing can be a fraction of a pixel. A tracking given in em, as in a design tool, is the em value times the size: -0.02 em at 24 px is -0.48 px. The spacing must be from -32768 to 32767 pixels, or `Load` throws `ArgumentOutOfRangeException`.

## Tags

A tag in `FontVariation` or `FontFeature` must be four ASCII letters or digits, or `Load` throws `ArgumentException`. A variation value must be finite and a feature value must not be negative. The OpenType specification lists the registered [feature tags](https://learn.microsoft.com/typography/opentype/spec/featuretags) and [axis tags](https://learn.microsoft.com/typography/opentype/spec/dvaraxisreg). A font's own axes and features are listed by tools such as `hb-info` or FontDrop.

## Styles

`FontStyle` is a flags enum, so styles combine: `FontStyle.Bold | FontStyle.Underline`.

- `Underline` and `Strikethrough` draw a line across the text. A scalable font provides the underline's position and the thickness of both lines in its metrics. The strikethrough always sits at half the font's height. A bitmap font has no such metrics, so both lines are 1 px thick and the underline starts 1 px above half the font's descent below the baseline.
- `Bold` and `Italic` are synthesized by SDL_ttf. Bold thickens each glyph and widens it by a tenth of the font size, rounded down to whole pixels. Italic slants the glyph outline. A real bold or italic font file looks better when one exists, and so does a weight of a variable font.
- Italic has no effect on a bitmap font, because SDL_ttf slants only outline glyphs.
- SDL_ttf does not synthesize `Bold` or `Italic` when the font file is already bold or italic. `Font.Style` still reports the style passed to `Load`.

`Font.Measure` accounts for the style. Bold text measures wider, and underlined or struck-through text measures taller when the line would fall below the font's normal height. The sprite from `Font.CreateTextSprite` has the measured size.

## Where the settings come from

Stock SDL_ttf has no API for variations, features or letter spacing. XDL, the patched SDL that Pixely is built on, adds `XDL_SetFontVariations`, `XDL_SetFontFeatures` and `XDL_SetFontCharSpacing` to SDL_ttf, and Pixely calls them once when it loads a font. They work wherever Pixely runs, the browser included.
