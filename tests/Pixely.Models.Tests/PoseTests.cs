using System.Collections.Immutable;
using System.Numerics;

namespace Pixely.Models.Tests;

public class PoseTests
{
    [Test]
    public void ComputeWorldMatrices_AppliesTheParentAfterTheChild()
    {
        // The parent turns 90 degrees about +Z and moves up by 5; the child sits 1 along the parent's X axis.
        ModelPose pose = CreateTwoBoneChain(out _);
        Matrix4x4[] world = new Matrix4x4[2];

        pose.ComputeWorldMatrices(world);

        // The child's origin is the parent's X axis turned onto +Y, then moved up: a reversed order would give (1, 0, 5).
        Vector3 childOrigin = Vector3.Transform(Vector3.Zero, world[1]);
        Assert.That(Vector3.Distance(childOrigin, new Vector3(0, 1, 5)), Is.LessThan(1e-6f));
        // A point 1 along the child's X axis is turned too.
        Assert.That(Vector3.Distance(Vector3.Transform(Vector3.UnitX, world[1]), new Vector3(0, 2, 5)), Is.LessThan(1e-6f));
    }

    [Test]
    public void ComputeWorldMatrices_FollowsParentFirstOrderNotFileOrder()
    {
        ModelNode child = new(null, 1, [], new NodeTransform(Vector3.UnitX, Quaternion.Identity, Vector3.One), null, null);
        ModelNode parent = new(null, null, [0], new NodeTransform(Vector3.UnitY, Quaternion.Identity, Vector3.One), null, null);
        ModelPose pose = new([child, parent], [1, 0]);
        Matrix4x4[] world = new Matrix4x4[2];

        pose.ComputeWorldMatrices(world);

        Assert.That(Vector3.Transform(Vector3.Zero, world[0]), Is.EqualTo(new Vector3(1, 1, 0)));
    }

    [Test]
    public void ComputeJointMatrices_AtTheBindPose_AreIdentity()
    {
        ModelPose pose = CreateTwoBoneChain(out ImmutableArray<ModelNode> nodes);
        Matrix4x4[] world = new Matrix4x4[2];
        pose.ComputeWorldMatrices(world);
        Matrix4x4.Invert(world[0], out Matrix4x4 inverse0);
        Matrix4x4.Invert(world[1], out Matrix4x4 inverse1);
        ModelSkin skin = new(null, [0, 1], [inverse0, inverse1], null);
        Matrix4x4[] joints = new Matrix4x4[2];

        ModelPose.ComputeJointMatrices(skin, world, joints);

        Assert.That(IsClose(joints[0], Matrix4x4.Identity) && IsClose(joints[1], Matrix4x4.Identity), Is.True);
    }

    [Test]
    public void ComputeJointMatrices_MovedJoint_MovesItsVertices()
    {
        ModelPose pose = CreateTwoBoneChain(out _);
        Matrix4x4[] world = new Matrix4x4[2];
        pose.ComputeWorldMatrices(world);
        Matrix4x4.Invert(world[1], out Matrix4x4 inverse1);
        ModelSkin skin = new(null, [1], [inverse1], null);
        // A vertex at the child joint in the bind pose.
        Vector3 vertex = Vector3.Transform(Vector3.Zero, world[1]);

        // Bend the child: turn it 90 degrees about +Z, so a vertex 1 along its X axis moves.
        pose.LocalTransforms[1] = pose.LocalTransforms[1] with { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2) };
        pose.ComputeWorldMatrices(world);
        Matrix4x4[] joints = new Matrix4x4[1];
        ModelPose.ComputeJointMatrices(skin, world, joints);

        // The joint's origin does not move; a point 1 further along the bind-pose child X axis (+Y in world) now points along -X.
        Assert.That(Vector3.Distance(Vector3.Transform(vertex, joints[0]), vertex), Is.LessThan(1e-5f));
        Vector3 tip = vertex + Vector3.UnitY;
        Assert.That(Vector3.Distance(Vector3.Transform(tip, joints[0]), vertex - Vector3.UnitX), Is.LessThan(1e-5f));
    }

    [Test]
    public void ComputeJointMatrices_IdentityInverseBind_GivesTheWorldMatrix()
    {
        ModelPose pose = CreateTwoBoneChain(out _);
        Matrix4x4[] world = new Matrix4x4[2];
        pose.ComputeWorldMatrices(world);
        Matrix4x4[] joints = new Matrix4x4[1];

        ModelPose.ComputeJointMatrices(new ModelSkin(null, [1], [Matrix4x4.Identity], null), world, joints);

        Assert.That(joints[0], Is.EqualTo(world[1]));
    }

    [Test]
    public void ComputeWorldMatrices_SpanTooShort_Throws()
    {
        ModelPose pose = CreateTwoBoneChain(out _);

        Assert.Throws<ArgumentException>(() => pose.ComputeWorldMatrices(new Matrix4x4[1]));
    }

    private static ModelPose CreateTwoBoneChain(out ImmutableArray<ModelNode> nodes)
    {
        Quaternion quarterTurn = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2);
        nodes =
        [
            new ModelNode("parent", null, [1], new NodeTransform(new Vector3(0, 0, 5), quarterTurn, Vector3.One), null, null),
            new ModelNode("child", 0, [], new NodeTransform(Vector3.UnitX, Quaternion.Identity, Vector3.One), null, null)
        ];
        return new ModelPose(nodes, [0, 1]);
    }

    private static bool IsClose(Matrix4x4 a, Matrix4x4 b)
    {
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                if (MathF.Abs(a[row, column] - b[row, column]) > 1e-5f)
                {
                    return false;
                }
            }
        }

        return true;
    }
}
