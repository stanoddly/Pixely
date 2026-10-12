using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Pixely.Gpu;
using Pixely.Models.Gltf;

namespace Pixely.Models.Tests;

public class AccessorTests
{
    [Test]
    public void Read_InterleavedAttributes_UseTheByteStride()
    {
        GlbBuilder builder = new();
        float[] interleaved =
        [
            0, 0, 0, 0, 0, 1,
            1, 0, 0, 0, 0, 1,
            0, 1, 0, 0, 0, 1
        ];
        int view = builder.AddBufferView(MemoryMarshal.AsBytes<float>(interleaved), byteStride: 24);
        int positions = builder.AddAccessor(view, AccessorReader.Float, "VEC3", 3);
        int normals = builder.AddAccessor(view, AccessorReader.Float, "VEC3", 3, byteOffset: 12);
        AddPrimitive(builder, new JsonObject { ["POSITION"] = positions, ["NORMAL"] = normals });

        PositionTextureNormalVertex[] vertices = builder.Read().Meshes[0].Primitives[0].Vertices;

        Assert.That(vertices.Select(vertex => vertex.Position), Is.EqualTo(new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY }));
        Assert.That(vertices.Select(vertex => vertex.Normal), Is.All.EqualTo(Vector3.UnitZ));
    }

    [Test]
    public void Read_NormalizedUnsignedByteTexCoords_AreConvertedToFloats()
    {
        GlbBuilder builder = new();
        JsonObject primitive = builder.AddTriangle();
        // Vertex attribute elements start on 4-byte boundaries, so 2-byte elements need a stride of 4.
        int view = builder.AddBufferView(new byte[] { 0, 255, 0, 0, 51, 102, 0, 0, 255, 0, 0, 0 }, byteStride: 4);
        primitive["attributes"]!["TEXCOORD_0"] = builder.AddAccessor(view, AccessorReader.UnsignedByte, "VEC2", 3, normalized: true);

        PositionTextureNormalVertex[] vertices = builder.Read().Meshes[0].Primitives[0].Vertices;

        Assert.That(vertices[0].TextureCoords, Is.EqualTo(new Vector2(0, 1)));
        Assert.That(vertices[1].TextureCoords.X, Is.EqualTo(0.2f).Within(1e-6f));
        Assert.That(vertices[1].TextureCoords.Y, Is.EqualTo(0.4f).Within(1e-6f));
    }

    [Test]
    public void Read_NormalizedUnsignedShortTexCoords_AreConvertedToFloats()
    {
        GlbBuilder builder = new();
        JsonObject primitive = builder.AddTriangle();
        int view = builder.AddBufferView(MemoryMarshal.AsBytes<ushort>(new ushort[] { 65535, 0, 0, 65535, 0, 0 }));
        primitive["attributes"]!["TEXCOORD_0"] = builder.AddAccessor(view, AccessorReader.UnsignedShort, "VEC2", 3, normalized: true);

        PositionTextureNormalVertex[] vertices = builder.Read().Meshes[0].Primitives[0].Vertices;

        Assert.That(vertices[0].TextureCoords, Is.EqualTo(new Vector2(1, 0)));
        Assert.That(vertices[1].TextureCoords, Is.EqualTo(new Vector2(0, 1)));
    }

    [Test]
    public void Read_MissingTexCoords_AreZero()
    {
        GlbBuilder builder = new();
        builder.AddTriangle();

        Assert.That(builder.Read().Meshes[0].Primitives[0].Vertices.Select(vertex => vertex.TextureCoords), Is.All.EqualTo(Vector2.Zero));
    }

    [Test]
    public void Read_UnsignedByteIndices_AreWidenedTo16Bits()
    {
        GlbBuilder builder = new();
        JsonObject primitive = builder.AddTriangle();
        primitive["indices"] = builder.AddBytes("SCALAR", AccessorReader.UnsignedByte, false, 2, 1, 0, 0);
        builder.Json["accessors"]![(int)primitive["indices"]!]!["count"] = 3;

        PrimitiveData data = builder.Read().Meshes[0].Primitives[0];

        Assert.That(data.Indices16, Is.EqualTo(new ushort[] { 2, 1, 0 }));
        Assert.That(data.Indices32, Is.Null);
    }

    [Test]
    public void Read_UnsignedIntIndices_Stay32Bits()
    {
        GlbBuilder builder = new();
        JsonObject primitive = builder.AddTriangle();
        primitive["indices"] = builder.AddUInts("SCALAR", 0, 2, 1);

        PrimitiveData data = builder.Read().Meshes[0].Primitives[0];

        Assert.That(data.Indices32, Is.EqualTo(new uint[] { 0, 2, 1 }));
        Assert.That(data.Indices16, Is.Null);
    }

    [Test]
    public void Read_CountLargerThanBufferView_Throws()
    {
        GlbBuilder builder = new();
        JsonObject primitive = builder.AddTriangle();
        builder.Json["accessors"]![(int)primitive["attributes"]!["POSITION"]!]!["count"] = 4;

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_ByteOffsetPastBufferView_Throws()
    {
        GlbBuilder builder = new();
        JsonObject primitive = builder.AddTriangle();
        builder.Json["accessors"]![(int)primitive["attributes"]!["POSITION"]!]!["byteOffset"] = 4;

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_HugeByteOffset_Throws()
    {
        GlbBuilder builder = new();
        JsonObject primitive = builder.AddTriangle();
        builder.Json["bufferViews"]![0]!["byteOffset"] = long.MaxValue - 3;

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_ZeroCount_Throws()
    {
        GlbBuilder builder = new();
        JsonObject primitive = builder.AddTriangle();
        builder.Json["accessors"]![(int)primitive["attributes"]!["POSITION"]!]!["count"] = 0;

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_MisalignedByteOffset_Throws()
    {
        GlbBuilder builder = new();
        JsonObject primitive = builder.AddTriangle();
        builder.Json["accessors"]![(int)primitive["attributes"]!["POSITION"]!]!["byteOffset"] = 2;

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_VertexAttributeNotOnFourByteBoundary_Throws()
    {
        GlbBuilder builder = new();
        JsonObject primitive = builder.AddTriangle();
        int view = builder.AddBufferView(new byte[16]);
        primitive["attributes"]!["TEXCOORD_0"] = builder.AddAccessor(view, AccessorReader.UnsignedShort, "VEC2", 3, normalized: true, byteOffset: 2);

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [TestCase(3)]
    [TestCase(256)]
    [TestCase(8)]
    public void Read_InvalidByteStride_Throws(int byteStride)
    {
        GlbBuilder builder = new();
        JsonObject primitive = builder.AddTriangle();
        builder.Json["bufferViews"]![(int)builder.Json["accessors"]![(int)primitive["attributes"]!["POSITION"]!]!["bufferView"]!]!["byteStride"] = byteStride;

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_NormalizedFloat_Throws()
    {
        GlbBuilder builder = new();
        JsonObject primitive = builder.AddTriangle();
        builder.Json["accessors"]![(int)primitive["attributes"]!["POSITION"]!]!["normalized"] = true;

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_WrongTypeForUse_Throws()
    {
        GlbBuilder builder = new();
        JsonObject primitive = builder.AddTriangle();
        builder.Json["accessors"]![(int)primitive["attributes"]!["POSITION"]!]!["type"] = "VEC4";

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_NormalizedJoints_Throws()
    {
        GlbBuilder builder = new();
        JsonObject primitive = builder.AddTriangle();
        primitive["attributes"]!["JOINTS_0"] = builder.AddBytes("VEC4", AccessorReader.UnsignedByte, true, new byte[12]);
        primitive["attributes"]!["WEIGHTS_0"] = builder.AddFloats("VEC4", 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0);

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_AccessorIndexOutOfRange_Throws()
    {
        GlbBuilder builder = new();
        JsonObject primitive = builder.AddTriangle();
        primitive["attributes"]!["NORMAL"] = 99;

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_BufferViewIndexOutOfRange_Throws()
    {
        GlbBuilder builder = new();
        JsonObject primitive = builder.AddTriangle();
        builder.Json["accessors"]![(int)primitive["attributes"]!["POSITION"]!]!["bufferView"] = 99;

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_AccessorWithoutBufferViewButWithByteOffset_Throws()
    {
        GlbBuilder builder = new();
        JsonObject primitive = builder.AddTriangle();
        primitive["attributes"]!["TEXCOORD_0"] = builder.AddAccessor(null, AccessorReader.Float, "VEC2", 3, byteOffset: 0);

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_AccessorWithoutBufferView_ReadsZeros()
    {
        GlbBuilder builder = new();
        JsonObject primitive = builder.AddTriangle();
        primitive["attributes"]!["TEXCOORD_0"] = builder.AddAccessor(null, AccessorReader.Float, "VEC2", 3);

        Assert.That(builder.Read().Meshes[0].Primitives[0].Vertices.Select(vertex => vertex.TextureCoords), Is.All.EqualTo(Vector2.Zero));
    }

    [Test]
    public void Read_ZeroFilledAccessorLargerThanFile_Throws()
    {
        GlbBuilder builder = new();
        JsonObject primitive = builder.AddTriangle();
        primitive["attributes"]!["TEXCOORD_0"] = builder.AddAccessor(null, AccessorReader.Float, "VEC2", int.MaxValue);

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_SparseAccessor_IsNotSupported()
    {
        GlbBuilder builder = new();
        JsonObject primitive = builder.AddTriangle();
        builder.Json["accessors"]![(int)primitive["attributes"]!["POSITION"]!]!["sparse"] = new JsonObject { ["count"] = 1 };

        Assert.Throws<NotSupportedException>(() => builder.Read());
    }

    internal static void AddPrimitive(GlbBuilder builder, JsonObject attributes)
    {
        builder.Add("meshes", new JsonObject { ["primitives"] = new JsonArray(new JsonObject { ["attributes"] = attributes }) });
    }
}
