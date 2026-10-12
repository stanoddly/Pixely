using System.Collections.Immutable;

namespace Pixely.Models;

// Holds the GPU resources a load creates until the Model takes them. A load that throws disposes the scope, which releases them,
// so a failed load leaves nothing registered with the device.
internal sealed class ResourceScope : IDisposable
{
    private readonly List<IDisposable> _resources = new();
    private bool _released;

    public T Add<T>(T resource) where T : IDisposable
    {
        _resources.Add(resource);
        return resource;
    }

    // Hands the resources to their new owner; disposing the scope afterwards does nothing.
    public ImmutableArray<IDisposable> Release()
    {
        _released = true;
        return [.. _resources];
    }

    public void Dispose()
    {
        if (!_released)
        {
            _released = true;
            DisposeInReverse(_resources);
        }
    }

    public static void DisposeInReverse(IReadOnlyList<IDisposable> resources)
    {
        for (int i = resources.Count - 1; i >= 0; i--)
        {
            resources[i].Dispose();
        }
    }
}
