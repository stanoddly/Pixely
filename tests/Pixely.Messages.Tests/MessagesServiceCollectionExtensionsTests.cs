using Pixely.DependencyInjection;

namespace Pixely.Messages.Tests;

[TestFixture]
public sealed class MessagesServiceCollectionExtensionsTests
{
    [Test]
    public void AddMessages_GivesEachConsumerItsOwnReader()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddMessages<TestMessage>();
        using ServiceProvider provider = services.BuildServiceProvider();

        MessageReader<TestMessage> first = provider.GetRequiredService<MessageReader<TestMessage>>();
        MessageReader<TestMessage> second = provider.GetRequiredService<MessageReader<TestMessage>>();
        provider.GetRequiredService<MessageWriter<TestMessage>>().Write(new TestMessage(1));

        Assert.That(first, Is.Not.SameAs(second));
        Assert.That(first.TryRead(out TestMessage fromFirst), Is.True);
        Assert.That(second.TryRead(out TestMessage fromSecond), Is.True);
        Assert.That(fromFirst, Is.EqualTo(new TestMessage(1)));
        Assert.That(fromSecond, Is.EqualTo(new TestMessage(1)));
    }

    [Test]
    public void AddMessages_RegistersTheMessagesAsAnUpdatable()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddRegistry<IUpdatable>(static updatable => updatable.UpdateOrder);
        services.AddMessages<TestMessage>();
        using ServiceProvider provider = services.BuildServiceProvider();

        Messages<TestMessage> messages = provider.GetRequiredService<Messages<TestMessage>>();

        Assert.That(provider.GetRequiredService<ServiceRegistry<IUpdatable>>(), Has.Member(messages));
    }

    [Test]
    public void AddMessages_CalledTwiceRegistersOnce()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddMessages<TestMessage>();
        services.AddMessages<TestMessage>();
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.That(provider.GetServices<Messages<TestMessage>>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void AddMessages_InAChildCollectionKeepsTheParentsMessages()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddRegistry<IUpdatable>(static updatable => updatable.UpdateOrder);
        services.AddMessages<TestMessage>();
        using ServiceProvider parent = services.BuildServiceProvider();
        ServiceCollection childServices = parent.CreateServiceCollection();
        childServices.AddMessages<TestMessage>();
        using ServiceProvider child = childServices.BuildServiceProvider();

        MessageReader<TestMessage> reader = child.GetRequiredService<MessageReader<TestMessage>>();
        parent.GetRequiredService<MessageWriter<TestMessage>>().Write(new TestMessage(1));

        Assert.That(child.GetRequiredService<Messages<TestMessage>>(), Is.SameAs(parent.GetRequiredService<Messages<TestMessage>>()));
        Assert.That(parent.GetRequiredService<ServiceRegistry<IUpdatable>>().Count(), Is.EqualTo(1));
        Assert.That(reader.TryRead(out TestMessage message), Is.True);
        Assert.That(message, Is.EqualTo(new TestMessage(1)));
    }
}
