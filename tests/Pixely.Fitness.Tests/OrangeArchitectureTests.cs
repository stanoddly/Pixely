// The fixtures' constructor parameters exist only for the rules to reflect over.
#pragma warning disable CS9113

using Orangey.Vocabulary;
using Pixely.Ecs;
using Pixely.Messages;
using Pixely.RenderOrchestration;
using Pixely.Ui;

namespace Pixely.Fitness.Tests;

public sealed class OrangeArchitectureTests
{
    private static readonly OrangeArchitectureOptions Options = new OrangeArchitectureOptions("Orangey", typeof(OrangeArchitectureTests).Assembly);

    [Test]
    public void ViewTakingOnlyItsViewModelIsFit()
    {
        Assert.That(OrangeArchitecture.ViewsReadOnlyTheirViewModel(Options), Has.None.Contains(typeof(ModelView).FullName!));
    }

    [Test]
    public void ViewTakingAQueryOrALookupIsAViolation()
    {
        IReadOnlyList<string> violations = OrangeArchitecture.ViewsReadOnlyTheirViewModel(Options);

        Assert.That(violations, Has.One.EqualTo($"{typeof(QueryView).FullName}: constructor takes ReadHealths through healths; a view reads the world only through its view model"));
        Assert.That(violations, Has.One.EqualTo($"{typeof(LookupView).FullName}: constructor takes ReadOnlyComponentLookup<Health> through health; a view reads the world only through its view model"));
    }

    [Test]
    public void ViewTakingASystemIsAViolation()
    {
        Assert.That(OrangeArchitecture.ViewsReadOnlyTheirViewModel(Options), Has.One.EqualTo($"{typeof(SystemView).FullName}: constructor takes HealthSystem through system; a view calls its view model, not a system"));
    }

    [Test]
    public void ViewTakingAMessageWriterOrAWrappedReaderIsAViolation()
    {
        IReadOnlyList<string> violations = OrangeArchitecture.ViewsReadOnlyTheirViewModel(Options);

        Assert.That(violations, Has.One.EqualTo($"{typeof(MessageView).FullName}: constructor takes MessageWriter<Ping> through pings; a view reads and writes no message, the system that writes its view model does"));
        Assert.That(violations, Has.One.EqualTo($"{typeof(MessageView).FullName}: constructor takes Func<MessageReader<Ping>> through readers; a view reads and writes no message, the system that writes its view model does"));
    }

    [Test]
    public void RendererReadingMessagesIsAViolation()
    {
        Assert.That(OrangeArchitecture.RenderersReadNoMessages(Options), Has.One.StartsWith($"{typeof(ReadingRenderer).FullName}: constructor takes MessageReader<Ping> through pings; a renderer reads no message"));
    }

    [Test]
    public void RendererThatAlsoUpdatesAndReadsMessagesIsAViolation()
    {
        Assert.That(OrangeArchitecture.RenderersReadNoMessages(Options), Has.One.StartsWith($"{typeof(UpdatingRenderer).FullName}: constructor takes MessageReader<Ping> through pings; "));
    }

    [Test]
    public void RendererWritingMessagesIsFit()
    {
        Assert.That(OrangeArchitecture.RenderersReadNoMessages(Options), Has.None.Contains(typeof(WritingRenderer).FullName!));
    }

    [Test]
    public void UpdatableOrRendererTakingAWritingQueryIsAViolation()
    {
        IReadOnlyList<string> violations = OrangeArchitecture.OutsideSystemsTakeNoWritingQueries(Options);

        Assert.That(violations, Has.One.EqualTo($"{typeof(WritingUpdatable).FullName}: constructor takes WriteHealths through healths; it writes through Span<Health>; code outside systems reads the world, so take a ReadOnlySpan"));
        Assert.That(violations, Has.One.EqualTo($"{typeof(OptionalWritingRenderer).FullName}: constructor takes OptionalWriteHealths through healths; it writes through Optional<Span<Health>>; code outside systems reads the world, so take a ReadOnlySpan"));
    }

    [Test]
    public void ReadingQueryOutsideSystemsAndWritingQueryInASystemAreFit()
    {
        IReadOnlyList<string> violations = OrangeArchitecture.OutsideSystemsTakeNoWritingQueries(Options);

        Assert.That(violations, Has.None.Contains(typeof(ReadingUpdatable).FullName!));
        Assert.That(violations, Has.None.Contains(typeof(HealthSystem).FullName!));
    }

    [Test]
    public void CodeOutsideSystemsTakingAUniqueOrItsWritableClassIsAViolation()
    {
        IReadOnlyList<string> violations = OrangeArchitecture.OutsideSystemsTakeNoWritableUniques(Options);
        const string reason = "it writes a unique; code outside systems reads a unique through the ReadOnlyUnique of its read-only type";

        Assert.That(violations, Has.One.EqualTo($"{typeof(UniqueUpdatable).FullName}: constructor takes Unique<Terrain> through terrain; {reason}"));
        Assert.That(violations, Has.One.EqualTo($"{typeof(TerrainRenderer).FullName}: constructor takes Terrain through terrain; {reason}"));
        Assert.That(violations, Has.One.EqualTo($"{typeof(TerrainView).FullName}: constructor takes Func<Terrain> through terrain; {reason}"));
        Assert.That(violations, Has.Count.EqualTo(3));
    }

    [Test]
    public void ReadOnlyUniqueOutsideSystemsAndUniqueInASystemAreFit()
    {
        IReadOnlyList<string> violations = OrangeArchitecture.OutsideSystemsTakeNoWritableUniques(Options);

        Assert.That(violations, Has.None.Contains(typeof(ReadOnlyUniqueRenderer).FullName!));
        Assert.That(violations, Has.None.Contains(typeof(TerrainSystem).FullName!));
    }

    [Test]
    public void ViewModelTakenByTwoSystemsIsAViolation()
    {
        Assert.That(OrangeArchitecture.ViewModelsHaveOneWriter(Options), Has.One.EqualTo($"{typeof(Model).FullName}: 2 systems or updatables take it (HealthSystem, SecondModelSystem); one of them writes each view model"));
    }

    [Test]
    public void ViewModelTakenThroughAnInterfaceNotExtendingIUiViewModelIsCounted()
    {
        Assert.That(OrangeArchitecture.ViewModelsHaveOneWriter(Options), Has.One.EqualTo($"{typeof(ShownModel).FullName}: 2 systems or updatables take it (ShownSystem, ShownModelSystem); one of them writes each view model"));
    }

    // A view model of state outside the world is written by an updatable, so an updatable counts as a writer.
    [Test]
    public void ViewModelTakenByASystemAndAnUpdatableIsAViolation()
    {
        Assert.That(OrangeArchitecture.ViewModelsHaveOneWriter(Options), Has.One.EqualTo($"{typeof(SharedModel).FullName}: 2 systems or updatables take it (SharedModelSystem, SharedModelUpdatable); one of them writes each view model"));
    }

    [Test]
    public void ViewModelWithOneWriterOrNoneIsFit()
    {
        IReadOnlyList<string> violations = OrangeArchitecture.ViewModelsHaveOneWriter(Options);

        Assert.That(violations, Has.None.Contains(typeof(OtherModel).FullName!));
        Assert.That(violations, Has.None.Contains(typeof(UnwrittenModel).FullName!));
        Assert.That(violations, Has.None.Contains(typeof(SettingsModel).FullName!));
    }

    [Test]
    public void VocabularyHoldsOnlyEnumsReadOnlyRecordStructsAndConstants()
    {
        IReadOnlyList<string> violations = OrangeArchitecture.VocabularyHoldsOnlyPrimitives(Options);

        Assert.That(violations, Has.None.Contains(typeof(Facing).FullName!));
        Assert.That(violations, Has.None.Contains(typeof(ItemId).FullName!));
        Assert.That(violations, Has.None.Contains(typeof(Limits).FullName!));
        Assert.That(violations, Has.One.StartsWith($"{typeof(MutableCoordinate).FullName}: Orangey.Vocabulary holds only enums, read-only record structs and static classes of constant values"));
        Assert.That(violations, Has.One.StartsWith($"{typeof(Thresholds).FullName}: "));
        Assert.That(violations, Has.One.StartsWith($"{typeof(Counters).FullName}: "));
        Assert.That(violations, Has.One.StartsWith($"{typeof(Formulas).FullName}: "));
        Assert.That(violations, Has.One.StartsWith($"{typeof(Helper).FullName}: "));
        Assert.That(violations, Has.None.Contains("Orangey.Vocabulary.Hidden"));
    }

    [Test]
    public void VocabularySubNamespaceIsCheckedToo()
    {
        IReadOnlyList<string> violations = OrangeArchitecture.VocabularyHoldsOnlyPrimitives(Options);

        Assert.That(violations, Has.One.StartsWith($"{typeof(Orangey.Vocabulary.Units.Converter).FullName}: Orangey.Vocabulary holds only"));
        Assert.That(violations, Has.None.Contains(typeof(Orangey.Vocabulary.Units.Meters).FullName!));
    }

    [Test]
    public void TypesInNamedNamespacesAndTheirSubNamespacesAreFit()
    {
        IReadOnlyList<string> violations = OrangeArchitecture.TypesAreInNamedNamespaces(Options);

        Assert.That(violations, Has.None.StartsWith("Orangey.Systems:"));
        Assert.That(violations, Has.None.StartsWith("Orangey.Systems.Flight:"));
        Assert.That(violations, Has.None.StartsWith("Orangey.WorldAccess:"));
        Assert.That(violations, Has.None.StartsWith("Orangey.Components:"));
        Assert.That(violations, Has.None.StartsWith("Orangey.Vocabulary.Units:"));
    }

    [Test]
    public void TypesInAnUnnamedNamespaceAreAViolationOncePerNamespace()
    {
        IReadOnlyList<string> violations = OrangeArchitecture.TypesAreInNamedNamespaces(Options);

        Assert.That(violations, Has.One.EqualTo("Orangey.Misc: not a namespace orange-architecture.md names; move its types, or list it in ExtraNamespaces with why none of the named ones fits"));
        Assert.That(violations, Has.One.StartsWith("Orangey.Systems2: "));
        Assert.That(violations, Has.One.StartsWith("Orangey.Tools: "));
        Assert.That(violations, Has.One.StartsWith("Pixely.Fitness.Tests: "));
    }

    [Test]
    public void AddedNamespaceIsFitOnlyWithAReasonAndTypes()
    {
        OrangeArchitectureOptions options = Options with
        {
            ExtraNamespaces = [new ExtraNamespace("Orangey.Tools", "cheats are neither game state nor UI"), new ExtraNamespace("Orangey.Misc", " "), new ExtraNamespace("Orangey.Empty", "reserved")]
        };

        IReadOnlyList<string> violations = OrangeArchitecture.TypesAreInNamedNamespaces(options);

        Assert.That(violations, Has.None.StartsWith("Orangey.Tools"));
        Assert.That(violations, Has.One.EqualTo("Orangey.Misc: listed in ExtraNamespaces without a reason"));
        Assert.That(violations, Has.None.StartsWith("Orangey.Misc: not a namespace"));
        Assert.That(violations, Has.One.EqualTo("Orangey.Empty: listed in ExtraNamespaces but holds no types"));
    }

    [Test]
    public void SystemsSharingAComponentWithoutADeclaredOrderAreAViolation()
    {
        IReadOnlyList<string> violations = OrangeArchitecture.SharedComponentsHaveADeclaredOrder(Options);

        Assert.That(violations, Has.One.EqualTo("Orangey.Systems.Minimap and Orangey.Systems.Movement: share Position, which at least one of them writes, and neither is ordered after the other with IAfter or IBefore, so the order of AddSystem calls decides"));
        Assert.That(violations, Has.One.StartsWith("Orangey.Systems.EnemyFuel and Orangey.Systems.Refueling: share Fuel, "));
        Assert.That(violations, Has.One.StartsWith("Orangey.Systems.Emitter and Orangey.Systems.Glowing: share Heat, Spark, "));
        Assert.That(violations, Has.One.StartsWith("Orangey.Systems.Erosion and Orangey.Systems.Terraform: share Ground, "));
        Assert.That(violations, Has.One.StartsWith("Orangey.Systems.Drying and Orangey.Systems.Soaking: share Wet, "));
    }

    [Test]
    public void SystemsOrderedDirectlyOrThroughOthersAreFit()
    {
        IReadOnlyList<string> violations = OrangeArchitecture.SharedComponentsHaveADeclaredOrder(Options);

        Assert.That(violations, Has.None.Contains("Orangey.Systems.Camera"));
        Assert.That(violations, Has.None.Contains("Orangey.Systems.Radar"));
        Assert.That(violations, Has.None.Contains("Orangey.Systems.Trail"));
        Assert.That(violations, Has.None.Contains("Orangey.Systems.PlayerFuel"));
        Assert.That(violations, Has.None.Contains("Orangey.Systems.Survey"));
        Assert.That(violations, Has.None.Contains("Orangey.Systems.Engines"));
    }

    [Test]
    public void SystemsThatCannotReachTheSameEntityAreFit()
    {
        IReadOnlyList<string> violations = OrangeArchitecture.SharedComponentsHaveADeclaredOrder(Options);

        Assert.That(violations, Has.None.Contains("Orangey.Systems.Cooling"));
        Assert.That(violations, Has.None.StartsWith("Orangey.Systems.EnemyFuel and Orangey.Systems.PlayerFuel"));
        Assert.That(violations, Has.None.Contains("Orangey.Systems.Trading"));
        Assert.That(violations, Has.None.Contains("Orangey.Systems.Lightning"));
        Assert.That(violations, Has.None.Contains("Orangey.Systems.Forecast"));
    }

    [Test]
    public void FilterMeetsOnlyAStructuralWriter()
    {
        IReadOnlyList<string> violations = OrangeArchitecture.SharedComponentsHaveADeclaredOrder(Options);

        Assert.That(violations, Has.None.Contains("Orangey.Systems.Weighing"));
    }

    [Test]
    public void ComponentHoldingAReferenceIsAViolation()
    {
        IReadOnlyList<string> violations = OrangeArchitecture.ComponentsHoldNoReferences(Options);

        Assert.That(violations, Has.One.EqualTo($"{typeof(Inventory).FullName}: Inventory.Items holds a reference, List<Int32>, which code reading the component can change; hold values or ids"));
        Assert.That(violations, Has.One.StartsWith($"{typeof(Wrapper).FullName}: Wrapper.Inner.Values holds a reference, Int32[]"));
        Assert.That(violations, Has.One.StartsWith($"{typeof(Cargo).FullName}: Cargo.Load holds a reference, Object"));
        Assert.That(violations, Has.One.StartsWith($"{typeof(Mass).FullName}: Mass.Samples holds a reference, List<Single>"));
    }

    [Test]
    public void ComponentHoldingValuesOrStringsIsFit()
    {
        IReadOnlyList<string> violations = OrangeArchitecture.ComponentsHoldNoReferences(Options);

        Assert.That(violations, Has.None.Contains(typeof(Health).FullName!));
        Assert.That(violations, Has.None.Contains(typeof(Named).FullName!));
    }

    [Test]
    public void EvaluateRunsEveryRule()
    {
        Assert.That(OrangeArchitecture.Evaluate(Options)["Orange ComponentsHoldNoReferences"].Violations, Is.EqualTo(OrangeArchitecture.ComponentsHoldNoReferences(Options)));
        Assert.That(OrangeArchitecture.Evaluate(Options).Results, Has.Count.EqualTo(9));
    }

    private readonly record struct Health(int Value);

    private record struct Named(string Name, int Value);

    private record struct Inventory(List<int> Items);

    private record struct Inner(int[] Values);

    private record struct Wrapper(Inner Inner);

    private record struct Cargo(object Load);

    private record struct Mass(List<float> Samples);

    private readonly record struct Ping;

    private sealed class WriteHealths : Query<Span<Health>>;

    private sealed class ReadHealths : Query<ReadOnlySpan<Health>>;

    private sealed class OptionalWriteHealths : Query<ReadOnlySpan<Named>, Optional<Span<Health>>>;

    private sealed class Inventories : Query<ReadOnlySpan<Inventory>>;

    private sealed class WrapperSpawner : Spawner<Wrapper>;

    private abstract class ComponentQuery<T> : Query<ReadOnlySpan<T>>;

    private sealed class Masses : ComponentQuery<Mass>;

    private interface IModel : IUiViewModel;

    private sealed class Model : IModel
    {
        public event Action? Changed { add { } remove { } }
    }

    private sealed class OtherModel : IUiViewModel
    {
        public event Action? Changed { add { } remove { } }
    }

    private sealed class UnwrittenModel : IUiViewModel
    {
        public event Action? Changed { add { } remove { } }
    }

    private sealed class SharedModel : IUiViewModel
    {
        public event Action? Changed { add { } remove { } }
    }

    private sealed class SettingsModel : IUiViewModel
    {
        public event Action? Changed { add { } remove { } }
    }

    private sealed class SharedModelSystem(SharedModel viewModel) : ISystem
    {
        public void Update()
        {
        }
    }

    private sealed class SharedModelUpdatable(SharedModel viewModel) : IUpdatable
    {
        public void Update()
        {
        }
    }

    private sealed class SettingsUpdatable(SettingsModel viewModel) : IUpdatable
    {
        public void Update()
        {
        }
    }

    private interface IShown;

    private sealed class ShownModel : IUiViewModel, IShown
    {
        public event Action? Changed { add { } remove { } }
    }

    private sealed class ShownSystem(IShown viewModel) : ISystem
    {
        public void Update()
        {
        }
    }

    private sealed class ShownModelSystem(ShownModel viewModel) : ISystem
    {
        public void Update()
        {
        }
    }

    private sealed class HealthSystem(WriteHealths healths, Model viewModel, ComponentAdder<Cargo> cargo) : ISystem
    {
        public void Update()
        {
        }
    }

    private sealed class SecondModelSystem(IModel viewModel, Inventories inventories) : ISystem
    {
        public void Update()
        {
        }
    }

    private sealed class OtherModelSystem(OtherModel viewModel) : ISystem
    {
        public void Update()
        {
        }
    }

    private abstract class TestView(Model viewModel) : UiView<Model>(viewModel)
    {
        protected override Element Build() => new Column();

        protected override void Sync()
        {
        }
    }

    private sealed class ModelView(Model viewModel) : TestView(viewModel);

    private sealed class QueryView(Model viewModel, ReadHealths healths) : TestView(viewModel);

    private sealed class LookupView(Model viewModel, ReadOnlyComponentLookup<Health> health) : TestView(viewModel);

    private sealed class SystemView(Model viewModel, HealthSystem system) : TestView(viewModel);

    private sealed class MessageView(Model viewModel, MessageWriter<Ping> pings, Func<MessageReader<Ping>> readers) : TestView(viewModel);

    private sealed class ReadingRenderer(MessageReader<Ping> pings) : IRenderer<object>
    {
        public void Render(object renderContext)
        {
        }
    }

    private sealed class UpdatingRenderer(MessageReader<Ping> pings) : IUpdatable, IRenderer<object>
    {
        public void Update()
        {
        }

        public void Render(object renderContext)
        {
        }
    }

    private sealed class WritingRenderer(MessageWriter<Ping> pings) : IRenderer<object>
    {
        public void Render(object renderContext)
        {
        }
    }

    private sealed class WritingUpdatable(WriteHealths healths) : IUpdatable
    {
        public void Update()
        {
        }
    }

    private sealed class ReadingUpdatable(ReadHealths healths) : IUpdatable
    {
        public void Update()
        {
        }
    }

    private sealed class OptionalWritingRenderer(OptionalWriteHealths healths) : IRenderer<object>
    {
        public void Render(object renderContext)
        {
        }
    }

    private interface IReadOnlyTerrain;

    private sealed class Terrain : IReadOnlyTerrain;

    private sealed class TerrainSystem(Unique<Terrain> terrain) : ISystem
    {
        public void Update()
        {
        }
    }

    private sealed class UniqueUpdatable(Unique<Terrain> terrain) : IUpdatable
    {
        public void Update()
        {
        }
    }

    private sealed class TerrainRenderer(Terrain terrain) : IRenderer<object>
    {
        public void Render(object renderContext)
        {
        }
    }

    private sealed class ReadOnlyUniqueRenderer(ReadOnlyUnique<IReadOnlyTerrain> terrain) : IRenderer<object>
    {
        public void Render(object renderContext)
        {
        }
    }

    private sealed class TerrainView(Model viewModel, Func<Terrain> terrain) : TestView(viewModel);
}
