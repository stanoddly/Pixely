using Pixely.Ui;

namespace Pixely.Fitness.Tests;

public sealed class PixelyConventionsTests
{
    private static readonly IReadOnlyList<string> Violations = PixelyConventions.ViewsTakeOneViewModel(new PixelyConventionsOptions(typeof(PixelyConventionsTests).Assembly));

    [Test]
    public void ViewWithOneViewModelIsFit()
    {
        Assert.That(Violations, Has.None.Contains(typeof(OneModelView).FullName!));
    }

    [Test]
    public void ViewWithNoViewModelIsAViolation()
    {
        Assert.That(Violations, Has.One.Contains($"{typeof(NoModelView).FullName}: constructor takes 0 view models"));
    }

    [Test]
    public void ViewWithTwoViewModelsIsAViolation()
    {
        Assert.That(Violations, Has.One.Contains($"{typeof(TwoModelView).FullName}: constructor takes 2 view models"));
    }

    [Test]
    public void ViewWithACollectionOfViewModelsIsAViolation()
    {
        Assert.That(Violations, Has.One.Contains($"{typeof(ManyModelView).FullName}: constructor takes a collection of view models through viewModels"));
        Assert.That(Violations, Has.One.Contains($"{typeof(ManyModelView).FullName}: constructor takes 0 view models"));
    }

    [Test]
    public void ViewWithADerivedCollectionOfViewModelsIsAViolation()
    {
        Assert.That(Violations, Has.One.Contains($"{typeof(DerivedCollectionView).FullName}: constructor takes a collection of view models through extras"));
    }

    [Test]
    public void ViewWithACollectionOfFactoriesIsFit()
    {
        Assert.That(Violations, Has.None.Contains(typeof(FactoryCollectionView).FullName!));
    }

    [Test]
    public void EveryConstructorIsChecked()
    {
        Assert.That(Violations, Has.One.Contains($"{typeof(TwoConstructorView).FullName}: constructor takes 2 view models"));
    }

    [Test]
    public void EvaluateRunsTheRuleUnlessSwitchedOff()
    {
        PixelyConventionsOptions options = new PixelyConventionsOptions(typeof(PixelyConventionsTests).Assembly);

        Assert.That(PixelyConventions.Evaluate(options)["Pixely ViewsTakeOneViewModel"].Violations, Is.EqualTo(Violations));
        Assert.That(PixelyConventions.Evaluate(options with { ViewsTakeOneViewModel = false })["Pixely ViewsTakeOneViewModel"].Violations, Is.Empty);
    }

    private sealed class Model : IUiViewModel
    {
        public event Action? Changed { add { } remove { } }
    }

    private sealed class OneModelView(Model viewModel) : UiView<Model>(viewModel)
    {
        protected override Element Build() => new Column();

        protected override void Sync()
        {
        }
    }

    private sealed class NoModelView : UiView
    {
        protected override Element BuildRoot() => new Column();

        protected override void Synchronize()
        {
        }
    }

    private sealed class TwoModelView(Model first, Model second) : UiView<Model>(first)
    {
        protected override Element Build() => new Column();

        protected override void Sync() => Observe(second);
    }

    private sealed class ManyModelView(IReadOnlyList<Model> viewModels) : UiView
    {
        protected override Element BuildRoot() => new Column();

        protected override void Synchronize() => Observe(viewModels[0]);
    }

    private sealed class Models : List<Model>
    {
    }

    private sealed class DerivedCollectionView(Model viewModel, Models extras) : UiView<Model>(viewModel)
    {
        protected override Element Build() => new Column();

        protected override void Sync() => Observe(extras[0]);
    }

    private sealed class FactoryCollectionView(Model viewModel, IEnumerable<Func<Model>> factories) : UiView<Model>(viewModel)
    {
        protected override Element Build() => new Column();

        protected override void Sync() => Observe(factories.First()());
    }

    private sealed class TwoConstructorView : UiView<Model>
    {
        public TwoConstructorView(Model viewModel) : base(viewModel)
        {
        }

        public TwoConstructorView(Model viewModel, Model other) : this(viewModel)
        {
            Observe(other);
        }

        protected override Element Build() => new Column();

        protected override void Sync()
        {
        }
    }
}
