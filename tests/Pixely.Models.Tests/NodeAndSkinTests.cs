using System.Numerics;
using System.Text.Json.Nodes;
using Pixely.Models.Gltf;

namespace Pixely.Models.Tests;

public class NodeAndSkinTests
{
    // Scale (2, 3, 4), then 90 degrees about +Z, then translation (5, 6, 7), as a glTF column-major matrix for column vectors.
    private static readonly float[] _asymmetricMatrix = [0, 2, 0, 0, -3, 0, 0, 0, 0, 0, 4, 0, 5, 6, 7, 1];

    [Test]
    public void Read_NodeMatrix_DecomposesToItsTransform()
    {
        GlbBuilder builder = new();
        builder.Add("nodes", new JsonObject { ["matrix"] = ToJson(_asymmetricMatrix) });

        NodeTransform transform = builder.Read().Nodes[0].LocalTransform;

        Assert.That(Vector3.Distance(transform.Translation, new Vector3(5, 6, 7)), Is.LessThan(1e-5f));
        Assert.That(Vector3.Distance(transform.Scale, new Vector3(2, 3, 4)), Is.LessThan(1e-5f));
        // The X axis, scaled by 2 and turned 90 degrees about +Z, ends on +Y.
        Vector3 x = Vector3.Transform(Vector3.UnitX, transform.ToMatrix()) - new Vector3(5, 6, 7);
        Assert.That(Vector3.Distance(x, new Vector3(0, 2, 0)), Is.LessThan(1e-5f));
    }

    [Test]
    public void ReadMatrix_ColumnMajorValues_GiveTheRowVectorMatrix()
    {
        Matrix4x4 matrix = GltfModelReader.ReadMatrix(_asymmetricMatrix, 0, "test", "matrix");

        Assert.That(new[] { matrix.M41, matrix.M42, matrix.M43 }, Is.EqualTo(new[] { 5f, 6f, 7f }));
        Assert.That(Vector3.Transform(Vector3.UnitY, matrix), Is.EqualTo(new Vector3(2, 6, 7)));
    }

    [Test]
    public void Read_MirroringNodeMatrix_Decomposes()
    {
        GlbBuilder builder = new();
        builder.Add("nodes", new JsonObject { ["matrix"] = ToJson([-1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]) });

        NodeTransform transform = builder.Read().Nodes[0].LocalTransform;

        Assert.That(transform.ToMatrix().GetDeterminant(), Is.EqualTo(-1f).Within(1e-5f));
    }

    [Test]
    public void Read_ShearedNodeMatrix_Throws()
    {
        GlbBuilder builder = new();
        builder.Add("nodes", new JsonObject { ["matrix"] = ToJson([1, 0, 0, 0, 0.5f, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]) });

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_PerspectiveNodeMatrix_Throws()
    {
        GlbBuilder builder = new();
        builder.Add("nodes", new JsonObject { ["matrix"] = ToJson([1, 0, 0, 0.5f, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]) });

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_NodeMatrixWithTranslation_Throws()
    {
        GlbBuilder builder = new();
        builder.Add("nodes", new JsonObject { ["matrix"] = ToJson(RepairAndFiniteTests.Identity()), ["translation"] = new JsonArray(1, 0, 0) });

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_NodeMatrixOfWrongLength_Throws()
    {
        GlbBuilder builder = new();
        builder.Add("nodes", new JsonObject { ["matrix"] = ToJson([1, 0, 0, 0]) });

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_ChildBeforeParent_OrdersParentFirst()
    {
        GlbBuilder builder = new();
        builder.Add("nodes", new JsonObject());
        builder.Add("nodes", new JsonObject { ["children"] = new JsonArray(0) });

        ModelData data = builder.Read();

        Assert.That(data.ParentFirstOrder, Is.EqualTo(new[] { 1, 0 }));
        Assert.That(data.Nodes[0].Parent, Is.EqualTo(1));
        Assert.That(data.Nodes[1].Children, Is.EqualTo(new[] { 0 }));
    }

    [Test]
    public void Read_NodeCycle_Throws()
    {
        GlbBuilder builder = new();
        builder.Add("nodes", new JsonObject { ["children"] = new JsonArray(1) });
        builder.Add("nodes", new JsonObject { ["children"] = new JsonArray(0) });

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_NodeWithTwoParents_Throws()
    {
        GlbBuilder builder = new();
        builder.Add("nodes", new JsonObject());
        builder.Add("nodes", new JsonObject { ["children"] = new JsonArray(0) });
        builder.Add("nodes", new JsonObject { ["children"] = new JsonArray(0) });

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_NodeIsItsOwnChild_Throws()
    {
        GlbBuilder builder = new();
        builder.Add("nodes", new JsonObject { ["children"] = new JsonArray(0) });

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_ChildOutOfRange_Throws()
    {
        GlbBuilder builder = new();
        builder.Add("nodes", new JsonObject { ["children"] = new JsonArray(5) });

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_NodeMeshOutOfRange_Throws()
    {
        GlbBuilder builder = new();
        builder.Add("nodes", new JsonObject { ["mesh"] = 0 });

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_DefaultScene_IsExposed()
    {
        GlbBuilder builder = new();
        builder.AddTriangle();

        ModelData data = builder.Read();

        Assert.That(data.DefaultScene, Is.EqualTo(0));
        Assert.That(data.Scenes[0].RootNodes, Is.EqualTo(new[] { 0 }));
    }

    [Test]
    public void Read_NoDefaultScene_IsNull()
    {
        GlbBuilder builder = new();
        builder.AddTriangle();
        builder.Json.Remove("scene");

        Assert.That(builder.Read().DefaultScene, Is.Null);
    }

    [Test]
    public void Read_DefaultSceneOutOfRange_Throws()
    {
        GlbBuilder builder = new();
        builder.AddTriangle();
        builder.Json["scene"] = 1;

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_SceneRootWithParent_Throws()
    {
        GlbBuilder builder = new();
        builder.Add("nodes", new JsonObject());
        builder.Add("nodes", new JsonObject { ["children"] = new JsonArray(0) });
        builder.Add("scenes", new JsonObject { ["nodes"] = new JsonArray(0) });

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_SkinWithoutInverseBindMatrices_UsesIdentity()
    {
        GlbBuilder builder = AddSkinnedTriangle(jointCount: 2, inverseBindMatrices: null);

        ModelSkin skin = builder.Read().Skins[0];

        Assert.That(skin.InverseBindMatrices, Is.EqualTo(new[] { Matrix4x4.Identity, Matrix4x4.Identity }));
        Assert.That(skin.Joints, Is.EqualTo(new[] { 1, 2 }));
    }

    [Test]
    public void Read_MoreInverseBindMatricesThanJoints_UsesTheFirst()
    {
        float[] matrices = [.. RepairAndFiniteTests.Identity(), .. RepairAndFiniteTests.Identity(), .. RepairAndFiniteTests.Identity()];
        matrices[12] = 9;
        GlbBuilder builder = AddSkinnedTriangle(jointCount: 2, inverseBindMatrices: matrices);

        ModelSkin skin = builder.Read().Skins[0];

        Assert.That(skin.InverseBindMatrices, Has.Length.EqualTo(2));
        Assert.That(skin.InverseBindMatrices[0].M41, Is.EqualTo(9));
    }

    [Test]
    public void Read_FewerInverseBindMatricesThanJoints_Throws()
    {
        GlbBuilder builder = AddSkinnedTriangle(jointCount: 2, inverseBindMatrices: RepairAndFiniteTests.Identity());

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_NonAffineInverseBindMatrix_Throws()
    {
        float[] matrices = [.. RepairAndFiniteTests.Identity(), .. RepairAndFiniteTests.Identity()];
        matrices[3] = 0.5f;
        GlbBuilder builder = AddSkinnedTriangle(jointCount: 2, inverseBindMatrices: matrices);

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_JointIndexAboveSkinJointCount_Throws()
    {
        GlbBuilder builder = AddSkinnedTriangle(jointCount: 2, inverseBindMatrices: null, maxJoint: 2);

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_SkinnedNodeWithoutJointAttributes_Throws()
    {
        GlbBuilder builder = new();
        builder.AddTriangle();
        builder.Add("nodes", new JsonObject());
        builder.Json["nodes"]![0]!["skin"] = 0;
        builder.Add("skins", new JsonObject { ["joints"] = new JsonArray(1) });

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_SkinWithoutMesh_Throws()
    {
        GlbBuilder builder = new();
        builder.Add("nodes", new JsonObject { ["skin"] = 0 });
        builder.Add("skins", new JsonObject { ["joints"] = new JsonArray(0) });

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [TestCase("[]")]
    [TestCase("[7]")]
    [TestCase("[0, 0]")]
    public void Read_InvalidSkinJoints_Throws(string joints)
    {
        GlbBuilder builder = new();
        builder.Add("nodes", new JsonObject());
        builder.Add("skins", new JsonObject { ["joints"] = JsonNode.Parse(joints) });

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_SkinSkeleton_IsExposed()
    {
        GlbBuilder builder = AddSkinnedTriangle(jointCount: 2, inverseBindMatrices: null);
        builder.Json["skins"]![0]!["skeleton"] = 1;

        Assert.That(builder.Read().Skins[0].Skeleton, Is.EqualTo(1));
    }

    // A triangle on node 0 skinned to joints on nodes 1 and following; every vertex uses joint maxJoint.
    internal static GlbBuilder AddSkinnedTriangle(int jointCount, float[]? inverseBindMatrices, ushort maxJoint = 0)
    {
        GlbBuilder builder = new();
        JsonObject primitive = builder.AddTriangle();
        primitive["attributes"]!["JOINTS_0"] = builder.AddUShorts("VEC4", maxJoint, 0, 0, 0, maxJoint, 0, 0, 0, maxJoint, 0, 0, 0);
        primitive["attributes"]!["WEIGHTS_0"] = builder.AddFloats("VEC4", 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0);
        builder.Json["nodes"]![0]!["skin"] = 0;

        JsonArray joints = new();
        for (int i = 0; i < jointCount; i++)
        {
            joints.Add(builder.Add("nodes", new JsonObject()));
        }

        JsonObject skin = new() { ["joints"] = joints };
        if (inverseBindMatrices is not null)
        {
            skin["inverseBindMatrices"] = builder.AddFloats("MAT4", inverseBindMatrices);
        }

        builder.Add("skins", skin);
        return builder;
    }

    private static JsonArray ToJson(float[] values) => new(values.Select(value => (JsonNode?)value).ToArray());
}
