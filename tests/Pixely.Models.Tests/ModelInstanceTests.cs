using System.Collections.Immutable;
using System.Numerics;

namespace Pixely.Models.Tests;

public class ModelInstanceTests
{
    private static readonly Matrix4x4 _mirror = Matrix4x4.CreateScale(-1, 1, 1);

    [TestCase(0.5, 0.5f)]
    [TestCase(2.5, 0.5f)]
    [TestCase(2.0, 0f)]
    [TestCase(-0.5, 1.5f)]
    public void GetLoopTime_WrapsIntoTheDuration(double seconds, float expected)
    {
        Assert.That(ModelInstance.GetLoopTime(TimeSpan.FromSeconds(seconds), 2f), Is.EqualTo(expected).Within(1e-6f));
    }

    [Test]
    public void GetLoopTime_AfterAYear_KeepsTheFractionOfALoop()
    {
        // A year in seconds is about 3.2e7, where a float steps by 2 seconds, so a float modulo would lose the position in the loop.
        TimeSpan elapsed = TimeSpan.FromDays(365) + TimeSpan.FromSeconds(0.25);

        Assert.That(ModelInstance.GetLoopTime(elapsed, 2f), Is.EqualTo(0.25f).Within(1e-6f));
    }

    [Test]
    public void GetLoopTime_WithoutDuration_IsZero()
    {
        Assert.That(ModelInstance.GetLoopTime(TimeSpan.FromSeconds(3), 0f), Is.Zero);
    }

    [Test]
    public void GetWinding_FollowsTheDeterminantSign()
    {
        Assert.That(ModelInstance.GetWinding(Matrix4x4.CreateScale(2)), Is.EqualTo(Winding.CounterClockwise));
        Assert.That(ModelInstance.GetWinding(_mirror), Is.EqualTo(Winding.Clockwise));
        Assert.That(ModelInstance.GetWinding(Matrix4x4.CreateScale(0, 1, 1)), Is.EqualTo(Winding.Mixed));
    }

    [Test]
    public void GetWinding_OfJointsThatAgree_IsTheirWinding()
    {
        Matrix4x4[] joints = [Matrix4x4.Identity, Matrix4x4.CreateRotationY(1f)];

        Assert.That(ModelInstance.GetWinding(joints, Matrix4x4.Identity), Is.EqualTo(Winding.CounterClockwise));
        Assert.That(ModelInstance.GetWinding([.. joints.Select(joint => joint * _mirror)], Matrix4x4.Identity), Is.EqualTo(Winding.Clockwise));
    }

    [Test]
    public void GetWinding_WithAMirroringPlacement_IsClockwise()
    {
        Assert.That(ModelInstance.GetWinding([Matrix4x4.Identity, Matrix4x4.Identity], _mirror * Matrix4x4.CreateTranslation(3, 0, 0)), Is.EqualTo(Winding.Clockwise));
    }

    [Test]
    public void GetWinding_OfMirroredJointsAndAMirroringPlacement_IsCounterClockwise()
    {
        Assert.That(ModelInstance.GetWinding([_mirror], _mirror), Is.EqualTo(Winding.CounterClockwise));
    }

    [Test]
    public void GetWinding_OfJointsThatDisagree_IsMixed()
    {
        Assert.That(ModelInstance.GetWinding([Matrix4x4.Identity, _mirror], Matrix4x4.Identity), Is.EqualTo(Winding.Mixed));
    }

    [Test]
    public void GetWinding_WithACollapsedJoint_IsMixed()
    {
        Assert.That(ModelInstance.GetWinding([Matrix4x4.Identity, Matrix4x4.CreateScale(1, 0, 1)], Matrix4x4.Identity), Is.EqualTo(Winding.Mixed));
    }

    [Test]
    public void Animate_LoopsTheAnimationAndUpdatesTheWorldMatrices()
    {
        using ModelInstance instance = CreateInstance(out Model model);

        // 2.5 seconds into a 2-second animation is 0.5 seconds into the loop, halfway from x = 0 to x = 4.
        instance.Animate(model.Animations[0], TimeSpan.FromSeconds(2.5));

        Assert.That(instance.WorldMatrices[0].Translation, Is.EqualTo(new Vector3(1, 0, 0)));
    }

    [Test]
    public void Animate_AnimationOfAnotherModel_ThrowsAndKeepsThePose()
    {
        using ModelInstance instance = CreateInstance(out _);
        CreateInstance(out Model other).Dispose();
        instance.Pose.LocalTransforms[0] = instance.Pose.LocalTransforms[0] with { Translation = new Vector3(0, 5, 0) };

        Assert.Throws<ArgumentException>(() => instance.Animate(other.Animations[0], TimeSpan.Zero));
        Assert.That(instance.Pose.LocalTransforms[0].Translation, Is.EqualTo(new Vector3(0, 5, 0)));
    }

    [Test]
    public void DisposedInstance_Throws()
    {
        ModelInstance instance = CreateInstance(out Model model);
        instance.Dispose();

        Assert.Throws<ObjectDisposedException>(() => instance.Animate(model.Animations[0], TimeSpan.Zero));
        Assert.Throws<ObjectDisposedException>(() => instance.UpdateMatrices());
    }

    // One node without a skin, so the instance has no joint buffers and needs no GPU. Its animation moves the node from x = 0 to
    // x = 4 over 2 seconds.
    private static ModelInstance CreateInstance(out Model model)
    {
        ImmutableArray<ModelNode> nodes = [new ModelNode(null, null, [], new NodeTransform(Vector3.Zero, Quaternion.Identity, Vector3.One), null, null)];
        AnimationChannel channel = new(0, AnimationPath.Translation, AnimationInterpolation.Linear, [0, 2], [0, 0, 0, 4, 0, 0]);
        model = new Model(nodes, [0], [], null, [], [], [], [new ModelAnimation(null, [channel], nodes)], []);
        return new ModelInstance(model, new ModelPose(model), new Matrix4x4[1], [], [], null!);
    }
}
