using System.Diagnostics.CodeAnalysis;
using System.Globalization;

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

    // Runs the active command named by the first word of a trimmed command without its "@<n>" prefix, with the rest of the
    // command trimmed. False when no active command has that name.
    public bool TryExecute(string command, ViewScope? viewScope)
    {
        int nameEnd = FindWordEnd(command, 0);
        if (!_commands.TryGetValue(command[..nameEnd], out AutomationCommand? found))
        {
            return false;
        }

        found.Execute(command[nameEnd..].Trim(), viewScope);
        return true;
    }

    // Splits an optional "@<n>" prefix, the value of a ViewScope, off a trimmed command and returns the rest of the command.
    public static string SplitViewScope(string command, out ViewScope? viewScope)
    {
        viewScope = null;
        if (command.Length == 0 || command[0] != '@')
        {
            return command;
        }

        int scopeEnd = FindWordEnd(command, 1);
        string scopeWord = command[1..scopeEnd];
        if (!int.TryParse(scopeWord, NumberStyles.Integer, CultureInfo.InvariantCulture, out int scopeValue))
        {
            throw new FormatException($"invalid view scope '{scopeWord}'");
        }

        viewScope = new ViewScope(scopeValue);
        return command[scopeEnd..].TrimStart();
    }

    private static int FindWordEnd(string command, int start)
    {
        int end = start;
        while (end < command.Length && !char.IsWhiteSpace(command[end]))
        {
            end++;
        }

        return end;
    }
}
