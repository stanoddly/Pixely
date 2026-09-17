using System.Reflection;

namespace Pixely.Fitness;

/// <summary>A type that holds an owned GPU object on someone else's behalf, so GpuOwnersAreDisposable passes it over.</summary>
public sealed record GpuBorrower(Type Type, string Justification);

/// <summary>
/// The assemblies the Pixely conventions are checked in, whatever architecture they follow.
/// </summary>
public sealed record PixelyConventionsOptions(IReadOnlyList<Assembly> Assemblies)
{
    public PixelyConventionsOptions(params Assembly[] assemblies) : this((IReadOnlyList<Assembly>)assemblies)
    {
    }

    /// <summary>The production assemblies of a Peach game, none until GameResolvesFromPrefix resolved them.</summary>
    public static PixelyConventionsOptions ForPeach(PeachArchitectureOptions options)
    {
        return new PixelyConventionsOptions(options.Resolved.IsComplete ? options.ProductionAssemblies.ToArray() : []);
    }

    /// <summary>A type with a static Create returning itself hides its constructors.</summary>
    public bool FactoriesHideConstructors { get; init; } = true;

    /// <summary>Every constructor of a class deriving from UiView takes exactly one IUiViewModel.</summary>
    public bool ViewsTakeOneViewModel { get; init; } = true;

    /// <summary>A type holding a GPU object it owns disposes it.</summary>
    public bool GpuOwnersAreDisposable { get; init; } = true;

    /// <summary>The types GpuOwnersAreDisposable passes over; each needs a justification and must hold an owned GPU object.</summary>
    public IReadOnlyList<GpuBorrower> GpuBorrowers { get; init; } = [];

    /// <summary>
    /// Every updatable, renderer and event handler is registered by a registrar. Off by default: a delegate factory
    /// typed by an interface and a registration outside any registrar both hide the concrete type.
    /// </summary>
    public bool FrameParticipantsAreRegistered { get; init; } = false;
}
