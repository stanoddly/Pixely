using System.Diagnostics.CodeAnalysis;

namespace Pixely.Input;

/// <summary>
/// The active automation commands by name. Commands join when a provider creates them, the app's or a stage's, and leave when
/// that provider disposes them.
/// </summary>
internal sealed class AutomationCommandRegistry
{
    private readonly Dictionary<string, AutomationCommand> _commands = new(StringComparer.Ordinal);

    public void Add(AutomationCommand command)
    {
        if (!_commands.TryAdd(command.Name, command))
        {
            throw new InvalidOperationException($"Automation command '{command.Name}' is registered by both {_commands[command.Name].GetType()} and {command.GetType()}.");
        }
    }

    public void Remove(AutomationCommand command)
    {
        // A rejected duplicate is disposed too, and must not remove the command that holds its name.
        if (_commands.TryGetValue(command.Name, out AutomationCommand? registered) && ReferenceEquals(registered, command))
        {
            _commands.Remove(command.Name);
        }
    }

    public bool TryGet(string name, [NotNullWhen(true)] out AutomationCommand? command)
    {
        return _commands.TryGetValue(name, out command);
    }
}
