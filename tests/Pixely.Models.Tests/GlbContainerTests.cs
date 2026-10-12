using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using Pixely.Models.Gltf;

namespace Pixely.Models.Tests;

public class GlbContainerTests
{
    private static readonly byte[] _minimalJson = Encoding.UTF8.GetBytes("""{"asset":{"version":"2.0"}}""");

    [Test]
    public void Read_MinimalFile_HasNoNodes()
    {
        ModelData data = GltfModelReader.Read(GlbBuilder.Container([(GlbBuilder.JsonChunk, _minimalJson)]));

        Assert.That(data.Nodes, Is.Empty);
    }

    [TestCase("nodes")]
    [TestCase("meshes")]
    [TestCase("accessors")]
    [TestCase("bufferViews")]
    [TestCase("buffers")]
    public void Read_NullArrayElement_Throws(string arrayName)
    {
        GlbBuilder builder = new();
        builder.AddTriangle();
        builder.Build();
        builder.Json[arrayName]!.AsArray().Add(null);

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_NullPrimitive_Throws()
    {
        GlbBuilder builder = new();
        builder.AddTriangle();
        builder.Json["meshes"]![0]!["primitives"]!.AsArray().Add(null);

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_BufferViewWithoutBuffers_Throws()
    {
        GlbBuilder builder = new();
        builder.AddTriangle();
        builder.Json["buffers"] = new JsonArray();

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_ShorterThanHeader_Throws()
    {
        Assert.Throws<InvalidDataException>(() => GltfModelReader.Read(new byte[8]));
    }

    [Test]
    public void Read_WrongMagic_Throws()
    {
        byte[] file = GlbBuilder.Container([(GlbBuilder.JsonChunk, _minimalJson)]);
        file[0] = (byte)'x';

        Assert.Throws<InvalidDataException>(() => GltfModelReader.Read(file));
    }

    [Test]
    public void Read_WrongVersion_Throws()
    {
        Assert.Throws<InvalidDataException>(() => GltfModelReader.Read(GlbBuilder.Container([(GlbBuilder.JsonChunk, _minimalJson)], version: 1)));
    }

    [TestCase(-4)]
    [TestCase(4)]
    public void Read_HeaderLengthDiffersFromFile_Throws(int lengthAdjustment)
    {
        Assert.Throws<InvalidDataException>(() => GltfModelReader.Read(GlbBuilder.Container([(GlbBuilder.JsonChunk, _minimalJson)], lengthAdjustment: lengthAdjustment)));
    }

    [Test]
    public void Read_ChunkLongerThanFile_Throws()
    {
        byte[] file = GlbBuilder.Container([(GlbBuilder.JsonChunk, _minimalJson)]);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(12), 1024);

        Assert.Throws<InvalidDataException>(() => GltfModelReader.Read(file));
    }

    [Test]
    public void Read_TruncatedChunkHeader_Throws()
    {
        byte[] file = [.. GlbBuilder.Container([(GlbBuilder.JsonChunk, _minimalJson)]), 0, 0, 0, 0];
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(8), (uint)file.Length);

        Assert.Throws<InvalidDataException>(() => GltfModelReader.Read(file));
    }

    [Test]
    public void Read_ChunkLengthNotMultipleOfFour_Throws()
    {
        byte[] file = GlbBuilder.Container([(GlbBuilder.JsonChunk, _minimalJson)]);
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(12));
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(12), length - 1);

        Assert.Throws<InvalidDataException>(() => GltfModelReader.Read(file));
    }

    [Test]
    public void Read_FirstChunkNotJson_Throws()
    {
        Assert.Throws<InvalidDataException>(() => GltfModelReader.Read(GlbBuilder.Container([(GlbBuilder.BinaryChunk, new byte[4]), (GlbBuilder.JsonChunk, _minimalJson)])));
    }

    [Test]
    public void Read_BinaryChunkNotSecond_Throws()
    {
        Assert.Throws<InvalidDataException>(() => GltfModelReader.Read(GlbBuilder.Container(
            [(GlbBuilder.JsonChunk, _minimalJson), (0x12345678, new byte[4]), (GlbBuilder.BinaryChunk, new byte[4])])));
    }

    [Test]
    public void Read_SecondJsonChunk_Throws()
    {
        Assert.Throws<InvalidDataException>(() => GltfModelReader.Read(GlbBuilder.Container([(GlbBuilder.JsonChunk, _minimalJson), (GlbBuilder.JsonChunk, _minimalJson)])));
    }

    [Test]
    public void Read_UnknownChunk_IsSkipped()
    {
        GlbBuilder builder = new();
        builder.AddTriangle();
        byte[] valid = builder.Build();
        byte[] withUnknownChunk = [.. valid, .. new byte[] { 4, 0, 0, 0, 0x78, 0x56, 0x34, 0x12, 1, 2, 3, 4 }];
        BinaryPrimitives.WriteUInt32LittleEndian(withUnknownChunk.AsSpan(8), (uint)withUnknownChunk.Length);

        Assert.That(GltfModelReader.Read(withUnknownChunk).Meshes, Has.Length.EqualTo(1));
    }

    [Test]
    public void Read_NoBinaryChunkAndAccessorWithoutBufferView_ReadsZeros()
    {
        GlbBuilder builder = new();
        int positions = builder.AddAccessor(null, AccessorReader.Float, "VEC3", 3);
        int normals = builder.AddAccessor(null, AccessorReader.Float, "VEC3", 3);
        builder.Add("meshes", new JsonObject { ["primitives"] = new JsonArray(new JsonObject { ["attributes"] = new JsonObject { ["POSITION"] = positions, ["NORMAL"] = normals } }) });

        // The zero normals are rejected, which shows the zero-filled accessor was read, not skipped.
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => builder.Read())!;
        Assert.That(exception.Message, Does.Contain("zero length"));
    }

    [Test]
    public void Read_BufferWithoutBinaryChunk_Throws()
    {
        GlbBuilder builder = new();
        builder.Json["buffers"] = new JsonArray(new JsonObject { ["byteLength"] = 4 });

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_BinaryChunkShorterThanBuffer_Throws()
    {
        GlbBuilder builder = new();
        builder.AddTriangle();
        builder.Json["buffers"] = new JsonArray(new JsonObject { ["byteLength"] = 4096 });

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_InvalidJson_Throws()
    {
        Assert.Throws<InvalidDataException>(() => GltfModelReader.Read(GlbBuilder.Container([(GlbBuilder.JsonChunk, Encoding.UTF8.GetBytes("{\"asset\":"))])));
    }

    [Test]
    public void Read_MissingAssetVersion_Throws()
    {
        GlbBuilder builder = new();
        builder.Json["asset"] = new JsonObject();

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [TestCase("2")]
    [TestCase("2.0.1")]
    [TestCase("two.zero")]
    public void Read_MalformedAssetVersion_Throws(string version)
    {
        GlbBuilder builder = new();
        builder.Json["asset"] = new JsonObject { ["version"] = version };

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_AssetVersionThree_IsNotSupported()
    {
        GlbBuilder builder = new();
        builder.Json["asset"] = new JsonObject { ["version"] = "3.0" };

        Assert.Throws<NotSupportedException>(() => builder.Read());
    }

    [Test]
    public void Read_LaterMinorVersion_IsRead()
    {
        GlbBuilder builder = new();
        builder.Json["asset"] = new JsonObject { ["version"] = "2.1" };

        Assert.DoesNotThrow(() => builder.Read());
    }

    [Test]
    public void Read_MinVersionAboveVersion_Throws()
    {
        GlbBuilder builder = new();
        builder.Json["asset"] = new JsonObject { ["version"] = "2.0", ["minVersion"] = "2.1" };

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_MinVersionAboveTwoZero_IsNotSupported()
    {
        GlbBuilder builder = new();
        builder.Json["asset"] = new JsonObject { ["version"] = "2.1", ["minVersion"] = "2.1" };

        Assert.Throws<NotSupportedException>(() => builder.Read());
    }
}
