using Pixely.Pencuil;

namespace Pixely.Tests;

public sealed class ModelViewTests
{
    [Test]
    public void Value_InitiallyContainsConstructorValue()
    {
        TestValue value = new(4, 2.5f);

        ModelView<TestValue> modelView = new(value);

        Assert.Multiple(() =>
        {
            Assert.That(modelView.Value, Is.EqualTo(value));
            Assert.That(modelView.Dirty, Is.False);
        });
    }

    [Test]
    public void Value_SetToDifferentValue_MarksDirty()
    {
        ModelView<int> modelView = new(1);

        modelView.Value = 2;

        Assert.Multiple(() =>
        {
            Assert.That(modelView.Value, Is.EqualTo(2));
            Assert.That(modelView.Dirty, Is.True);
        });
    }

    [Test]
    public void Value_SetToEqualValue_RemainsClean()
    {
        ModelView<int> modelView = new(1);

        modelView.Value = 1;

        Assert.That(modelView.Dirty, Is.False);
    }

    [Test]
    public void SetValue_SetToEqualValue_MarksDirty()
    {
        TestValue value = new(4, 2.5f);
        ModelView<TestValue> modelView = new(value);

        modelView.SetValue(in value);

        Assert.That(modelView.Dirty, Is.True);
    }

    [Test]
    public void GetValue_ReturnsReferenceToStoredValue()
    {
        ModelView<TestValue> modelView = new(new TestValue(4, 2.5f));
        ref readonly TestValue value = ref modelView.GetValue();
        TestValue replacement = new(8, 5f);

        modelView.SetValue(in replacement);

        Assert.That(value, Is.EqualTo(replacement));
    }

    [Test]
    public void PencuilView_ConsumesDirtyState()
    {
        ModelView<int> modelView = new(1);
        TestView view = new(modelView);
        modelView.Value = 2;

        bool dirty = view.ConsumeDirty();

        Assert.Multiple(() =>
        {
            Assert.That(dirty, Is.True);
            Assert.That(modelView.Dirty, Is.False);
            Assert.That(view.ConsumeDirty(), Is.False);
        });
    }

    private readonly record struct TestValue(int Count, float Scale);

    private sealed class TestView : PencuilView<ModelView<int>>
    {
        internal TestView(ModelView<int> modelView)
            : base(modelView)
        {
        }

        public override void Build(Pencil pencil)
        {
        }
    }
}
