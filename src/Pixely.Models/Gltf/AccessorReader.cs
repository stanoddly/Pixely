using System.Buffers.Binary;

namespace Pixely.Models.Gltf;

internal enum AccessorUse
{
    Position,
    Normal,
    TexCoord,
    Joints,
    Weights,
    Indices,
    InverseBindMatrices,
    AnimationInput,
    TranslationOrScaleOutput,
    RotationOutput
}

// Reads accessors into arrays (glTF 2.0, section 3.6). Every read checks the accessor against its use and its bytes against the
// buffer view. Reads are cached, and the arrays they return together are limited to a multiple of the file length, so accessors
// that alias the same bytes cannot multiply allocations. Callers must not modify the returned arrays.
internal sealed class AccessorReader
{
    // One byte of a normalized or integer component converts to a 4-byte float or uint, and an accessor can serve more than one use.
    public const int ConversionBudgetFactor = 16;

    public const int Byte = 5120;
    public const int UnsignedByte = 5121;
    public const int Short = 5122;
    public const int UnsignedShort = 5123;
    public const int UnsignedInt = 5125;
    public const int Float = 5126;

    private readonly GltfRootDto _root;
    private readonly ReadOnlyMemory<byte> _buffer;
    private readonly Dictionary<(int Accessor, AccessorUse Use), float[]> _floats = new();
    private readonly Dictionary<(int Accessor, AccessorUse Use), IntegerAccessorData> _integers = new();
    private long _remainingBudget;

    public AccessorReader(GltfRootDto root, ReadOnlyMemory<byte> buffer, long fileLength)
    {
        _root = root;
        _buffer = buffer;
        _remainingBudget = checked(fileLength * ConversionBudgetFactor);
    }

    public float[] ReadFloats(int accessorIndex, AccessorUse use)
    {
        if (_floats.TryGetValue((accessorIndex, use), out float[]? cached))
        {
            return cached;
        }

        ResolvedAccessor accessor = Resolve(accessorIndex, use);
        float[] values = new float[Charge(accessorIndex, accessor)];
        _floats.Add((accessorIndex, use), values);
        if (accessor.Data.IsEmpty)
        {
            return values;
        }

        // A value that is not finite fails the whole load, so a partly filled array in the cache is never read.
        ReadOnlySpan<byte> data = accessor.Data.Span;
        int componentSize = GetComponentSize(accessor.ComponentType);
        for (int element = 0; element < accessor.Count; element++)
        {
            int elementOffset = element * accessor.Stride;
            for (int component = 0; component < accessor.ComponentCount; component++)
            {
                ReadOnlySpan<byte> bytes = data[(elementOffset + component * componentSize)..];
                float value = accessor.ComponentType switch
                {
                    Float => BinaryPrimitives.ReadSingleLittleEndian(bytes),
                    // Normalized integers convert as section 3.11 defines; signed values clamp at -1.
                    Byte => MathF.Max((sbyte)bytes[0] / 127f, -1f),
                    UnsignedByte => bytes[0] / 255f,
                    Short => MathF.Max(BinaryPrimitives.ReadInt16LittleEndian(bytes) / 32767f, -1f),
                    UnsignedShort => BinaryPrimitives.ReadUInt16LittleEndian(bytes) / 65535f,
                    _ => throw new InvalidDataException($"accessors[{accessorIndex}]: component type {accessor.ComponentType} cannot be read as floats.")
                };

                if (!float.IsFinite(value))
                {
                    throw new InvalidDataException($"accessors[{accessorIndex}]: element {element} has a value that is not finite.");
                }

                values[element * accessor.ComponentCount + component] = value;
            }
        }

        return values;
    }

    public IntegerAccessorData ReadIntegers(int accessorIndex, AccessorUse use)
    {
        if (_integers.TryGetValue((accessorIndex, use), out IntegerAccessorData cached))
        {
            return cached;
        }

        ResolvedAccessor accessor = Resolve(accessorIndex, use);
        uint[] values = new uint[Charge(accessorIndex, accessor)];
        if (!accessor.Data.IsEmpty)
        {
            ReadOnlySpan<byte> data = accessor.Data.Span;
            int componentSize = GetComponentSize(accessor.ComponentType);
            for (int element = 0; element < accessor.Count; element++)
            {
                int elementOffset = element * accessor.Stride;
                for (int component = 0; component < accessor.ComponentCount; component++)
                {
                    ReadOnlySpan<byte> bytes = data[(elementOffset + component * componentSize)..];
                    values[element * accessor.ComponentCount + component] = accessor.ComponentType switch
                    {
                        UnsignedByte => bytes[0],
                        UnsignedShort => BinaryPrimitives.ReadUInt16LittleEndian(bytes),
                        UnsignedInt => BinaryPrimitives.ReadUInt32LittleEndian(bytes),
                        _ => throw new InvalidDataException($"accessors[{accessorIndex}]: component type {accessor.ComponentType} cannot be read as integers.")
                    };
                }
            }
        }

        IntegerAccessorData result = new(values, accessor.ComponentType);
        _integers.Add((accessorIndex, use), result);
        return result;
    }

    // Takes the 4-byte values of the accessor out of the budget and returns their count.
    private int Charge(int accessorIndex, ResolvedAccessor accessor)
    {
        int count = checked(accessor.Count * accessor.ComponentCount);
        long byteCount = count * 4L;
        if (byteCount > _remainingBudget)
        {
            throw new InvalidDataException($"accessors[{accessorIndex}]: the file's accessors convert to more than {ConversionBudgetFactor} times the file's size.");
        }

        _remainingBudget -= byteCount;
        return count;
    }

    private ResolvedAccessor Resolve(int accessorIndex, AccessorUse? use)
    {
        GltfAccessorDto[] accessors = _root.Accessors ?? [];
        if (accessorIndex < 0 || accessorIndex >= accessors.Length)
        {
            throw new InvalidDataException($"Accessor index {accessorIndex} is out of range; the file has {accessors.Length} accessors.");
        }

        GltfAccessorDto accessor = accessors[accessorIndex];
        string name = $"accessors[{accessorIndex}]";

        if (accessor.Sparse is not null)
        {
            throw new NotSupportedException($"{name}: sparse accessors are not supported.");
        }

        int componentType = accessor.ComponentType ?? throw new InvalidDataException($"{name}: componentType is missing.");
        int componentSize = GetComponentSize(componentType);
        if (componentSize == 0)
        {
            throw new InvalidDataException($"{name}: componentType {componentType} is not a glTF component type.");
        }

        int componentCount = accessor.Type switch
        {
            "SCALAR" => 1,
            "VEC2" => 2,
            "VEC3" => 3,
            "VEC4" => 4,
            "MAT2" => 4,
            "MAT3" => 9,
            "MAT4" => 16,
            _ => throw new InvalidDataException($"{name}: type '{accessor.Type}' is not a glTF accessor type.")
        };

        bool normalized = accessor.Normalized ?? false;
        if (normalized && componentType is UnsignedInt or Float)
        {
            throw new InvalidDataException($"{name}: normalized is only allowed for byte and short component types.");
        }

        if (use is { } accessorUse)
        {
            CheckUse(name, accessorUse, accessor.Type!, componentType, normalized);
        }

        long count = accessor.Count ?? throw new InvalidDataException($"{name}: count is missing.");
        if (count < 1 || count > int.MaxValue)
        {
            throw new InvalidDataException($"{name}: count {count} is out of range.");
        }

        // MAT2 and MAT3 of byte or short components have column padding, but no use allows them, so the element is packed.
        int elementSize = componentSize * componentCount;

        if (accessor.BufferView is not { } bufferViewIndex)
        {
            if (accessor.ByteOffset is not null)
            {
                throw new InvalidDataException($"{name}: byteOffset is defined without a bufferView.");
            }

            // An accessor without a buffer view reads as zeros. Its count is not backed by any bytes; the conversion budget bounds it.
            return new ResolvedAccessor(ReadOnlyMemory<byte>.Empty, (int)count, componentCount, componentType, elementSize);
        }

        GltfBufferViewDto[] bufferViews = _root.BufferViews ?? [];
        if (bufferViewIndex < 0 || bufferViewIndex >= bufferViews.Length)
        {
            throw new InvalidDataException($"{name}: bufferView {bufferViewIndex} is out of range.");
        }

        ReadOnlyMemory<byte> bufferView = GetBufferView(bufferViewIndex, out int? byteStride);
        long accessorOffset = accessor.ByteOffset ?? 0;
        long bufferViewOffset = bufferViews[bufferViewIndex].ByteOffset ?? 0;

        if (accessorOffset < 0 || accessorOffset % componentSize != 0 || (bufferViewOffset + accessorOffset) % componentSize != 0)
        {
            throw new InvalidDataException($"{name}: byteOffset {accessorOffset} is not aligned to its {componentSize}-byte components.");
        }

        int stride = byteStride ?? elementSize;
        if (stride < elementSize)
        {
            throw new InvalidDataException($"{name}: the buffer view's byteStride {stride} is smaller than the {elementSize}-byte element.");
        }

        if (use is AccessorUse.Position or AccessorUse.Normal or AccessorUse.TexCoord or AccessorUse.Joints or AccessorUse.Weights
            && ((bufferViewOffset + accessorOffset) % 4 != 0 || stride % 4 != 0))
        {
            throw new InvalidDataException($"{name}: vertex attribute elements must start on 4-byte boundaries.");
        }

        long end = checked(accessorOffset + stride * (count - 1) + elementSize);
        if (end > bufferView.Length)
        {
            throw new InvalidDataException($"{name}: its elements end at byte {end}, past the {bufferView.Length}-byte buffer view.");
        }

        return new ResolvedAccessor(bufferView.Slice((int)accessorOffset, (int)(end - accessorOffset)), (int)count, componentCount, componentType, stride);
    }

    public ReadOnlyMemory<byte> GetBufferView(int bufferViewIndex, out int? byteStride)
    {
        GltfBufferViewDto[] bufferViews = _root.BufferViews ?? [];
        if (bufferViewIndex < 0 || bufferViewIndex >= bufferViews.Length)
        {
            throw new InvalidDataException($"Buffer view index {bufferViewIndex} is out of range; the file has {bufferViews.Length} buffer views.");
        }

        GltfBufferViewDto bufferView = bufferViews[bufferViewIndex];
        string name = $"bufferViews[{bufferViewIndex}]";

        // GltfModelReader allows at most buffer 0, stored in the binary chunk, whose length it has checked against the chunk.
        int bufferCount = _root.Buffers?.Length ?? 0;
        if (bufferView.Buffer is not { } buffer || buffer < 0 || buffer >= bufferCount)
        {
            throw new InvalidDataException($"{name}: buffer {bufferView.Buffer} is out of range; the file has {bufferCount} buffers.");
        }

        long offset = bufferView.ByteOffset ?? 0;
        long length = bufferView.ByteLength ?? throw new InvalidDataException($"{name}: byteLength is missing.");
        long bufferLength = _root.Buffers![0].ByteLength!.Value;
        if (offset < 0 || length < 1 || checked(offset + length) > bufferLength)
        {
            throw new InvalidDataException($"{name}: bytes {offset} to {offset + length} are outside the {bufferLength}-byte buffer.");
        }

        byteStride = bufferView.ByteStride;
        if (byteStride is { } stride && (stride < 4 || stride > 252 || stride % 4 != 0))
        {
            throw new InvalidDataException($"{name}: byteStride {stride} is not a multiple of 4 between 4 and 252.");
        }

        return _buffer.Slice((int)offset, (int)length);
    }

    private static void CheckUse(string name, AccessorUse use, string type, int componentType, bool normalized)
    {
        bool allowed = use switch
        {
            AccessorUse.Position or AccessorUse.Normal or AccessorUse.TranslationOrScaleOutput => type == "VEC3" && componentType == Float,
            AccessorUse.TexCoord => type == "VEC2" && (componentType == Float || (normalized && componentType is UnsignedByte or UnsignedShort)),
            AccessorUse.Joints => type == "VEC4" && !normalized && componentType is UnsignedByte or UnsignedShort,
            AccessorUse.Weights => type == "VEC4" && (componentType == Float || (normalized && componentType is UnsignedByte or UnsignedShort)),
            AccessorUse.Indices => type == "SCALAR" && !normalized && componentType is UnsignedByte or UnsignedShort or UnsignedInt,
            AccessorUse.InverseBindMatrices => type == "MAT4" && componentType == Float,
            AccessorUse.AnimationInput => type == "SCALAR" && componentType == Float,
            AccessorUse.RotationOutput => type == "VEC4" && (componentType == Float || (normalized && componentType is Byte or UnsignedByte or Short or UnsignedShort)),
            _ => false
        };

        if (!allowed)
        {
            string normalizedText = normalized ? "normalized " : "";
            throw new InvalidDataException($"{name}: a {normalizedText}{type} of component type {componentType} cannot be used as {use}.");
        }
    }

    private static int GetComponentSize(int componentType) => componentType switch
    {
        Byte or UnsignedByte => 1,
        Short or UnsignedShort => 2,
        UnsignedInt or Float => 4,
        _ => 0
    };

    private readonly record struct ResolvedAccessor(ReadOnlyMemory<byte> Data, int Count, int ComponentCount, int ComponentType, int Stride);
}

internal readonly record struct IntegerAccessorData(uint[] Values, int ComponentType);
