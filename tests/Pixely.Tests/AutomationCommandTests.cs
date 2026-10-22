using Pixely.Input;

namespace Pixely.Tests;

public sealed class AutomationCommandTests
{
    [TestCase(null)]
    [TestCase("")]
    [TestCase("spawn enemy")]
    [TestCase("spawn\t")]
    [TestCase("spawn;")]
    [TestCase("@spawn")]
    public void Constructor_NameThatCannotMatch_Throws(string? name)
    {
        ArgumentException? exception = Assert.Throws<ArgumentException>(() => new NamedCommand(name!));

        Assert.That(exception!.Message, Does.Contain($"'{name}'").And.Contain(nameof(NamedCommand)));
    }

    [TestCase("wait")]
    [TestCase("quit")]
    [TestCase("gamepad")]
    [TestCase("mouse")]
    [TestCase("key")]
    [TestCase("text")]
    [TestCase("screenshot")]
    public void Constructor_BuiltInName_Throws(string name)
    {
        ArgumentException? exception = Assert.Throws<ArgumentException>(() => new NamedCommand(name));

        Assert.That(exception!.Message, Does.Contain($"'{name}'").And.Contain("built-in"));
    }

    [TestCase("spawn")]
    // Built-in names match case-sensitively, so another case is a different name.
    [TestCase("Mouse")]
    [TestCase("give-gold")]
    public void Constructor_ValidName_KeepsIt(string name)
    {
        Assert.That(new NamedCommand(name).Name, Is.EqualTo(name));
    }

    [Test]
    public void Registry_Add_SameNameTwice_Throws()
    {
        AutomationCommandRegistry registry = new();
        registry.Add(new NamedCommand("spawn"));

        InvalidOperationException? exception = Assert.Throws<InvalidOperationException>(() => registry.Add(new NamedCommand("spawn")));

        Assert.That(exception!.Message, Does.Contain("'spawn'"));
    }

    [Test]
    public void Registry_Remove_RejectedDuplicate_KeepsRegisteredCommand()
    {
        AutomationCommandRegistry registry = new();
        NamedCommand registered = new("spawn");
        NamedCommand duplicate = new("spawn");
        registry.Add(registered);
        Assert.Throws<InvalidOperationException>(() => registry.Add(duplicate));

        registry.Remove(duplicate);

        Assert.That(registry.TryGet("spawn", out AutomationCommand? command) ? command : null, Is.SameAs(registered));
    }

    [Test]
    public void Registry_Remove_FreesName()
    {
        AutomationCommandRegistry registry = new();
        NamedCommand first = new("spawn");
        registry.Add(first);

        registry.Remove(first);

        Assert.Multiple(() =>
        {
            Assert.That(registry.TryGet("spawn", out _), Is.False);
            Assert.That(() => registry.Add(new NamedCommand("spawn")), Throws.Nothing);
        });
    }

    private sealed class NamedCommand(string name) : AutomationCommand(name)
    {
        public override void Execute(string arguments, ViewScope? viewScope)
        {
        }
    }
}
