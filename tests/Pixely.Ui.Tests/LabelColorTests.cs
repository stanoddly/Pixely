using Pixely.Gpu;

namespace Pixely.Ui.Tests;

/// <summary>
/// What a label paints in, asserted through the instruction it emits rather than through the
/// resolution itself, so the precedence and the painting are covered by the same test.
/// </summary>
public class LabelColorTests
{
    private static readonly Color Explicit = new(1, 2, 3, 255);
    private static readonly Color Normal = new(10, 10, 10, 255);
    private static readonly Color Hovered = new(20, 20, 20, 255);
    private static readonly Color Pressed = new(30, 30, 30, 255);
    private static readonly Color Disabled = new(40, 40, 40, 255);
    private static readonly Color StyleForeground = new(50, 50, 50, 255);
    private static readonly Color StyleDisabled = new(60, 60, 60, 255);
    private static readonly Color StyleMuted = new(70, 70, 70, 255);
    private static readonly Color StyleAccent = new(80, 80, 80, 255);

    [Test]
    public void WithoutAnythingElse_TakesTheStylesForeground()
    {
        Label label = new("hi");
        UiRoot root = Rooted(label, Style());

        Assert.That(PaintedColor(root), Is.EqualTo((FColor)StyleForeground));
    }

    [Test]
    public void WithoutAStyle_TakesTheBuiltInForeground()
    {
        Label label = new(new RasterisingFont(), "hi");
        UiRoot root = Rooted(label, style: null);

        Assert.That(PaintedColor(root), Is.EqualTo((FColor)UiStyle.DefaultForeground));
    }

    [Test]
    public void InAButton_TakesTheStylesButtonForeground()
    {
        Button button = new();
        Label label = new("hi");
        button.Content = label;
        UiRoot root = Rooted(button, Style());

        Assert.That(PaintedColor(root), Is.EqualTo((FColor)Normal));
    }

    [Test]
    public void InAButton_FollowsThePointer()
    {
        Button button = new() { Width = Sizing.Fixed(60), Height = Sizing.Fixed(30) };
        button.Content = new Label("hi");
        UiRoot root = Rooted(button, Style());

        root.PointerMoved(new Vector2Int(10, 10));
        bool repainted = root.Update();
        FColor hovered = PaintedColor(root);

        root.PointerPressed(new Vector2Int(10, 10));
        root.Update();

        Assert.Multiple(() =>
        {
            Assert.That(repainted, Is.True, "the hover has to ask for the repaint that draws the new colour");
            Assert.That(hovered, Is.EqualTo((FColor)Hovered));
            Assert.That(PaintedColor(root), Is.EqualTo((FColor)Pressed));
        });
    }

    [Test]
    public void ADisabledButton_DrawsItsLabelInTheButtonsDisabledColour()
    {
        Button button = new() { IsEnabled = false };
        button.Content = new Label("hi") { Emphasis = TextEmphasis.Accent };
        UiRoot root = Rooted(button, Style());

        Assert.That(PaintedColor(root), Is.EqualTo((FColor)Disabled), "the control it sits in answers for its text, emphasis or not");
    }

    [Test]
    public void ADisabledElementBetweenTheButtonAndTheLabel_StillDisablesIt()
    {
        // The button reports Normal — it is enabled and nothing is over it — so the state has to
        // come from the label's own effective enablement rather than from the source alone.
        Button button = new();
        Column group = new() { IsEnabled = false };
        group.Children.Add(new Label("hi"));
        button.Content = group;
        UiRoot root = Rooted(button, Style());

        Assert.Multiple(() =>
        {
            Assert.That(button.VisualState, Is.EqualTo(VisualState.Normal));
            Assert.That(PaintedColor(root), Is.EqualTo((FColor)Disabled), "the button's disabled colour, since it is the nearest source");
        });
    }

    [Test]
    public void WithNoSourceAbove_ADisabledAncestorTakesTheStylesDisabledForeground()
    {
        Column group = new() { IsEnabled = false };
        group.Children.Add(new Label("hi"));
        UiRoot root = Rooted(group, Style());

        Assert.That(PaintedColor(root), Is.EqualTo((FColor)StyleDisabled));
    }

    [Test]
    public void DisablingALabelAfterItWasPainted_RepaintsItDisabled()
    {
        Label label = new("hi");
        Button button = new();
        button.Content = label;
        UiRoot root = Rooted(button, Style());

        FColor enabled = PaintedColor(root);
        button.IsEnabled = false;
        bool repainted = root.Update();

        Assert.Multiple(() =>
        {
            Assert.That(enabled, Is.EqualTo((FColor)Normal));
            Assert.That(repainted, Is.True);
            Assert.That(PaintedColor(root), Is.EqualTo((FColor)Disabled));
        });
    }

    [Test]
    public void WithoutASourceOrAStyle_ADisabledLabelTakesTheBuiltInDisabledForeground()
    {
        Column group = new() { IsEnabled = false };
        group.Children.Add(new Label(new RasterisingFont(), "hi"));
        UiRoot root = Rooted(group, style: null);

        Assert.That(PaintedColor(root), Is.EqualTo((FColor)UiStyle.DefaultDisabledForeground));
    }

    [Test]
    public void StateColors_ResolveEveryStateAndFallBackToNormal()
    {
        StateColors full = new(Normal) { Hovered = Hovered, Pressed = Pressed, Focused = Explicit, Disabled = Disabled };
        StateColors bare = new(Normal);

        Assert.Multiple(() =>
        {
            Assert.That(full.Resolve(VisualState.Normal), Is.EqualTo(Normal));
            Assert.That(full.Resolve(VisualState.Hovered), Is.EqualTo(Hovered));
            Assert.That(full.Resolve(VisualState.Pressed), Is.EqualTo(Pressed));
            Assert.That(full.Resolve(VisualState.Focused), Is.EqualTo(Explicit));
            Assert.That(full.Resolve(VisualState.Disabled), Is.EqualTo(Disabled));
            Assert.That(bare.Resolve(VisualState.Hovered), Is.EqualTo(Normal));
            Assert.That(bare.Resolve(VisualState.Pressed), Is.EqualTo(Normal));
            Assert.That(bare.Resolve(VisualState.Focused), Is.EqualTo(Normal));
            Assert.That(bare.Resolve(VisualState.Disabled), Is.EqualTo(Normal));
        });
    }

    [Test]
    public void Emphasis_SelectsAmongTheStylesTextColours()
    {
        UiRoot muted = Rooted(new Label("hi") { Emphasis = TextEmphasis.Muted }, Style());
        UiRoot accent = Rooted(new Label("hi") { Emphasis = TextEmphasis.Accent }, Style());

        Assert.Multiple(() =>
        {
            Assert.That(PaintedColor(muted), Is.EqualTo((FColor)StyleMuted));
            Assert.That(PaintedColor(accent), Is.EqualTo((FColor)StyleAccent));
        });
    }

    [Test]
    public void ADisabledLabel_IsDrawnDisabledWhateverItsEmphasis()
    {
        Column group = new() { IsEnabled = false };
        group.Children.Add(new Label("hi") { Emphasis = TextEmphasis.Accent });
        UiRoot root = Rooted(group, Style());

        Assert.That(PaintedColor(root), Is.EqualTo((FColor)StyleDisabled));
    }

    [Test]
    public void ChangingEmphasisAfterItWasPainted_Repaints()
    {
        Label label = new("hi");
        UiRoot root = Rooted(label, Style());

        FColor before = PaintedColor(root);
        label.Emphasis = TextEmphasis.Accent;
        bool repainted = root.Update();

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo((FColor)StyleForeground));
            Assert.That(repainted, Is.True);
            Assert.That(PaintedColor(root), Is.EqualTo((FColor)StyleAccent));
        });
    }

    [Test]
    public void TheNearestSourceAboveTheLabel_Wins()
    {
        // An element of someone else's making is as much a source as a Button is, which is what
        // IVisualStateSource being public is for.
        StateSource outer = new(new StateColors(Explicit));
        StateSource inner = new(new StateColors(Normal));
        inner.Children.Add(new Label("hi"));
        outer.Children.Add(inner);
        UiRoot root = Rooted(outer, Style());

        Assert.That(PaintedColor(root), Is.EqualTo((FColor)Normal));
    }

    [Test]
    public void TextAppearance_ResolvesEachEmphasisAndPutsDisabledFirst()
    {
        TextAppearance appearance = new() { Foreground = Normal, Muted = Hovered, Accent = Pressed, Disabled = Disabled };

        Assert.Multiple(() =>
        {
            Assert.That(appearance.Resolve(TextEmphasis.Normal, isEnabled: true), Is.EqualTo(Normal));
            Assert.That(appearance.Resolve(TextEmphasis.Muted, isEnabled: true), Is.EqualTo(Hovered));
            Assert.That(appearance.Resolve(TextEmphasis.Accent, isEnabled: true), Is.EqualTo(Pressed));
            Assert.That(appearance.Resolve(TextEmphasis.Accent, isEnabled: false), Is.EqualTo(Disabled));
        });
    }

    [Test]
    public void AnUnsetAppearance_StillResolvesToTheBuiltInColours()
    {
        TextAppearance appearance = default;

        Assert.Multiple(() =>
        {
            Assert.That(appearance.Resolve(TextEmphasis.Normal, isEnabled: true), Is.EqualTo(UiStyle.DefaultForeground));
            Assert.That(appearance.Resolve(TextEmphasis.Muted, isEnabled: true), Is.EqualTo(UiStyle.DefaultMuted));
            Assert.That(appearance.Resolve(TextEmphasis.Accent, isEnabled: true), Is.EqualTo(UiStyle.DefaultAccent));
            Assert.That(appearance.Resolve(TextEmphasis.Normal, isEnabled: false), Is.EqualTo(UiStyle.DefaultDisabledForeground));
        });
    }

    private static UiStyle Style()
    {
        return new UiStyle(new RasterisingFont())
        {
            Text = new TextAppearance { Foreground = StyleForeground, Muted = StyleMuted, Accent = StyleAccent, Disabled = StyleDisabled },
            Button = new ButtonAppearance
            {
                Foreground = new StateColors(Normal) { Hovered = Hovered, Pressed = Pressed, Disabled = Disabled }
            }
        };
    }

    private static UiRoot Rooted(Element content, UiStyle? style)
    {
        UiRoot root = new() { Style = style ?? UiStyle.Default };
        root.AddLayer(new Column { Children = { content } });
        root.SetViewportSize(new Vector2Int(320, 240));
        root.Update();
        return root;
    }

    /// <summary>The tint of the last instruction, which is the deepest thing painted: the text.</summary>
    private static FColor PaintedColor(UiRoot root) => root.Instructions[^1].Tint;

    /// <summary>Stands in for a control outside Pixely.Ui that colours the text inside it.</summary>
    private sealed class StateSource(StateColors foreground) : Element, IVisualStateSource
    {
        public VisualState VisualState => IsEffectivelyEnabled ? VisualState.Normal : VisualState.Disabled;

        StateColors IVisualStateSource.ContentForeground => foreground;
    }
}
