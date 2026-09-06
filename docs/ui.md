# Retained UI

`Pixely.Ui` builds a tree of elements once and keeps it. A change writes to the element it concerns, and only what that change touched is measured, arranged and painted again. `Pixely.Pencuil` is the immediate-mode alternative: it has no tree, and every change reruns the whole build.

Neither replaces the other. Pencuil suits UI that is mostly a function of state that changes every frame anyway — debug overlays, editors, anything where writing the build is cheaper than keeping references. `Pixely.Ui` suits UI that outlives the frame: menus, HUDs, dialogs, anything with focus, text editing or a pointer gesture that spans frames.

## Layout of the library

```
UiRoot          one viewport: owns layers, runs the passes, routes pointer and focus
Element         the node; layout, painting and invalidation live here
ILayout         how a parent allocates space to children
Drawable        how a background paints itself
UiView          a tree plus the code that copies a view model into it
UiStyle         defaults an element falls back on when it was not given one
```

## Getting started

`UseUi` registers a root, its renderer and its input bridge for a window:

```csharp
builder.UseUi();
```

A style is optional but usually wanted, since it is what saves passing a font to every label:

```csharp
builder.AddSingleton<UiStyle>(provider =>
{
    IFontSystem fonts = provider.GetRequiredService<IFontSystem>();
    return new UiStyle(fonts.Load("fonts/GohuFont-Medium.ttf", 16))
    {
        Title = fonts.Load("fonts/GohuFont-Medium.ttf", 20)
    };
});
```

Register it before `Build()`. The root reads it once, when the container builds it.

Then add a layer, or a view:

```csharp
UiRoot root = pixelyApp.ServiceProvider.GetUiRoot();
root.AddLayer(new Column { Children = { new Label("Hello") } });
```

Layers are painted in the order they were added, and hit-tested back to front. A dialog is a second layer over the first, not a child of it.

## Sizing

`Sizing` is per axis, set through `Element.Width` and `Element.Height`:

| Mode | Meaning |
| --- | --- |
| `Sizing.Fit` (default) | Size to content. |
| `Sizing.Fixed(px)` | Exactly that many pixels, even when it exceeds what was offered. |
| `Sizing.Grow(weight)` | A share of what is left after the non-growing siblings are measured. |
| `Sizing.Percent(fraction)` | A fraction of the parent's content extent. |

`Margin` is outside the element, `Padding` inside it. `HorizontalAlignment` and `VerticalAlignment` place an element in space larger than it asked for; `Alignment.Stretch` fills that space instead, is ignored when the element declares an explicit size, and degrades to `Start` when the parent's own extent on that axis is indefinite.

## Layouts

An element's `Layout` decides where its children go. The three containers are convenience wrappers, not distinct types:

```csharp
new Column(gap: 8)   // Layout = new StackLayout(Orientation.Vertical, 8)
new Row(gap: 8)      // Layout = new StackLayout(Orientation.Horizontal, 8)
new Overlay()        // Layout = OverlayLayout.Instance, every child over the whole content box
```

`AnchoredLayout` pins each child to its own `Anchor` point and keeps it inside the parent, which is what a context menu or a tooltip needs:

```csharp
Column menu = new(gap: 4) { Anchor = worldPositionInUiSpace };

Element layer = new()
{
    Layout = AnchoredLayout.TopLeft,
    Children = { menu }
};
```

The anchor is a point on the child; the pivot says which of the child's own edges meets it — `Start` its leading edge, `Center` its middle, `End` its trailing edge. The child is then moved back inside the parent, so a menu opening near the right edge stays on screen. One larger than the parent is pinned to the leading edge instead, since no position fits.

Clamping is why this is a layout rather than an `Offset` the caller computes: a caller cannot clamp against a size it does not know yet, and the size is not known until the child is measured. It also means a viewport that changes re-clamps by arranging again.

Anchoring to something in the world means converting first — casting `Vector2` to `Vector2Int` truncates, so round if the anchor came from a camera.

`Offset` moves an element after its parent placed it. It is an arrange property, so changing it re-arranges without re-measuring anything.

Write a custom `ILayout` when allocation is the thing that differs. `ILayoutHost` is the sizing mechanics the layout calls into; `Element` implements it.

## Elements

`Label`, `Button`, `Image`, `TextBox`, `NumberBox<T>` and `ClipBorder` are the built-in leaves and wrappers. Everything else is composition: a panel is an `Element` with a `Background` and a `Layout`.

Backgrounds are `Drawable`s — `SolidDrawable`, `SpriteDrawable`, `NinePatchDrawable` — and are painted before the element's clip is pushed, so `ClipsContent` clips children and content, never the element's own background.

A custom element overrides `MeasureContent` and `PaintContent`, and caps its children with `MaxChildCount` when it draws its own content:

```csharp
public sealed class Divider : Element
{
    private Color _color = Colors.White;

    protected override int MaxChildCount => 0;

    public Color Color
    {
        get => _color;
        set => SetPaintProperty(ref _color, value);
    }

    protected override Vector2Int MeasureContent(Constraints constraints) => new(0, 1);

    protected override void PaintContent(PaintContext context) => context.FillRectangle(Bounds, _color);
}
```

Use the setter that matches how far the change reaches: `SetMeasureProperty` when the desired size can change, `SetArrangeProperty` when only placement can, `SetPaintProperty` when only appearance can. Each invalidates upward and stops at the first ancestor already marked, so writing many properties in a row costs about as much as writing one.

## Views and view models

A view owns a tree and knows how to copy a model into it. `IUiViewModel` is one event:

```csharp
public sealed class ScoreViewModel : IUiViewModel
{
    private int _score;

    public event Action? Changed;

    public int Score
    {
        get => _score;
        set
        {
            if (_score == value)
            {
                return;
            }

            _score = value;
            Changed?.Invoke();
        }
    }
}
```

`UiView<TViewModel>` splits building from updating:

```csharp
public sealed class ScoreView : UiView<ScoreViewModel>
{
    private readonly Label _score = new();

    public ScoreView(ScoreViewModel viewModel) : base(viewModel)
    {
    }

    protected override Element Build() => new Column { Children = { _score } };

    protected override void Sync() => _score.Content = ViewModel.Score.ToString();
}
```

`Build` runs exactly once, on attach. `Sync` runs on attach and then only when the model reports a change — never per frame. Keep the constructor to assignment only: `Build` and `Sync` are virtual, so calling them from a constructor would run before a derived class had initialised its fields.

Assigning a value that has not changed is a no-op all the way down, so `Sync` can write every field it owns without checking which one actually moved.

A view reading more than one model calls `Observe` for each; the subscription is owned by the base and lifetime-scoped, and observing the same model twice is ignored. `OnAttached` and `OnDetached` are the hooks for anything the view holds that is not an element.

## Registering views

Register a view as a singleton and it is added to the root for its `ViewScope` automatically:

```csharp
builder.AddSingleton<ScoreViewModel>();
builder.AddSingleton<IUiView, ScoreView>();
```

Register views as singletons, not transients. The container only tracks a transient it has to dispose, so a transient view would be attached to a root and never taken away. A view must derive from `UiView` to be found — `IUiView` is the role it is registered under, not an alternative to inheriting the behaviour.

Attaching by hand with `root.AddView(view)` stays available and is what the tutorials use, since it keeps the wiring visible in one file.

## Pointer

An element takes the pointer by implementing `IPointerTarget`. `OnPointerPress` returns whether the element takes that button: only an accepted press is captured, and only an accepted press leads to a release or a cancel. A declined press is left unconsumed and reaches whatever is behind the UI.

Capture is per button, so a right-drag and a left-drag can be held by different elements at once. While an element holds capture it is the only one that can be hovered, which is what makes a pressed button un-highlight when the pointer is dragged off it and light up again on return.

`OnPointerRelease` reports whether the release landed inside, which is what separates a click from a press the user dragged away. `OnPointerCancel` means the press ended without a release the element can be told about: the pointer left the window, another press of the same button took capture, or the element left the tree.

Motion is never consumed — a camera that follows the mouse has to keep seeing it while the pointer is over a button.

Callbacks may do anything, including pressing again, moving the pointer, or restructuring the tree. The router is written for that: hover is settled against the tree after every transition rather than assumed, and a gesture that replaced another during a cancel sweep is not cancelled along with it.

## Focus, keyboard and text

An element takes the keyboard by implementing `IFocusTarget`. A left press moves focus to the element it lands on, or clears it when the press was declined or landed on nothing. `UiRoot.Focus` sets it directly, and `FocusChanged` reports where it ended up.

`OnKeyDown` returns whether the element used the key, which is what keeps it from reaching the game. `isRepeat` says whether the platform produced it because the key is held: moving a caret and deleting want repeats, anything that toggles a mode does not. `OnTextInput` receives text the platform has already committed, however many keystrokes it took.

Focus is reconciled against the tree after every change, so an element that is removed, hidden or disabled loses focus even though none of those raises an input event.

The input bridge subscribes at `inputOrder`, which defaults to `-10_000`. Handlers run in ascending priority and a consumed event stops the rest, so the UI sees input before a plain `keyboardService.KeyDown +=` at priority `0`. Platform text input is started and stopped from where focus ends up, not from the transitions on the way there.

## Text fields

`TextBox` edits a string; `NumberBox<T>` edits any `INumber<T>`:

```csharp
TextBox name = new() { Width = Sizing.Fixed(240), Clipboard = clipboardService };
name.Committed += text => viewModel.Name = text;

NumberBox<int> width = new(formatProvider: CultureInfo.InvariantCulture) { Width = Sizing.Fixed(240) };
width.ValueCommitted += value => viewModel.Width = value;
```

Enter commits and releases focus, Escape cancels, and clicking away commits. `Committed` and `ValueCommitted` are the only notifications — a field does not report every keystroke.

`NumberBox<T>` rejects a keystroke that would leave the field unparseable rather than validating at the end, so there is no invalid state to handle on commit. Cut, copy and paste do nothing until `Clipboard` is set; it is not taken from the container, because an element is not resolved from one.

Assigning `Text` while the field is focused leaves what is being typed alone. It becomes the value the edit is compared against when it finishes, not a replacement for it — which is what lets `Sync` write every field unconditionally.

A field measures against its value rather than against what is being typed, so it does not resize with every keystroke and shuffle its neighbours along. Text is scrolled horizontally to keep the caret visible.

Derive from `TextBox` and override `AcceptsEdit` and `CanCommit` for a field with its own rules.

## Styling

`UiStyle` holds what an element falls back on when it was not given a value: `Body`, `Title` and `Small` fonts, `Foreground`, `DisabledForeground`, `Selection`, `Caret`, `ButtonBackground` and `FieldBackground`. An element given an explicit value ignores the style. Replacing `UiRoot.Style` invalidates every layer.

Backgrounds that react to interaction are `StateDrawables`: a required `Normal` plus optional `Hovered`, `Pressed`, `Focused` and `Disabled`, each falling back to `Normal` when it was not given.

Which state an element is in is the element's own answer. `TextBox` reports `Disabled` before `Focused`, so a disabled field looks disabled even while it holds the keyboard. `Focused` is last in the enum rather than beside the other interaction states, so the values already in use kept their numbers.

## More than one window

Every registration takes a `ViewScope`, and a root belongs to one:

```csharp
builder.UseUi();
builder.UseUi(ViewScopes.Inventory);
```

A view says which root it belongs to by overriding `IUiView.ViewScope`, so registering a view is the same line whichever window it is for. `GetUiRoot(viewScope)` resolves a specific root.

## Tutorials

- `Pixely.Tutorials.UiBoxes` — layout and sizing on their own.
- `Pixely.Tutorials.UiScoreboard` — a view model driving a tree that is built once.
- `Pixely.Tutorials.UiTextInput` — editable fields, focus and the clipboard.
