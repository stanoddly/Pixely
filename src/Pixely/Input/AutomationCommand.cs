using System.Collections.Frozen;

namespace Pixely.Input;

/// <summary>
/// A game's own command for the headless command grammar, registered through the service collection of the app or of a
/// stage. It runs on the frame loop at <see cref="UpdateOrders.Input"/> like the built-in commands, in headless mode or with
/// <see cref="PixelyConfig.EnableCommandConsole"/>.
/// </summary>
public abstract class AutomationCommand
{
    private static readonly FrozenSet<string> BuiltInCommandNames = FrozenSet.Create(StringComparer.Ordinal, "wait", "quit", "gamepad", "mouse", "key", "text", "screenshot");

    /// <param name="name">
    /// The first word of the command, matched case-sensitively: not empty, without whitespace or <c>;</c>, not starting with
    /// <c>@</c> and not a built-in command name.
    /// </param>
    protected AutomationCommand(string name)
    {
        // The interpreter matches the first word of a command after an optional "@<n>", so any other name never runs.
        if (string.IsNullOrEmpty(name) || name[0] == '@' || name.Contains(';') || name.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException($"Automation command '{name}' of {GetType()} is not a single word without '@' at its start or ';'.", nameof(name));
        }

        if (BuiltInCommandNames.Contains(name))
        {
            throw new ArgumentException($"Automation command '{name}' of {GetType()} has the name of a built-in command.", nameof(name));
        }

        Name = name;
    }

    public string Name { get; }

    /// <summary>
    /// Runs the command. <paramref name="arguments"/> is the rest of the command after its name, trimmed.
    /// <paramref name="viewScope"/> is the scope of an <c>@&lt;n&gt;</c> prefix, or null without one. Throw
    /// <see cref="FormatException"/> for a malformed command: it ends the app in headless mode, and the command console reports it
    /// and goes on. Any other exception ends the app.
    /// </summary>
    public abstract void Execute(string arguments, ViewScope? viewScope);
}
