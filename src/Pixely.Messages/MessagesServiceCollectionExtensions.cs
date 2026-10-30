using Pixely.DependencyInjection;

namespace Pixely.Messages;

public static class MessagesServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="Messages{TMessage}"/>, a <see cref="MessageWriter{TMessage}"/> over it, and a
    /// <see cref="MessageReader{TMessage}"/> as a transient, so each consumer that takes a reader gets its own. A child
    /// collection keeps its parent's.
    /// </summary>
    public static void AddMessages<TMessage>(this ServiceCollection services)
    {
        if (services.IsRegistered<Messages<TMessage>>())
        {
            return;
        }

        services.AddSingleton(static _ => new Messages<TMessage>());
        services.AddSingleton(static provider => new MessageWriter<TMessage>(provider.GetRequiredService<Messages<TMessage>>()));
        services.AddTransient(static provider => new MessageReader<TMessage>(provider.GetRequiredService<Messages<TMessage>>()));
    }
}
