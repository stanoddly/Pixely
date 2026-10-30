using System.Collections.Immutable;
using Pixely.Gpu;

namespace Pixely.Testing;

// Binds consecutive slots from the first one on, the way the stub's render and compute passes keep their bindings.
internal static class StubBindings
{
    internal static ImmutableDictionary<uint, StubSamplerBinding> BindSamplers(ImmutableDictionary<uint, StubSamplerBinding> bound, uint slot, ReadOnlySpan<Texture> textures, Sampler sampler)
    {
        for (int i = 0; i < textures.Length; i++)
        {
            bound = bound.SetItem(slot + (uint)i, new StubSamplerBinding(textures[i], sampler));
        }

        return bound;
    }

    internal static ImmutableDictionary<uint, T> Bind<T>(ImmutableDictionary<uint, T> bound, uint slot, ReadOnlySpan<T> resources)
    {
        for (int i = 0; i < resources.Length; i++)
        {
            bound = bound.SetItem(slot + (uint)i, resources[i]);
        }

        return bound;
    }
}
