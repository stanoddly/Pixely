using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Pixely.Models.Gltf;

namespace Pixely.Models.Tests;

public class AnimationTests
{
    [Test]
    public void Sample_Linear_InterpolatesBetweenKeyframes()
    {
        AnimationChannel channel = new(0, AnimationPath.Translation, AnimationInterpolation.Linear, [0, 2], [0, 0, 0, 4, 2, 0]);

        Assert.That(channel.Sample(0.5f), Is.EqualTo(new Vector4(1, 0.5f, 0, 0)));
    }

    [Test]
    public void Sample_Step_HoldsThePreviousKeyframe()
    {
        AnimationChannel channel = new(0, AnimationPath.Scale, AnimationInterpolation.Step, [0, 1, 2], [1, 1, 1, 2, 2, 2, 3, 3, 3]);

        Assert.That(channel.Sample(1.99f), Is.EqualTo(new Vector4(2, 2, 2, 0)));
        Assert.That(channel.Sample(1f), Is.EqualTo(new Vector4(2, 2, 2, 0)));
    }

    [Test]
    public void Sample_OutsideTheKeyframes_ClampsToTheFirstAndLast()
    {
        AnimationChannel channel = new(0, AnimationPath.Translation, AnimationInterpolation.Linear, [1, 2], [1, 0, 0, 2, 0, 0]);

        Assert.That(channel.Sample(0), Is.EqualTo(new Vector4(1, 0, 0, 0)));
        Assert.That(channel.Sample(5), Is.EqualTo(new Vector4(2, 0, 0, 0)));
    }

    [Test]
    public void Sample_SingleKeyframe_AlwaysGivesItsValue()
    {
        AnimationChannel channel = new(0, AnimationPath.Translation, AnimationInterpolation.Linear, [0], [3, 0, 0]);
        ModelAnimation animation = new(null, [channel], []);

        Assert.That(animation.Duration, Is.EqualTo(0));
        Assert.That(channel.Sample(1), Is.EqualTo(new Vector4(3, 0, 0, 0)));
    }

    [Test]
    public void Sample_LinearRotation_TakesTheShortPath()
    {
        // 300 degrees about +Z is -60 degrees the short way, so halfway is -30 degrees.
        Quaternion end = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI * 300 / 180);
        AnimationChannel channel = new(0, AnimationPath.Rotation, AnimationInterpolation.Linear, [0, 1], [0, 0, 0, 1, end.X, end.Y, end.Z, end.W]);

        Quaternion half = AnimationChannel.ToQuaternion(channel.Sample(0.5f));

        Vector3 x = Vector3.Transform(Vector3.UnitX, half);
        Assert.That(Vector3.Distance(x, new Vector3(MathF.Cos(-MathF.PI / 6), MathF.Sin(-MathF.PI / 6), 0)), Is.LessThan(1e-5f));
    }

    [Test]
    public void Sample_CubicSpline_UsesHermiteWithScaledTangents()
    {
        // Keyframes at 0 and 2: value 0 with out-tangent 1, value 1 with in-tangent 3.
        float[] values = [9, 9, 9, /**/ 0, 0, 0, /**/ 1, 0, 0, /**/ 3, 0, 0, /**/ 1, 0, 0, /**/ 9, 9, 9];
        AnimationChannel channel = new(0, AnimationPath.Translation, AnimationInterpolation.CubicSpline, [0, 2], values);

        // u = 0.5, delta = 2: 0.5 * 0 + 0.125 * (2 * 1) + 0.5 * 1 + (-0.125) * (2 * 3) = 0.
        Vector4 result = channel.Sample(1);

        Assert.That(result.X, Is.EqualTo(0).Within(1e-6f));
        Assert.That(channel.Sample(0).X, Is.EqualTo(0));
        Assert.That(channel.Sample(2).X, Is.EqualTo(1));
    }

    [Test]
    public void Read_QuantizedCubicRotation_KeepsTangentsUnnormalized()
    {
        // Values are unit quaternions about +Z. The first out-tangent is (0, 0, 0.5, 0), which normalizing would turn into
        // (0, 0, 1, 0); the second in-tangent is 0, which normalizing would reject.
        short[] output =
        [
            0, 0, 0, 0, /**/ 0, 0, 0, 32767, /**/ 0, 0, 16384, 0,
            0, 0, 0, 0, /**/ 0, 0, 23170, 23170, /**/ 0, 0, 0, 0
        ];
        GlbBuilder builder = AddAnimatedNode(new[] { 0f, 1f }, MemoryMarshal.AsBytes<short>(output), AccessorReader.Short, "VEC4", "rotation", "CUBICSPLINE");

        AnimationChannel channel = builder.Read().Animations[0].Channels[0];
        Vector4 result = channel.Sample(0.5f);

        Vector4 p0 = new(0, 0, 0, 1);
        Vector4 p1 = Vector4.Normalize(new Vector4(0, 0, 23170, 23170));
        Vector4 tangent = new(0, 0, 16384 / 32767f, 0);
        Vector4 expected = Vector4.Normalize(0.5f * p0 + 0.125f * tangent + 0.5f * p1);
        Assert.That(Vector4.Distance(result, expected), Is.LessThan(1e-5f));
    }

    [Test]
    public void Read_NonUnitRotationKeys_AreNormalized()
    {
        GlbBuilder builder = AddAnimatedNode(new[] { 0f }, MemoryMarshal.AsBytes<float>(new float[] { 0, 0, 0, 2 }), AccessorReader.Float, "VEC4", "rotation", "LINEAR");

        Assert.That(builder.Read().Animations[0].Channels[0].Sample(0), Is.EqualTo(new Vector4(0, 0, 0, 1)));
    }

    [Test]
    public void Read_OutputSharedByCubicAndLinearChannels_KeepsTheCubicTangents()
    {
        // In-tangent, value and out-tangent of two keyframes; the tangents are not unit length.
        float[] output = [0, 0, 3, 0, 0, 0, 0, 2, 0, 0, 3, 0, 0, 0, 0, 3, 0, 0, 1, 1, 0, 0, 3, 0];
        GlbBuilder alone = AddAnimatedNode([0, 1], MemoryMarshal.AsBytes<float>(output), AccessorReader.Float, "VEC4", "rotation", "CUBICSPLINE");
        GlbBuilder shared = AddAnimatedNode([0, 1], MemoryMarshal.AsBytes<float>(output), AccessorReader.Float, "VEC4", "rotation", "CUBICSPLINE");
        // A second animation reads the same output as six LINEAR keyframes, which normalizes every element of its copy.
        int times = shared.AddFloats("SCALAR", 0, 1, 2, 3, 4, 5);
        shared.Add("animations", new JsonObject
        {
            ["channels"] = new JsonArray(new JsonObject { ["sampler"] = 0, ["target"] = new JsonObject { ["node"] = 0, ["path"] = "rotation" } }),
            ["samplers"] = new JsonArray(new JsonObject { ["input"] = times, ["output"] = 1 })
        });

        Vector4 expected = alone.Read().Animations[0].Channels[0].Sample(0.5f);

        Assert.That(shared.Read().Animations[0].Channels[0].Sample(0.5f), Is.EqualTo(expected));
    }

    [Test]
    public void Read_ChannelWithoutNode_IsSkipped()
    {
        GlbBuilder builder = AddAnimatedNode(new[] { 0f, 1f }, MemoryMarshal.AsBytes<float>(new float[] { 0, 0, 0, 1, 0, 0 }), AccessorReader.Float, "VEC3", "translation", "LINEAR");
        JsonArray channels = (JsonArray)builder.Json["animations"]![0]!["channels"]!;
        // A second channel on the same path, without a node, is neither read nor a duplicate target.
        channels.Add(new JsonObject { ["sampler"] = 0, ["target"] = new JsonObject { ["path"] = "translation" } });

        Assert.That(builder.Read().Animations[0].Channels, Has.Length.EqualTo(1));
    }

    [Test]
    public void Read_DuplicateTarget_Throws()
    {
        GlbBuilder builder = AddAnimatedNode(new[] { 0f, 1f }, MemoryMarshal.AsBytes<float>(new float[] { 0, 0, 0, 1, 0, 0 }), AccessorReader.Float, "VEC3", "translation", "LINEAR");
        JsonArray channels = (JsonArray)builder.Json["animations"]![0]!["channels"]!;
        channels.Add(channels[0]!.DeepClone());

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_ChannelOnMatrixNode_Throws()
    {
        GlbBuilder builder = AddAnimatedNode(new[] { 0f, 1f }, MemoryMarshal.AsBytes<float>(new float[] { 0, 0, 0, 1, 0, 0 }), AccessorReader.Float, "VEC3", "translation", "LINEAR");
        builder.Json["nodes"]![0]!["matrix"] = new JsonArray(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_WeightsPath_IsNotSupported()
    {
        GlbBuilder builder = AddAnimatedNode(new[] { 0f, 1f }, MemoryMarshal.AsBytes<float>(new float[] { 0, 0, 0, 1, 0, 0 }), AccessorReader.Float, "VEC3", "weights", "LINEAR");

        Assert.Throws<NotSupportedException>(() => builder.Read());
    }

    [TestCase(new[] { 0f, 0f })]
    [TestCase(new[] { 1f, 0.5f })]
    [TestCase(new[] { -1f, 0f })]
    [TestCase(new[] { 0f, float.NaN })]
    public void Read_InvalidKeyframeTimes_Throw(float[] times)
    {
        GlbBuilder builder = AddAnimatedNode(times, MemoryMarshal.AsBytes<float>(new float[] { 0, 0, 0, 1, 0, 0 }), AccessorReader.Float, "VEC3", "translation", "LINEAR");

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_OutputCountMismatch_Throws()
    {
        GlbBuilder builder = AddAnimatedNode(new[] { 0f, 1f }, MemoryMarshal.AsBytes<float>(new float[] { 0, 0, 0 }), AccessorReader.Float, "VEC3", "translation", "LINEAR");

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_CubicOutputWithoutTangents_Throws()
    {
        GlbBuilder builder = AddAnimatedNode(new[] { 0f, 1f }, MemoryMarshal.AsBytes<float>(new float[] { 0, 0, 0, 1, 0, 0 }), AccessorReader.Float, "VEC3", "translation", "CUBICSPLINE");

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_CubicWithOneKeyframe_Throws()
    {
        GlbBuilder builder = AddAnimatedNode(new[] { 0f }, MemoryMarshal.AsBytes<float>(new float[9]), AccessorReader.Float, "VEC3", "translation", "CUBICSPLINE");

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_NotFiniteOutput_Throws()
    {
        GlbBuilder builder = AddAnimatedNode(new[] { 0f, 1f }, MemoryMarshal.AsBytes<float>(new float[] { 0, 0, 0, float.NaN, 0, 0 }), AccessorReader.Float, "VEC3", "translation", "LINEAR");

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_UnknownInterpolation_Throws()
    {
        GlbBuilder builder = AddAnimatedNode(new[] { 0f, 1f }, MemoryMarshal.AsBytes<float>(new float[] { 0, 0, 0, 1, 0, 0 }), AccessorReader.Float, "VEC3", "translation", "SMOOTH");

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_Animation_HasDurationOfLastKeyframe()
    {
        GlbBuilder builder = AddAnimatedNode(new[] { 0f, 1.5f }, MemoryMarshal.AsBytes<float>(new float[] { 0, 0, 0, 1, 0, 0 }), AccessorReader.Float, "VEC3", "translation", "LINEAR");

        Assert.That(builder.Read().Animations[0].Duration, Is.EqualTo(1.5f));
    }

    [Test]
    public void Apply_WritesTheSampledComponentOnly()
    {
        ModelNode node = new(null, null, [], new NodeTransform(Vector3.Zero, Quaternion.Identity, new Vector3(2)), null, null);
        ImmutableArray<ModelNode> nodes = [node];
        ModelPose pose = new(nodes, [0]);
        AnimationChannel channel = new(0, AnimationPath.Translation, AnimationInterpolation.Linear, [0, 1], [0, 0, 0, 2, 0, 0]);

        pose.Apply(new ModelAnimation(null, [channel], nodes), 0.5f);

        Assert.That(pose.LocalTransforms[0], Is.EqualTo(new NodeTransform(Vector3.UnitX, Quaternion.Identity, new Vector3(2))));
        pose.Reset();
        Assert.That(pose.LocalTransforms[0].Translation, Is.EqualTo(Vector3.Zero));
    }

    [Test]
    public void Apply_NaNTime_Throws()
    {
        ImmutableArray<ModelNode> nodes = [new ModelNode(null, null, [], NodeTransform.Identity, null, null)];
        ModelPose pose = new(nodes, [0]);

        Assert.Throws<ArgumentException>(() => pose.Apply(new ModelAnimation(null, ImmutableArray<AnimationChannel>.Empty, nodes), float.NaN));
    }

    [Test]
    public void Apply_AnimationOfAnotherModel_Throws()
    {
        // Equal nodes in another array: the check is identity, because another model's channels index other nodes.
        ModelNode node = new(null, null, [], NodeTransform.Identity, null, null);
        ModelPose pose = new([node], [0]);
        AnimationChannel channel = new(0, AnimationPath.Translation, AnimationInterpolation.Linear, [0, 1], [0, 0, 0, 2, 0, 0]);

        Assert.Throws<ArgumentException>(() => pose.Apply(new ModelAnimation(null, [channel], [node]), 0.5f));
    }

    [TestCase(0.25f, 1f)]
    [TestCase(0.75f, -1f)]
    public void Sample_CubicRotationBetweenOppositeKeys_UsesTheNearerKey(float time, float expectedW)
    {
        // q and -q are the same rotation; with zero tangents the spline passes through a zero quaternion halfway, and near it.
        float[] values = [0, 0, 0, 0, /**/ 0, 0, 0, 1, /**/ 0, 0, 0, 0, /**/ 0, 0, 0, 0, /**/ 0, 0, 0, -1, /**/ 0, 0, 0, 0];
        AnimationChannel channel = new(0, AnimationPath.Rotation, AnimationInterpolation.CubicSpline, [0, 1], values);

        Vector4 result = channel.Sample(time);

        Assert.That(float.IsFinite(result.Length()), Is.True);
        Assert.That(result.W, Is.EqualTo(expectedW).Within(1e-6f));
    }

    [Test]
    public void Sample_CubicRotationThroughZero_IsFinite()
    {
        float[] values = [0, 0, 0, 0, /**/ 0, 0, 0, 1, /**/ 0, 0, 0, 0, /**/ 0, 0, 0, 0, /**/ 0, 0, 0, -1, /**/ 0, 0, 0, 0];
        AnimationChannel channel = new(0, AnimationPath.Rotation, AnimationInterpolation.CubicSpline, [0, 1], values);

        Assert.That(float.IsFinite(channel.Sample(0.5f).Length()), Is.True);
    }

    [Test]
    public void Sample_CubicWithOverflowingTangents_UsesTheNearerKey()
    {
        // Finite tangents scaled by a keyframe delta of 2 overflow to infinity.
        float[] values = [0, 0, 0, /**/ 1, 0, 0, /**/ float.MaxValue, 0, 0, /**/ float.MaxValue, 0, 0, /**/ 2, 0, 0, /**/ 0, 0, 0];
        AnimationChannel channel = new(0, AnimationPath.Translation, AnimationInterpolation.CubicSpline, [0, 2], values);

        Assert.That(channel.Sample(0.5f), Is.EqualTo(new Vector4(1, 0, 0, 0)));
        Assert.That(channel.Sample(1.5f), Is.EqualTo(new Vector4(2, 0, 0, 0)));
    }

    private static GlbBuilder AddAnimatedNode(float[] times, ReadOnlySpan<byte> output, int outputComponentType, string outputType, string path, string interpolation)
    {
        GlbBuilder builder = new();
        builder.Add("nodes", new JsonObject());
        int input = builder.AddFloats("SCALAR", times);
        int componentSize = outputComponentType is AccessorReader.Float ? 4 : outputComponentType is AccessorReader.Short or AccessorReader.UnsignedShort ? 2 : 1;
        int componentCount = outputType == "VEC4" ? 4 : 3;
        int outputAccessor = builder.AddAccessor(builder.AddBufferView(output), outputComponentType, outputType, output.Length / componentSize / componentCount,
            normalized: outputComponentType != AccessorReader.Float);
        builder.Add("animations", new JsonObject
        {
            ["channels"] = new JsonArray(new JsonObject { ["sampler"] = 0, ["target"] = new JsonObject { ["node"] = 0, ["path"] = path } }),
            ["samplers"] = new JsonArray(new JsonObject { ["input"] = input, ["output"] = outputAccessor, ["interpolation"] = interpolation })
        });
        return builder;
    }
}
