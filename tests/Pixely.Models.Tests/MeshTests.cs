using System.Numerics;
using System.Text.Json.Nodes;
using Pixely.Models.Gltf;

namespace Pixely.Models.Tests;

public class MeshTests
{
    [Test]
    public void Read_Triangle_HasVerticesIndicesAndNoSkin()
    {
        GlbBuilder builder = new();
        builder.AddTriangle();

        PrimitiveData primitive = builder.Read().Meshes[0].Primitives[0];

        Assert.That(primitive.Vertices.Select(vertex => vertex.Position), Is.EqualTo(new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY }));
        Assert.That(primitive.Indices16, Is.EqualTo(new ushort[] { 0, 1, 2 }));
        Assert.That(primitive.Skin, Is.Null);
        Assert.That(primitive.Material, Is.Null);
    }

    [Test]
    public void Read_NonIndexedPrimitive_GetsSequentialIndices()
    {
        GlbBuilder builder = new();
        builder.AddTriangle(indexed: false);

        Assert.That(builder.Read().Meshes[0].Primitives[0].Indices16, Is.EqualTo(new ushort[] { 0, 1, 2 }));
    }

    [Test]
    public void Read_AttributeCountsDiffer_Throws()
    {
        GlbBuilder builder = new();
        JsonObject primitive = builder.AddTriangle();
        primitive["attributes"]!["NORMAL"] = builder.AddFloats("VEC3", 0, 0, 1, 0, 0, 1);

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_IndexOutOfRange_Throws()
    {
        GlbBuilder builder = new();
        JsonObject primitive = builder.AddTriangle();
        primitive["indices"] = builder.AddUShorts("SCALAR", 0, 1, 3);

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_IndicesNotWholeTriangles_Throws()
    {
        GlbBuilder builder = new();
        JsonObject primitive = builder.AddTriangle();
        primitive["indices"] = builder.AddUShorts("SCALAR", 0, 1, 2, 0);

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_NonIndexedVerticesNotWholeTriangles_Throws()
    {
        GlbBuilder builder = new();
        int positions = builder.AddFloats("VEC3", 0, 0, 0, 1, 0, 0, 0, 1, 0, 1, 1, 0);
        int normals = builder.AddFloats("VEC3", 0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1);
        AccessorTests.AddPrimitive(builder, new JsonObject { ["POSITION"] = positions, ["NORMAL"] = normals });

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_MaterialOutOfRange_Throws()
    {
        GlbBuilder builder = new();
        JsonObject primitive = builder.AddTriangle();
        primitive["material"] = 0;

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_EmptyPrimitives_Throws()
    {
        GlbBuilder builder = new();
        builder.Add("meshes", new JsonObject { ["primitives"] = new JsonArray() });

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_JointsWithoutWeights_Throws()
    {
        GlbBuilder builder = new();
        JsonObject primitive = builder.AddTriangle();
        primitive["attributes"]!["JOINTS_0"] = builder.AddUShorts("VEC4", new ushort[12]);

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_UnsignedByteJoints_AreWidened()
    {
        GlbBuilder builder = new();
        JsonObject primitive = builder.AddTriangle();
        primitive["attributes"]!["JOINTS_0"] = builder.AddBytes("VEC4", AccessorReader.UnsignedByte, false, 1, 2, 3, 4, 0, 0, 0, 0, 0, 0, 0, 0);
        primitive["attributes"]!["WEIGHTS_0"] = builder.AddFloats("VEC4", 0.25f, 0.25f, 0.25f, 0.25f, 1, 0, 0, 0, 1, 0, 0, 0);

        PrimitiveData data = builder.Read().Meshes[0].Primitives[0];

        Assert.That(data.Skin![0], Is.EqualTo(new SkinVertex(1, 2, 3, 4, new Vector4(0.25f))));
        Assert.That(data.MaxJoint, Is.EqualTo(4));
    }

    [Test]
    public void Read_TwoSetsOfJoints_IsNotSupported()
    {
        GlbBuilder builder = new();
        JsonObject primitive = builder.AddTriangle();
        primitive["attributes"]!["JOINTS_1"] = builder.AddUShorts("VEC4", new ushort[12]);

        Assert.Throws<NotSupportedException>(() => builder.Read());
    }

    [Test]
    public void Read_MissingNormal_IsNotSupported()
    {
        GlbBuilder builder = new();
        JsonObject primitive = builder.AddTriangle();
        ((JsonObject)primitive["attributes"]!).Remove("NORMAL");

        Assert.Throws<NotSupportedException>(() => builder.Read());
    }

    [Test]
    public void Read_MissingPosition_IsNotSupported()
    {
        GlbBuilder builder = new();
        JsonObject primitive = builder.AddTriangle();
        ((JsonObject)primitive["attributes"]!).Remove("POSITION");

        Assert.Throws<NotSupportedException>(() => builder.Read());
    }

    [Test]
    public void Read_LinesMode_IsNotSupported()
    {
        GlbBuilder builder = new();
        JsonObject primitive = builder.AddTriangle();
        primitive["mode"] = 1;

        Assert.Throws<NotSupportedException>(() => builder.Read());
    }

    [Test]
    public void Read_MorphTargets_AreNotSupported()
    {
        GlbBuilder builder = new();
        JsonObject primitive = builder.AddTriangle();
        primitive["targets"] = new JsonArray(new JsonObject { ["POSITION"] = (int)primitive["attributes"]!["POSITION"]! });

        Assert.Throws<NotSupportedException>(() => builder.Read());
    }

    [Test]
    public void Read_RequiredExtension_IsNotSupported()
    {
        GlbBuilder builder = new();
        builder.AddTriangle();
        builder.Json["extensionsRequired"] = new JsonArray("KHR_draco_mesh_compression");

        Assert.Throws<NotSupportedException>(() => builder.Read());
    }

    [Test]
    public void Read_UsedExtension_IsIgnored()
    {
        GlbBuilder builder = new();
        builder.AddTriangle();
        builder.Json["extensionsUsed"] = new JsonArray("KHR_materials_emissive_strength");

        Assert.DoesNotThrow(() => builder.Read());
    }

    [Test]
    public void Read_BufferWithUri_IsNotSupported()
    {
        GlbBuilder builder = new();
        builder.AddTriangle();
        builder.Json["buffers"] = new JsonArray(new JsonObject { ["byteLength"] = 4, ["uri"] = "data.bin" });

        Assert.Throws<NotSupportedException>(() => builder.Read());
    }

    [Test]
    public void Read_TwoBuffers_AreNotSupported()
    {
        GlbBuilder builder = new();
        builder.AddTriangle();
        builder.Json["buffers"] = new JsonArray(new JsonObject { ["byteLength"] = 4 }, new JsonObject { ["byteLength"] = 4 });

        Assert.Throws<NotSupportedException>(() => builder.Read());
    }
}
