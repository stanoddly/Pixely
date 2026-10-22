namespace Pixely.Input;

/// <summary>
/// A game's own command for the headless command grammar, registered through the service collection. It runs on the frame
/// loop at <see cref="UpdateOrders.Input"/> like the built-in commands, and only in headless mode.
/// </summary>
public interface IAutomationCommand
{
    /// <summary>The first word of the command, matched case-sensitively. It must not be a built-in command name.</summary>
    string Name { get; }

    /// <summary>
    /// Runs the command. <paramref name="arguments"/> is the rest of the command after its name, trimmed.
    /// <paramref name="viewScope"/> is the scope of an <c>@&lt;n&gt;</c> prefix, or null without one. Throw
    /// <see cref="FormatException"/> for a malformed command; any exception ends the app.
    /// </summary>
    void Execute(string arguments, ViewScope? viewScope);
}
