using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using Pixely.Models.Gltf;

namespace Pixely.Models.Tests;

// Writes small GLB files in memory, so the tests need no binary fixtures. Tests edit Json directly for the cases they check.
internal sealed class GlbBuilder
{
    public const uint JsonChunk = 0x4E4F534A;
    public const uint BinaryChunk = 0x004E4942;

    private readonly List<byte> _binary = new();

    public JsonObject Json { get; } = new() { ["asset"] = new JsonObject { ["version"] = "2.0" } };

    public int AddBufferView(ReadOnlySpan<byte> data, int? byteStride = null)
    {
        while (_binary.Count % 4 != 0)
        {
            _binary.Add(0);
        }

        JsonObject bufferView = new() { ["buffer"] = 0, ["byteOffset"] = _binary.Count, ["byteLength"] = data.Length };
        if (byteStride is { } stride)
        {
            bufferView["byteStride"] = stride;
        }

        _binary.AddRange(data);
        return Add("bufferViews", bufferView);
    }

    public int AddAccessor(int? bufferView, int componentType, string type, int count, bool normalized = false, long? byteOffset = null)
    {
        JsonObject accessor = new() { ["componentType"] = componentType, ["type"] = type, ["count"] = count };
        if (bufferView is { } view)
        {
            accessor["bufferView"] = view;
        }

        if (normalized)
        {
            accessor["normalized"] = true;
        }

        if (byteOffset is { } offset)
        {
            accessor["byteOffset"] = offset;
        }

        return Add("accessors", accessor);
    }

    public int AddFloats(string type, params float[] values) =>
        AddAccessor(AddBufferView(MemoryMarshal.AsBytes<float>(values)), AccessorReader.Float, type, values.Length / ComponentCount(type));

    public int AddUShorts(string type, params ushort[] values) =>
        AddAccessor(AddBufferView(MemoryMarshal.AsBytes<ushort>(values)), AccessorReader.UnsignedShort, type, values.Length / ComponentCount(type));

    public int AddUInts(string type, params uint[] values) =>
        AddAccessor(AddBufferView(MemoryMarshal.AsBytes<uint>(values)), AccessorReader.UnsignedInt, type, values.Length / ComponentCount(type));

    public int AddBytes(string type, int componentType, bool normalized, params byte[] values) =>
        AddAccessor(AddBufferView(values), componentType, type, values.Length / ComponentCount(type) / (componentType is AccessorReader.Short or AccessorReader.UnsignedShort ? 2 : 1), normalized);

    public int Add(string arrayName, JsonNode item)
    {
        if (Json[arrayName] is not JsonArray array)
        {
            array = new JsonArray();
            Json[arrayName] = array;
        }

        array.Add(item);
        return array.Count - 1;
    }

    // One triangle in the XY plane facing +Z, in a mesh, a node and a default scene. Returns the primitive's JSON.
    public JsonObject AddTriangle(bool indexed = true)
    {
        int positions = AddFloats("VEC3", 0, 0, 0, 1, 0, 0, 0, 1, 0);
        int normals = AddFloats("VEC3", 0, 0, 1, 0, 0, 1, 0, 0, 1);
        JsonObject primitive = new() { ["attributes"] = new JsonObject { ["POSITION"] = positions, ["NORMAL"] = normals } };
        if (indexed)
        {
            primitive["indices"] = AddUShorts("SCALAR", 0, 1, 2);
        }

        int mesh = Add("meshes", new JsonObject { ["primitives"] = new JsonArray(primitive) });
        int node = Add("nodes", new JsonObject { ["mesh"] = mesh });
        Add("scenes", new JsonObject { ["nodes"] = new JsonArray(node) });
        Json["scene"] = 0;
        return primitive;
    }

    public byte[] Build()
    {
        bool hasBinary = _binary.Count > 0;
        if (hasBinary && Json["buffers"] is null)
        {
            Json["buffers"] = new JsonArray(new JsonObject { ["byteLength"] = _binary.Count });
        }

        List<(uint Type, byte[] Data)> chunks = [(JsonChunk, Encoding.UTF8.GetBytes(Json.ToJsonString()))];
        if (hasBinary)
        {
            chunks.Add((BinaryChunk, [.. _binary]));
        }

        return Container(chunks);
    }

    public ModelData Read() => GltfModelReader.Read(Build());

    // Writes a GLB container around the chunks, padding each to 4 bytes: JSON with spaces, others with zeros.
    public static byte[] Container(IEnumerable<(uint Type, byte[] Data)> chunks, uint version = 2, int lengthAdjustment = 0)
    {
        List<byte> file = new(new byte[12]);
        foreach ((uint type, byte[] data) in chunks)
        {
            byte padding = type == JsonChunk ? (byte)' ' : (byte)0;
            int paddedLength = (data.Length + 3) / 4 * 4;
            byte[] header = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)paddedLength);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), type);
            file.AddRange(header);
            file.AddRange(data);
            file.AddRange(Enumerable.Repeat(padding, paddedLength - data.Length));
        }

        byte[] result = [.. file];
        BinaryPrimitives.WriteUInt32LittleEndian(result, 0x46546C67);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), version);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), (uint)(result.Length + lengthAdjustment));
        return result;
    }

    private static int ComponentCount(string type) => type switch
    {
        "SCALAR" => 1,
        "VEC2" => 2,
        "VEC3" => 3,
        "VEC4" => 4,
        "MAT4" => 16,
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };
}
