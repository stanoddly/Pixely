using System.Numerics;
using System.Text.Json.Nodes;
using Pixely.Gpu;
using Pixely.Models.Gltf;

namespace Pixely.Models.Tests;

public class RepairAndFiniteTests
{
    [Test]
    public void Read_FloatWeightsNotSummingToOne_AreRenormalized()
    {
        GlbBuilder builder = new();
        AddSkinAttributes(builder, builder.AddFloats("VEC4", 0.5f, 0.25f, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0));

        Vector4 weights = builder.Read().Meshes[0].Primitives[0].Skin![0].Weights;

        Assert.That(weights.X, Is.EqualTo(2f / 3f).Within(1e-6f));
        Assert.That(weights.Y, Is.EqualTo(1f / 3f).Within(1e-6f));
    }

    [Test]
    public void Read_QuantizedWeightsNotSummingTo255_AreRenormalized()
    {
        GlbBuilder builder = new();
        AddSkinAttributes(builder, builder.AddBytes("VEC4", AccessorReader.UnsignedByte, true, 127, 127, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0));

        Vector4 weights = builder.Read().Meshes[0].Primitives[0].Skin![0].Weights;

        Assert.That(weights.X, Is.EqualTo(0.5f).Within(1e-6f));
        Assert.That(weights.Y, Is.EqualTo(0.5f).Within(1e-6f));
    }

    [Test]
    public void Read_NegativeWeight_Throws()
    {
        GlbBuilder builder = new();
        AddSkinAttributes(builder, builder.AddFloats("VEC4", 1.5f, -0.5f, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0));

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_WeightsSummingToZero_Throws()
    {
        GlbBuilder builder = new();
        AddSkinAttributes(builder, builder.AddFloats("VEC4", 0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0));

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_RepeatedJointInVertex_IsKept()
    {
        GlbBuilder builder = new();
        JsonObject primitive = AddSkinAttributes(builder, builder.AddFloats("VEC4", 0.5f, 0.5f, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0));
        primitive["attributes"]!["JOINTS_0"] = builder.AddUShorts("VEC4", 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        SkinVertex skin = builder.Read().Meshes[0].Primitives[0].Skin![0];

        Assert.That((skin.Joint0, skin.Joint1), Is.EqualTo(((ushort)1, (ushort)1)));
    }

    [Test]
    public void Read_NonUnitNormal_IsNormalized()
    {
        GlbBuilder builder = new();
        JsonObject primitive = builder.AddTriangle();
        primitive["attributes"]!["NORMAL"] = builder.AddFloats("VEC3", 0, 0, 2, 0, 3, 0, 0.5f, 0, 0);

        PositionTextureNormalVertex[] vertices = builder.Read().Meshes[0].Primitives[0].Vertices;

        Assert.That(vertices.Select(vertex => vertex.Normal), Is.EqualTo(new[] { Vector3.UnitZ, Vector3.UnitY, Vector3.UnitX }));
    }

    [Test]
    public void Read_HugeFiniteNormal_IsNormalized()
    {
        GlbBuilder builder = new();
        JsonObject primitive = builder.AddTriangle();
        primitive["attributes"]!["NORMAL"] = builder.AddFloats("VEC3", float.MaxValue, float.MaxValue, 0, 0, 0, 1, 0, 0, 1);

        Vector3 normal = builder.Read().Meshes[0].Primitives[0].Vertices[0].Normal;

        Assert.That(Vector3.Distance(normal, Vector3.Normalize(new Vector3(1, 1, 0))), Is.LessThan(1e-6f));
    }

    [Test]
    public void Read_HugeFiniteWeights_AreRenormalized()
    {
        GlbBuilder builder = new();
        float max = float.MaxValue;
        AddSkinAttributes(builder, builder.AddFloats("VEC4", max, max, max, max, 1, 0, 0, 0, 1, 0, 0, 0));

        Vector4 weights = builder.Read().Meshes[0].Primitives[0].Skin![0].Weights;

        Assert.That(weights, Is.EqualTo(new Vector4(0.25f)));
    }

    [Test]
    public void Read_HugeFiniteNodeRotation_IsNormalized()
    {
        GlbBuilder builder = new();
        builder.Add("nodes", new JsonObject { ["rotation"] = new JsonArray(0, 0, float.MaxValue, float.MaxValue) });

        Quaternion rotation = builder.Read().Nodes[0].LocalTransform.Rotation;

        Assert.That(rotation.Length(), Is.EqualTo(1f).Within(1e-6f));
        Assert.That(rotation.Z, Is.EqualTo(rotation.W));
    }

    [Test]
    public void Read_ZeroNormal_Throws()
    {
        GlbBuilder builder = new();
        JsonObject primitive = builder.AddTriangle();
        primitive["attributes"]!["NORMAL"] = builder.AddFloats("VEC3", 0, 0, 1, 0, 0, 0, 0, 0, 1);

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_NonUnitNodeRotation_IsNormalized()
    {
        GlbBuilder builder = new();
        builder.Add("nodes", new JsonObject { ["rotation"] = new JsonArray(0, 0, 0, 2) });

        Assert.That(builder.Read().Nodes[0].LocalTransform.Rotation, Is.EqualTo(Quaternion.Identity));
    }

    [Test]
    public void Read_ZeroNodeRotation_Throws()
    {
        GlbBuilder builder = new();
        builder.Add("nodes", new JsonObject { ["rotation"] = new JsonArray(0, 0, 0, 0) });

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [TestCase("POSITION")]
    [TestCase("NORMAL")]
    [TestCase("TEXCOORD_0")]
    [TestCase("WEIGHTS_0")]
    public void Read_NotFiniteAttribute_Throws(string attribute)
    {
        foreach (float value in new[] { float.NaN, float.PositiveInfinity })
        {
            GlbBuilder builder = new();
            JsonObject primitive = AddSkinAttributes(builder, builder.AddFloats("VEC4", 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0));
            primitive["attributes"]![attribute] = attribute switch
            {
                "TEXCOORD_0" => builder.AddFloats("VEC2", value, 0, 0, 0, 0, 0),
                "WEIGHTS_0" => builder.AddFloats("VEC4", value, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0),
                _ => builder.AddFloats("VEC3", value, 0, 1, 0, 0, 1, 0, 0, 1)
            };

            Assert.Throws<InvalidDataException>(() => builder.Read(), $"{attribute} = {value}");
        }
    }

    [Test]
    public void Read_NotFiniteInverseBindMatrix_Throws()
    {
        GlbBuilder builder = new();
        float[] matrix = Identity();
        matrix[12] = float.NaN;
        builder.Add("nodes", new JsonObject());
        builder.Add("skins", new JsonObject { ["joints"] = new JsonArray(0), ["inverseBindMatrices"] = builder.AddFloats("MAT4", matrix) });

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_NotFiniteNodeTranslation_Throws()
    {
        GlbBuilder builder = new();
        builder.Add("nodes", new JsonObject { ["translation"] = new JsonArray(0, 0, 1e39) });

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    internal static float[] Identity() => [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

    private static JsonObject AddSkinAttributes(GlbBuilder builder, int weights)
    {
        JsonObject primitive = builder.AddTriangle();
        primitive["attributes"]!["JOINTS_0"] = builder.AddUShorts("VEC4", new ushort[12]);
        primitive["attributes"]!["WEIGHTS_0"] = weights;
        return primitive;
    }
}
