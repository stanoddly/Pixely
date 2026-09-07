# Retained UI

`Pixely.Ui` builds a tree of elements once and keeps it. A change writes to the element it concerns rather than rebuilding the element tree. `Pixely.Pencuil` is the immediate-mode alternative: it has no tree, and every change reruns the whole build.

What that buys is measure and arrange, which are cached per subtree: a clean subtree asked for the same size and the same position returns what it already holds. Painting is not incremental — any rebuild walks every visible element and produces the instruction list afresh. So a change costs the layout of the path it invalidated plus a full repaint, not a full rebuild.

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

A layer is measured against the viewport and arranged to it, so its own `Width`, `Height`, alignment, margin and offset are never consulted — it already fills the window. Set those on what is inside it.

A layer does not block the pointer by being on top. Only an `IPointerTarget` is hit-tested at all, so a modal backdrop has to be one; an ordinary panel over a button lets the button through. A target that declines a button does not fall through to a UI target beneath it either — the event is simply left unconsumed for whatever is outside the UI.

## Sizing

`Sizing` is per axis, set through `Element.Width` and `Element.Height`:

| Mode | Meaning |
| --- | --- |
| `Sizing.Fit` (default) | Size to content. |
| `Sizing.Fixed(px)` | Exactly that many pixels, even when it exceeds what was offered. |
| `Sizing.Grow(weight)` | On a stack's main axis, a share of what is left after the non-growing siblings are measured. On any other axis, fill it. |
| `Sizing.Percent(fraction)` | A fraction of the parent's content extent. |

`Grow` and `Percent` both need a number the parent has already committed to. On an axis whose extent is indefinite there is no such number, so both degrade to `Fit` rather than resolving circularly.

`Margin` is outside the element, `Padding` inside it. A margin may be negative; padding rejects a negative edge. `HorizontalAlignment` and `VerticalAlignment` place an element in space larger than it asked for; `Alignment.Stretch` fills that space instead, is ignored when the element declares an explicit size, and degrades to `Start` when the parent's own extent on that axis is indefinite.

## Layouts

An element's `Layout` decides where its children go. The three containers are element types that pick a layout for you:

```csharp
new Column(gap: 8)   // Layout = new StackLayout(Orientation.Vertical, 8)
new Row(gap: 8)      // Layout = new StackLayout(Orientation.Horizontal, 8)
new Overlay()        // Layout = OverlayLayout.Instance, every child offered the whole content box
```

An overlay offers each child the same slot; it does not force them to fill it. A `Fit` child keeps its desired size and is placed in that slot by its alignment. A child spans the slot when its sizing works out that way — stretching, growing, `Percent(1f)`, a matching fixed size, or content that happens to be that big.

`AnchoredLayout` pins each child to its own `Anchor` point and keeps it inside the parent, which is what a context menu or a tooltip needs:

```csharp
Column menu = new(gap: 4) { Anchor = worldPositionInUiSpace };

Element layer = new()
{
    Layout = AnchoredLayout.TopLeft,
    Children = { menu }
};
```

`Anchor` is a coordinate in the arranging layout's space. The pivot says which point of the child's own margin box lands on it — `Start` its leading edge, `Center` its middle, `End` its trailing edge. `Offset` is added to the anchor, and the margin box is then moved back inside the parent, so a menu opening near the right edge stays on screen. A child larger than the parent is pinned to the leading edge instead, since no position fits. It is the margin box that is kept inside, so a negative margin can still leave the child's own bounds outside.

`Alignment.Stretch` is accepted as a pivot and behaves as `Start`: there is no extent to fill against a point.

Clamping is why this is a layout rather than an `Offset` the caller computes: a caller cannot clamp against a size it does not know yet, and the size is not known until the child is measured. It also means a viewport that changes re-clamps by arranging again.

Anchoring to something in the world means converting first — casting `Vector2` to `Vector2Int` truncates, so round if the anchor came from a camera.

`Offset` shifts an element away from where its layout put it, and is an arrange property, so changing it re-arranges without re-measuring anything. Stacks and overlays apply it after alignment; `AnchoredLayout` instead adds it to the anchor before pivoting and clamping, so there it moves the anchor rather than the result.

Write a custom `ILayout` when allocation is the thing that differs. `ILayoutHost` is the sizing mechanics the layout calls into; `Element` implements it.

## Elements

`Label`, `Button`, `Image`, `TextBox`, `NumberBox<T>` and `ClipBorder` are the built-in leaves and wrappers. Everything else is composition: a panel is an `Element` with a `Background` and a `Layout`.

Backgrounds are `Drawable`s — `SolidDrawable`, `SpriteDrawable`, `NinePatchDrawable` — and are painted before the element's clip is pushed, so `ClipsContent` clips children and content, never the element's own background.

A custom element overrides `MeasureContent` and `PaintContent`. Drawing content and having children are independent — `MeasureContent` can measure intrinsic content against `MeasureChildren` — so set `MaxChildCount` only to say what the element actually accepts. `Divider` below sets zero because it is a leaf, not because it paints:

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

`Build` runs on attach, and `Sync` runs on attach and then only when the model reports a change — never per frame.

A view can be attached again after being removed, and `Build` runs again when it is. What detaching does *not* do is take the old tree apart, so whether reattaching works is a property of `Build`: returning a freshly created tree is fine, and so is returning the same root it returned before. What throws is composing a retained descendant into a newly created parent, because that element is still parented to the tree the first `Build` made.

Creating elements and subscribing to them in the constructor is the intended shape; what a constructor must not do is call `Build` or `Sync`, which are virtual and would run before a derived class had initialised its fields. That is why attaching, not construction, is what builds the tree.

Assigning an unchanged value to an ordinary property is a no-op — the setters compare before invalidating — so `Sync` can write every scalar it owns without checking which one moved.

`Button.Content` and `ClipBorder.Content` are the exception: they are backed by the child collection, so assigning one clears and re-adds the child even when it is the same element, which invalidates structurally. (Assigning `null` to an already-empty one does nothing.) Write to the content element you kept rather than reassigning the property.

A view reading more than one model calls `Observe` for each; the base owns the subscribing and unsubscribing, and observing the same model twice is ignored. The list of observed models lasts as long as the view, while the `Changed` subscriptions exist only while it is attached. Observing a model after attachment subscribes it but does not synchronise there and then. `OnAttached` and `OnDetached` are the hooks for anything the view holds that is not an element.

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

Capture is per button, so a right-drag and a left-drag can be held by different elements at once. Hover then has a single owner: the left button's capture if there is one, otherwise the oldest capture still standing. While a gesture is in progress that owner is the only element that can be hovered, which is what makes a pressed button un-highlight when the pointer is dragged off it and light up again on return — a *different* element holding another button meanwhile is not hovered at all. Ownership picks an element, not one of its buttons, so an element holding both left and right is simply the owner.

`OnPointerRelease` reports whether the release landed inside, which is what separates a click from a press the user dragged away. `inside` means the captured element is still the topmost target at that position, not merely that the position is within its bounds — another `IPointerTarget` over it makes the release land outside. A purely visual element drawn on top changes nothing, since only pointer targets are hit-tested.

`OnPointerCancel` means the press ended without a release the element can be told about. That covers more than losing a drag: the pointer left the window, the same button was pressed again anywhere — the old gesture is cancelled before the new press is even hit-tested, so a press that goes on to be declined still cancels it — the element left the tree or became hidden or disabled, or the press was accepted at a moment when capture could not be installed.

Motion is never consumed — a camera that follows the mouse has to keep seeing it while the pointer is over a button.

Callbacks may do anything, including pressing again, moving the pointer, or restructuring the tree. The router is written for that: hover is settled against the tree after every transition rather than assumed, and a gesture that replaced another during a cancel sweep is not cancelled along with it.

## Focus, keyboard and text

An element takes the keyboard by implementing `IFocusTarget`. A left press moves focus to the element it lands on only when that press was accepted *and* the element is an `IFocusTarget`; anything else takes focus away, including an accepted press on a `Button`, which is only an `IPointerTarget`. `UiRoot.Focus` sets it directly — there is no press to accept, so it requires only that the element is an `IFocusTarget` and that input can still reach it — and `FocusChanged` reports where it ended up.

`OnKeyDown` returns whether the element used the key, which is what keeps it from reaching the game. `isRepeat` says whether the platform produced it because the key is held: moving a caret and deleting want repeats, anything that toggles a mode does not. `OnTextInput` receives text the platform has already committed, however many keystrokes it took.

Focus is reconciled against the tree, so an element that is removed, hidden or disabled loses focus even though none of those raises an input event. Reconciliation is not instant: hiding or disabling only invalidates. Focus is settled on the next `UiRoot.Update`, on a key or text dispatch, on a pointer press, or immediately when a whole layer is removed — but not on pointer motion, release or window leave. Until then `FocusedElement` can still name the element that just became unreachable.

The input bridge subscribes at `inputOrder`, which defaults to `-10_000`. Handlers run in ascending priority and a consumed event stops the rest, so the UI sees input before a plain `keyboardService.KeyDown +=` at priority `0`. A key the focused element does not use is left unconsumed and carries on to those handlers, so being focused does not swallow everything. Platform text input is started and stopped from where focus ends up, not from the transitions on the way there.

Three things are not implemented yet, and are worth knowing before designing around them: clicking a field focuses it but does not place the caret, and there is no pointer-drag selection; there is no Tab traversal, so an unhandled Tab passes straight through; and IME pre-edit is not drawn, since only committed text is subscribed.

## Text fields

`TextBox` edits a string; `NumberBox<T>` edits any `struct` implementing `INumber<T>`:

```csharp
TextBox name = new() { Width = Sizing.Fixed(240), Clipboard = clipboardService };
name.Committed += text => viewModel.Name = text;

NumberBox<int> width = new(formatProvider: CultureInfo.InvariantCulture) { Width = Sizing.Fixed(240) };
width.ValueCommitted += value => viewModel.Width = value;
```

Enter commits an acceptable value and releases focus, Escape cancels, and clicking away commits an acceptable value and discards an unacceptable one. `Committed` and `ValueCommitted` are the only notifications — a field does not report every keystroke.

A field that will not accept what is in it does not commit. `NumberBox<T>` allows the values on the way to a number, since refusing them would make the numbers they lead to unreachable: a candidate is accepted when it is empty, parses as `T`, or parses with a digit appended. What that admits therefore depends on `T` — `NumberBox<float>` takes `-`, `1.` and `1e`, while `NumberBox<int>` takes `-` and rejects the other two.

Only a complete number finishes an edit. Enter on an incomplete one consumes the key and keeps focus, leaving the user looking at what needs fixing; losing focus discards the edit rather than committing it. For the types whose parsers produce them, infinity and NaN are refused as well.

Copy and paste do nothing until `Clipboard` is set. Cut still deletes the selection, since deleting is the half that needs no clipboard. `Clipboard` is a property rather than a constructor dependency because an element is not resolved from the container.

Assigning `Text` while the field is focused leaves what is being typed alone. It becomes the value the edit is compared against when it finishes, not a replacement for it — which is what lets `Sync` write every field unconditionally.

A field measures against its value rather than against what is being typed, so it does not resize with every keystroke and shuffle its neighbours along. Text is scrolled horizontally to keep the caret visible.

Derive from `TextBox` and override `AcceptsEdit` and `CanCommit` for a field with its own rules.

## Styling

`UiStyle` holds what an element falls back on when it was not given a value: `Body`, `Title` and `Small` fonts, `Foreground`, `DisabledForeground`, `Selection`, `Caret`, `ButtonBackground` and `FieldBackground`. An element given an explicit value ignores the style. Replacing `UiRoot.Style` invalidates every layer.

Not every field is consumed by every element. The fonts, `ButtonBackground` and `FieldBackground` are general; `Foreground`, `DisabledForeground`, `Selection` and `Caret` are currently read by `TextBox` alone. `Label.Color` is a plain colour defaulting to white, so a label — including the one a `Button(string)` makes — is not tinted by `Foreground`. Set `Label.Color` when you want a label to follow a palette.

Everything that takes a font takes `IFont` — `UiStyle.Body/Title/Small`, `Label.Font`, `TextBox` and `NumberBox<T>`. `Font` implements it, so passing one loaded from an `IFontSystem` is unchanged. The interface is what makes text layout testable without a device: measuring goes through `IFont.Measure`, which for a real `Font` is `TTF_GetStringSizeWrapped` and uploads nothing, while `IFont.CreateTextSprite` is reached only by painting. A test can substitute a font with stated metrics and let rasterisation throw, which is what `FixedWidthFont` in `Pixely.Ui.Tests` does.

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
