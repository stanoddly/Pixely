using Pixely.Pencuil;

namespace Pixely.Tests;

public sealed class ViewModelTests
{
    [Test]
    public void Value_InitiallyContainsConstructorValue()
    {
        TestValue value = new(4, 2.5f);

        ViewModel<TestValue> viewModel = new(value);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Value, Is.EqualTo(value));
            Assert.That(viewModel.Dirty, Is.False);
        });
    }

    [Test]
    public void Value_SetToDifferentValue_MarksDirty()
    {
        ViewModel<int> viewModel = new(1);

        viewModel.Value = 2;

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Value, Is.EqualTo(2));
            Assert.That(viewModel.Dirty, Is.True);
        });
    }

    [Test]
    public void Value_SetToEqualValue_RemainsClean()
    {
        ViewModel<int> viewModel = new(1);

        viewModel.Value = 1;

        Assert.That(viewModel.Dirty, Is.False);
    }

    [Test]
    public void SetValue_SetToEqualValue_MarksDirty()
    {
        TestValue value = new(4, 2.5f);
        ViewModel<TestValue> viewModel = new(value);

        viewModel.SetValue(in value);

        Assert.That(viewModel.Dirty, Is.True);
    }

    [Test]
    public void GetValue_ReturnsReferenceToStoredValue()
    {
        ViewModel<TestValue> viewModel = new(new TestValue(4, 2.5f));
        ref readonly TestValue value = ref viewModel.GetValue();
        TestValue replacement = new(8, 5f);

        viewModel.SetValue(in replacement);

        Assert.That(value, Is.EqualTo(replacement));
    }

    [Test]
    public void PencuilView_ConsumesDirtyState()
    {
        ViewModel<int> viewModel = new(1);
        TestView view = new(viewModel);
        viewModel.Value = 2;

        bool dirty = view.ConsumeDirty();

        Assert.Multiple(() =>
        {
            Assert.That(dirty, Is.True);
            Assert.That(viewModel.Dirty, Is.False);
            Assert.That(view.ConsumeDirty(), Is.False);
        });
    }

    private readonly record struct TestValue(int Count, float Scale);

    private sealed class TestView : PencuilView<int>
    {
        internal TestView(ViewModel<int> viewModel)
            : base(viewModel)
        {
        }

        public override void Build(Pencil pencil)
        {
        }
    }
}
