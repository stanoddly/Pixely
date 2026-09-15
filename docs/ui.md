# Retained UI

`Pixely.Ui` builds a tree of elements once and keeps it. A change writes to the element it concerns rather than rebuilding the element tree.

What that buys is measure and arrange, which are cached per subtree: a clean subtree asked for the same size and the same position returns what it already holds. Painting is not incremental — any rebuild walks every visible element and produces the instruction list afresh. So a change costs the layout of the path it invalidated plus a full repaint, not a full rebuild.

A rebuild that produces the same instruction list as the previous one does not repaint the retained texture. Textures passed to the paint context are compared by reference and treated as immutable; to show new pixels, pass a new texture.

## Layout of the library

```
UiRoot          one viewport: owns layers, runs the passes, routes pointer and focus
Element         the node; layout, painting and invalidation live here
ILayout         how a parent allocates space to children
Drawable        how a background paints itself
UiView          a tree plus the code that copies a view model into it
UiStyle         the look of everything under a root; elements hold none of their own
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
    return new UiStyle(fonts.Load("fonts/GohuFont-Medium.ttf", 11))
    {
        Title = fonts.Load("fonts/GohuFont-Medium.ttf", 14)
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

## When the tree is built

`UseUi` registers a system that builds the tree in the update phase, before anything renders. Building is not a passive walk: it raises pointer enter and leave as layout moves under a stationary pointer, raises focus lost when a focused element leaves the tree, and runs every custom element, layout and drawable in it. A renderer may not raise those — the renderers sharing a frame all read domain data over one command buffer and are entitled to it not changing underneath them — so the UI renderer only paints what the build already produced.

`UseUi` takes one order per phase — `renderOrder`, `updateOrder`, `inputOrder` — and lower runs first in all three.

`updateOrder` says when the tree is built relative to the other updatables. It defaults to `UpdateOrders.Ui` so the UI builds after ordinary order-0 game systems and views sync against the state this frame produced. Updatables with an equal order run in registration order, see [frame-order.md](frame-order.md). A system that runs after the build and dirties the UI has its change shown on the next frame, not this one.

```csharp
builder.UseUi(updateOrder: 500);
```

The viewport event is raised by `SetTargetSize`, which the same system calls immediately before the build, and by `Update` applying a requested scale, not by the build itself. Pointer and focus callbacks still arrive during event routing as they always did, `RemoveLayer` still reconciles immediately, and `UiRoot.Update` stays public for an application that wants to drive a root itself.

A hidden or zero-area window does not build. A window resized between the update phase and rendering shows one blank UI frame, because the instructions describe the previous size; the next update catches up.

The build lays out against the render context's colour target, divided by the root's scale, see below. A render context whose colour target is a different size than the window is not supported: the UI is laid out for that target and the renderer refuses to draw it into a target of another size, so it stays blank.

## Scale

Everything in the tree is in logical pixels: sizes, margins, paddings, offsets, anchors, border and nine-patch thicknesses, font sizes and pointer positions. `UiRoot.Scale` says how many target pixels one logical pixel covers, and defaults to 1, where logical and target pixels are the same thing.

A game that renders a low-resolution scene and scales it up gives the root the same scale, so the UI sits on the scene's pixel grid instead of drawing finer pixels over it. A fixed scale is part of setup:

```csharp
builder.UseUi(scale: 2f);
```

A scale that changes at runtime, such as a zoom the player controls, is requested on the root:

```csharp
root.RequestScale(2f);
```

The request takes effect at the next `Update`, which is when the viewport, the pointer's logical position and the layout all change together, so it can be requested from anywhere, including a pointer callback. A root built by hand takes its starting scale in the initializer, `new UiRoot { Scale = 2f }`. The scale must be finite and at least 1.

The tree never sees the scale. `ViewportSize`, the viewport event, `PointerPosition` and every layout and pointer callback are in logical pixels; the renderer paints the tree at its logical size into the retained texture and presents that texture at the scale with nearest sampling. A 16 px font at 2x is 2x2 blocks, which is what keeps pixel fonts and sprites crisp; nothing is reloaded or re-rasterised. A fractional scale such as 2.5 works the same way and shows uneven pixel widths, the same as a scene scaled by it.

`ViewportSize` is `TargetSize` divided by the scale and rounded up, so a target that is not a multiple of the scale is covered by a last row and column of logical pixels that are partly outside it. Content aligned to the end of such an axis loses up to one logical pixel; an integer scale on a target it divides is exact.

Anchoring something in the tree to a position in the world means dividing that position by the scale after converting it to target pixels, since the anchor is in logical pixels.

## Sizing

`Sizing` is per axis, set through `Element.Width` and `Element.Height`:

| Mode | Meaning |
| --- | --- |
| `Sizing.Fit` (default) | Size to content. |
| `Sizing.Fixed(px)` | Exactly that many logical pixels, even when it exceeds what was offered. |
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

Anchoring to something in the world means converting first — into target pixels, then divided by `UiRoot.Scale` — and casting `Vector2` to `Vector2Int` truncates, so round if the anchor came from a camera.

`Offset` shifts an element away from where its layout put it, and is an arrange property, so changing it re-arranges without re-measuring anything. Stacks and overlays apply it after alignment; `AnchoredLayout` instead adds it to the anchor before pivoting and clamping, so there it moves the anchor rather than the result.

Write a custom `ILayout` when allocation is the thing that differs. `ILayoutHost` is the sizing mechanics the layout calls into; `Element` implements it.

## Elements

`Label`, `Button`, `Image`, `TextBox`, `NumberBox<T>`, `ClipBorder` and `ScrollView` are the built-in leaves and wrappers. Everything else is composition: a panel is an `Element` with a `Background` and a `Layout`.

Backgrounds are `Drawable`s — `SolidDrawable`, `SpriteDrawable`, `NinePatchDrawable`, `BorderDrawable` — and are painted before the element's clip is pushed, so `ClipsContent` clips children and content, never the element's own background. `BorderDrawable` takes a colour, a `Thickness` and an optional inner `Drawable` to fill what is left, so a border around a nine-patch is one drawable rather than two nested elements. It clamps its own edges: a thickness wider than the element paints an outline that swallows it rather than one that spills outside.

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

An element that must not commit its children to the space it was given overrides `ResolveContentConstraints`. It is called once per measure with the padding already removed, and what it returns is what every child's `Grow`, `Percent` and `Stretch` resolve against. The default commits what was offered; `ScrollView` is the one built-in override.

## Scrolling

`ScrollView` is a container that shows a window onto children larger than itself. It is a `Column` that clips, with the children slid by `ScrollOffset`: its `Layout` is the ordinary one and may be replaced, so a horizontal strip is a scroll view with a horizontal stack and `ScrollAxes.Horizontal`.

```csharp
ScrollView log = new(gap: 4)
{
    Width = Sizing.Grow(),
    Height = Sizing.Grow(),
    Children = { /* many labels */ }
};

log.ScrollOffset = new Vector2Int(0, int.MaxValue);   // to the end once laid out
```

`Axes` says which axes scroll, vertical by default. On a scroll axis the children are measured without a bound, so a `Grow` child there is measured to its content and a `Percent` child likewise, which is the degradation described under Sizing. On the other axis they get what the scroll view itself was given, so a `Grow` child of a vertical list fills its width when the list's own width is definite, as it would in a column.

The scroll view's own size is ordinary. A `Fit` scroll view takes what its children need up to what it is offered and only scrolls when that is less; one whose scroll axis is unbounded from outside as well grows to its children and never scrolls. Give it a `Fixed`, `Grow` or `Percent` extent on the axis it scrolls.

`ScrollOffset` is an arrange property: scrolling re-arranges the subtree and measures nothing. It is clamped into `[0, MaxScrollOffset]` when the tree is next built, so assigning `int.MaxValue` means the end once laid out, and after the build it reads the real position, the way `Bounds` does. `ScrollBy` moves within the range of the last build straight away and says whether the offset changed. `ScrollExtent` is the children plus the padding, and `ViewportSize` is the scroll view's own size; both are valid after a build.

Padding scrolls with the children, the way a padding box does on the web: the last row has the bottom padding under it at the end, and the clip is the scroll view's bounds as it is for every element.

`ScrollIntoView(descendant)` scrolls the least distance that brings an element below the scroll view wholly into the viewport on each axis that scrolls, or to its start when it is larger than the viewport. It is answered by the next build, from the geometry that build produces, so it is right after a change of size or content that has not been laid out yet; `ScrollOffset` reads the result after that build. It applies on top of an offset assigned before the build, a second call replaces the first, and a descendant that has left the subtree or been hidden by the build is not scrolled to. An element that is not below the scroll view is an `ArgumentException`.

The bars lie over the content along the trailing edges and take no space from it. `ScrollBars` is `Auto` by default, which shows a bar only while its axis overflows; a bar never shows on an axis that does not scroll. A press on a bar's track pages by one viewport towards the press; a press on the thumb grabs it, and dragging puts the grabbed point under the pointer, mapped to an offset through the thumb's travel. The bars are not children: `Children` holds only what was put there, and clearing it leaves the bars in place.

The mouse wheel reaches a scroll view through `IScrollTarget`, which anything can implement. The wheel goes to the topmost pointer or scroll target under the pointer, and from there up through its ancestors, so a wheel over a button inside a list reaches the list, and a modal backdrop that is an `IPointerTarget` keeps it from the list beneath. A plain panel is as transparent to the wheel as it is to the pointer. Each target along the way is offered what is left of the delta and returns the axes it took; the rest carries on upward, and then to whatever is outside the UI, which is what keeps a wheel over a list that has reached its end available to the game. A scroll view at its end refuses a delta that points further out and takes one that points back in, and it banks a touchpad's fractions too small to move a pixel until they add up to one. `WheelStep` is the pixels per notch.

A scroll view whose `Axes` is `Horizontal` alone takes the vertical component as a horizontal one, since a plain wheel produces nothing else: rolling towards the user moves to the right, as it moves down elsewhere. The two components are added into one request, so at an end the whole is refused and reaches an outer vertical list. A view with both axes keeps the components apart.

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

An element that follows the pointer while it holds a press, a slider or a scrollbar's thumb, implements `IPointerDragTarget` as well. `OnPointerDrag` is sent for every move between the accepted press and its release or cancel, with the button that press was, wherever the pointer has gone, including off the element; a button held by another element is reported to that element alone. The drag is delivered before hover follows the move, so a drag callback that rearranges the tree is settled by the hover that comes after it. A plain `IPointerTarget` never hears of a move.

The wheel is routed separately, to `IScrollTarget`s, and is described under Scrolling. It moves the pointer and hover the way motion does, is delivered whatever capture is in progress, and is consumed only when a target took some of it. An element that is only an `IScrollTarget` is transparent to the pointer the way a panel is.

Callbacks may do anything, including pressing again, moving the pointer, or restructuring the tree. The router is written for that: hover is settled against the tree after every transition rather than assumed, and a gesture that replaced another during a cancel sweep is not cancelled along with it.

## Focus, keyboard and text

An element takes the keyboard by implementing `IFocusTarget`. A left press moves focus to the element it lands on only when that press was accepted *and* the element is an `IFocusTarget`; anything else takes focus away, including an accepted press on a `Button`, which is only an `IPointerTarget`. `UiRoot.Focus` sets it directly — there is no press to accept, so it requires only that the element is an `IFocusTarget` and that input can still reach it — and `FocusChanged` reports where it ended up.

`OnKeyDown` returns whether the element used the key, which is what keeps it from reaching the game. `isRepeat` says whether the platform produced it because the key is held: moving a caret and deleting want repeats, anything that toggles a mode does not. `OnTextInput` receives text the platform has already committed, however many keystrokes it took.

Focus is reconciled against the tree, so an element that is removed, hidden or disabled loses focus even though none of those raises an input event. Reconciliation is not instant: hiding or disabling only invalidates. Focus is settled on the next `UiRoot.Update`, on a key or text dispatch, on a pointer press, or immediately when a whole layer is removed — but not on pointer motion, release or window leave. Until then `FocusedElement` can still name the element that just became unreachable.

The input bridge subscribes at `inputOrder`, which defaults to `-10_000`. Handlers run in ascending order and a consumed event stops the rest, so the UI sees input before a plain `keyboardService.KeyDown +=` at order `0`. A key the focused element does not use is left unconsumed and carries on to those handlers, so being focused does not swallow everything. Platform text input is started and stopped from where focus ends up, not from the transitions on the way there.

Three things are not implemented yet, and are worth knowing before designing around them: clicking a field focuses it but does not place the caret, and there is no pointer-drag selection; there is no Tab traversal, so an unhandled Tab passes straight through; and IME pre-edit is not drawn, since only committed text is subscribed.

## Text fields

`TextBox` edits a string; `NumberBox<T>` edits any `struct` implementing `INumber<T>`:

```csharp
TextBox name = new() { Width = Sizing.Fixed(240) };
name.Committed += text => viewModel.Name = text;

NumberBox<int> width = new(formatProvider: CultureInfo.InvariantCulture) { Width = Sizing.Fixed(240) };
width.ValueCommitted += value => viewModel.Width = value;
```

Enter commits an acceptable value and releases focus, Escape cancels, and clicking away commits an acceptable value and discards an unacceptable one. `Committed` and `ValueCommitted` are the only notifications — a field does not report every keystroke.

A field that will not accept what is in it does not commit. `NumberBox<T>` allows the values on the way to a number, since refusing them would make the numbers they lead to unreachable: a candidate is accepted when it is empty, parses as `T`, or parses with a digit appended. What that admits therefore depends on `T` — `NumberBox<float>` takes `-`, `1.` and `1e`, while `NumberBox<int>` takes `-` and rejects the other two.

Only a complete number finishes an edit. Enter on an incomplete one consumes the key and keeps focus, leaving the user looking at what needs fixing; losing focus discards the edit rather than committing it. For the types whose parsers produce them, infinity and NaN are refused as well.

Copy and paste reach the clipboard `UseUi` resolved onto `UiRoot.Clipboard`, so they work in a field nobody configured. A root built by hand keeps its default, `NullClipboardService`, and there copy and paste do nothing while cut still deletes the selection, since deleting is the half that needs no clipboard. The clipboard sits on the root rather than on the field because an element is not resolved from the container, and rather than in `UiStyle` because it is a service and not a look.

Assigning `Text` while the field is focused leaves what is being typed alone. It becomes the value the edit is compared against when it finishes, not a replacement for it — which is what lets `Sync` write every field unconditionally.

A field measures against its value rather than against what is being typed, so it does not resize with every keystroke and shuffle its neighbours along. Text is scrolled horizontally to keep the caret visible.

Derive from `TextBox` and override `AcceptsEdit` and `CanCommit` for a field with its own rules.

## Styling

`UiStyle` is where a screen's look lives, and it is the only place: no element holds a colour or a drawable of its own. That is what makes a theme a theme rather than a set of defaults individual controls quietly walk away from — swapping `UiRoot.Style` restyles everything, with nothing left behind holding a value of its own. Replacing it invalidates every layer.

It is a record, so a variation is `style with { Button = quiet }` rather than a restatement of what did not change, and appearances are grouped per control so a variation touches one member:

```csharp
root.Style = new UiStyle(body)
{
    Title = titleFont,
    Text = new TextAppearance { Foreground = Value, Muted = Caption, Accent = Highlight },
    Button = new ButtonAppearance
    {
        Background = new StateDrawables(new SolidDrawable(Idle)) { Hovered = new SolidDrawable(Lit) },
        Foreground = new StateColors(Colors.White) { Hovered = Highlight }
    }
};
```

`ButtonAppearance`, `FieldAppearance` and `TextAppearance` are `record struct`s, and every member of them reads through a fallback — so `default` and `new()` produce a style that paints rather than one that is invisible, and a theme sets only what it cares about.

Fonts have no built-in default, since they have to be loaded from content. Everything else does: `UiStyle.Default` is what a root paints with until it is given a style, which is why nothing has to check for null.

### Which control reads what

`TextAppearance` is text that is not inside a control with a look of its own. Two small axes select within it, neither multiplying the other:

- `TextRole` — `Body`, `Title`, `Small` — picks the font.
- `TextEmphasis` — `Normal`, `Muted`, `Accent` — picks the colour.

```csharp
new Label("Scoreboard") { Role = TextRole.Title, Emphasis = TextEmphasis.Accent }
```

An intent rather than a colour, so the theme decides what "secondary" looks like and every screen that says it gets the same answer. `Disabled` beats both: unusable is the more important thing to show.

`ButtonAppearance`, `FieldAppearance` and `ScrollAppearance` cover the controls that have a look of their own; `FieldAppearance.Selection` and `Caret` are read by `TextBox` alone, and a null `Caret` means whatever the text is drawn in. `ScrollAppearance` is the bars of a `ScrollView`: a `Thumb` per state, an optional `Track` behind it, their `Thickness` and the `MinimumThumbLength` the thumb does not shrink below when the track permits.

A label inside a control takes that control's answer, not `TextAppearance`, so `Emphasis` is ignored there — what a button's text looks like is the button's to say. Resolution is short enough to state whole:

1. The nearest `IVisualStateSource` above the label, resolved for the current state.
2. Failing that, `UiStyle.Text` resolved for the label's `Emphasis` and its effective enablement.

### Wanting something different

There is no per-element override to reach for. In order of what to try:

- **A recurring intent** is an emphasis or, for a control, a member of the style. Three labels in a screen that all want to be quieter are `TextEmphasis.Muted`, not three colours.
- **A genuinely different control** is a different element. `IVisualStateSource` is public precisely so an element outside `Pixely.Ui` can colour the text inside it the way `Button` does.
- **Content that happens to be coloured** — a player's team colour, a health bar — is not styling at all. `Image.Tint` and a custom element's own `PaintContent` are where that belongs; a theme cannot hold a value computed at runtime.

`Element.Background` stays a primitive alongside `Padding` and `Margin`: a grey strip behind a toolbar is not a control look, and assigning one means one drawable for every state. `Button` and `TextBox` honour it ahead of the style's, which is what keeps an inherited property from accepting a value and then doing nothing.

### Fonts, states and the seam

Everything that takes a font takes `IFont` — `UiStyle.Body/Title/Small`, `Label.Font`, `TextBox` and `NumberBox<T>`. `Font` implements it, so passing one loaded from an `IFontSystem` is unchanged. The interface is what makes text layout testable without a device: measuring goes through `IFont.Measure`, which for a real `Font` is `TTF_GetStringSizeWrapped` and uploads nothing, while `IFont.CreateTextSprite` is reached only by painting. A test can substitute a font with stated metrics and let rasterisation throw, which is what `FixedWidthFont` in `Pixely.Ui.Tests` does.

Backgrounds that react to interaction are `StateDrawables`: a required `Normal` plus optional `Hovered`, `Pressed`, `Focused` and `Disabled`, each falling back to `Normal` when it was not given. `StateColors` is the same shape over `Color`.

A control's state reaches the text inside it through `IVisualStateSource`, which `Button` implements: a `Label` walks up to the nearest one and resolves its colour against that state while painting. This is what lets a button tint its label on hover without knowing that its content is text at all — the content stays an ordinary element. An implementation of the interface must invalidate paint when its state *or* its `ContentForeground` changes (`SetPaintProperty` for either kept in a field, `InvalidatePaint` for what is derived); nothing below it is marked dirty otherwise.

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
- `Pixely.Tutorials.UiTextInput` — editable fields and focus.
- `Pixely.Tutorials.UiScrollView` — a list and a strip larger than their panels, scrolled by the wheel, by a press on a bar's track and by dragging its thumb.
- `Pixely.Tutorials.Hotbar` — a custom `IPointerTarget` element and an anchored label following hover.
- `Pixely.Tutorials.StageSwitching` — views owned by a stage, added and removed with it.
- `Pixely.Tutorials.MultiWindowTextInput` — one root per window, each with its own focus.
- `Pixely.Tutorials.FileDialogs` and `Pixely.Tutorials.MessageBoxes` — buttons opening modal dialogs.
