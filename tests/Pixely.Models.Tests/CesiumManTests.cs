using System.Numerics;
using Pixely.Content;
using Pixely.Gpu;
using Pixely.Models.Gltf;

namespace Pixely.Models.Tests;

// Reads the tutorial's CesiumMan.glb. The expected matrices were computed once by an independent implementation that follows the
// glTF specification literally: column vectors, world = parentWorld * local, joint = jointWorld * inverseBind. They are written
// here as row-vector matrices, M11 to M44.
public class CesiumManTests
{
    private static ModelData _data = null!;

    [OneTimeSetUp]
    public void ReadModel()
    {
        _data = GltfModelReader.Read(File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "CesiumMan.glb")));
    }

    [Test]
    public void Read_HasTheExpectedStructure()
    {
        Assert.That(_data.Nodes, Has.Length.EqualTo(22));
        Assert.That(_data.Scenes, Has.Length.EqualTo(1));
        Assert.That(_data.DefaultScene, Is.EqualTo(0));
        Assert.That(_data.Meshes, Has.Length.EqualTo(1));
        Assert.That(_data.Skins[0].Joints, Has.Length.EqualTo(19));
        Assert.That(_data.Animations[0].Channels, Has.Length.EqualTo(57));
        Assert.That(_data.Animations[0].Duration, Is.EqualTo(2f).Within(1e-5f));

        PrimitiveData primitive = _data.Meshes[0].Primitives[0];
        Assert.That(primitive.Vertices, Has.Length.EqualTo(3273));
        Assert.That(primitive.Skin, Has.Length.EqualTo(3273));
        Assert.That(primitive.Indices16, Has.Length.EqualTo(14016));
        Assert.That(primitive.MaxJoint, Is.LessThan(19));
        Assert.That(_data.Materials[0].BaseColorImage, Is.EqualTo(0));
        Assert.That(_data.Materials[0].DoubleSided, Is.False);
    }

    [TestCase(0, new[] { 0f, 0f, 1f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 0f, 1f })]
    [TestCase(7, new[] { 0f, 0f, 1f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 0f, 1f })]
    [TestCase(18, new[] { 0f, 0f, 1f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 0f, 1f })]
    public void JointMatrices_AtRest_MatchTheReference(int joint, float[] expected)
    {
        Assert.That(ToArray(ComputeJointMatrices(time: null)[joint]), Is.EqualTo(expected).Within(1e-4f));
    }

    [TestCase(0, new[] { 0.007346f, 0.001966f, 0.999971f, 0f, 0.999744f, 0.021396f, -0.007386f, 0f, -0.021410f, 0.999769f, -0.001808f, 0f, -0.015461f, -0.033950f, 0.001265f, 1f })]
    [TestCase(7, new[] { 0.343832f, -0.804810f, 0.483796f, 0f, 0.504499f, -0.276214f, -0.818037f, 0f, 0.791997f, 0.525343f, 0.311055f, 0f, -0.823242f, 0.448279f, -0.202098f, 1f })]
    [TestCase(18, new[] { 0.065871f, -0.986454f, -0.150235f, 0f, 0.997602f, 0.061900f, 0.030960f, 0f, -0.021241f, -0.151915f, 0.988165f, 0f, -0.037407f, 0.274399f, -0.479727f, 1f })]
    public void JointMatrices_AtOneSecond_MatchTheReference(int joint, float[] expected)
    {
        Assert.That(ToArray(ComputeJointMatrices(time: 1f)[joint]), Is.EqualTo(expected).Within(1e-4f));
    }

    [Test]
    public void EmbeddedJpeg_DecodesToTightlyPackedRgba()
    {
        using Image image = new SdlImageDecoder().Decode(_data.Images[0].Span);

        Assert.That(image.Size, Is.EqualTo(new ShortSize(1024, 1024)));
        Assert.That(image.PixelFormat, Is.EqualTo(PixelFormat.Abgr8888));
        Assert.That(image.Data.Length, Is.EqualTo(1024 * 1024 * 4));
    }

    private static Matrix4x4[] ComputeJointMatrices(float? time)
    {
        ModelPose pose = new(_data.Nodes, _data.ParentFirstOrder);
        if (time is { } t)
        {
            pose.Apply(_data.Animations[0], t);
        }

        Matrix4x4[] world = new Matrix4x4[_data.Nodes.Length];
        pose.ComputeWorldMatrices(world);
        Matrix4x4[] joints = new Matrix4x4[_data.Skins[0].Joints.Length];
        ModelPose.ComputeJointMatrices(_data.Skins[0], world, joints);
        return joints;
    }

    private static float[] ToArray(Matrix4x4 m) => [m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44];
}
