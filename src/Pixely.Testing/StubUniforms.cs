using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pixely.Testing;

// The uniform data of one shader stage, per slot, as it was pushed. Pushing makes a new instance, so a recorded draw keeps
// the uniforms of its own moment by holding on to the current one.
internal sealed class StubUniforms
{
    // The slots SDL GPU has per stage.
    private const int SlotCount = 4;

    public static readonly StubUniforms Empty = new(new byte[]?[SlotCount]);

    private readonly byte[]?[] _slots;

    private StubUniforms(byte[]?[] slots)
    {
        _slots = slots;
    }

    public StubUniforms With(uint slot, ReadOnlySpan<byte> data)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot, (uint)SlotCount);
        byte[]?[] slots = (byte[]?[])_slots.Clone();
        slots[slot] = data.ToArray();
        return new StubUniforms(slots);
    }

    public bool Contains(uint slot) => slot < SlotCount && _slots[slot] != null;

    public T Read<T>(uint slot, string stage) where T : unmanaged
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot, (uint)SlotCount);
        byte[] data = _slots[slot] ?? throw new InvalidOperationException($"Nothing was pushed to {stage} uniform slot {slot}.");
        if (data.Length != Unsafe.SizeOf<T>())
        {
            throw new InvalidOperationException($"{stage} uniform slot {slot} holds {data.Length} bytes, but {typeof(T).Name} has {Unsafe.SizeOf<T>()}.");
        }

        return MemoryMarshal.Read<T>(data);
    }
}
