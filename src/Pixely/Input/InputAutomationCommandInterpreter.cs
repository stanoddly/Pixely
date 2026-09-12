using System.Globalization;
using System.Numerics;

namespace Pixely.Input;

/// <summary>
/// Runs one line of the text command grammar against <see cref="IInputAutomation"/> and returns the reply line:
/// <c>ok</c>, <c>error: ...</c> for a malformed line, or null for a blank or <c>#</c> comment line that gets no reply.
/// Exceptions from input handlers propagate, the same as for real input.
/// </summary>
internal sealed class InputAutomationCommandInterpreter
{
    private readonly IInputAutomation _automation;

    public InputAutomationCommandInterpreter(IInputAutomation automation)
    {
        _automation = automation;
    }

    public string? Execute(string line)
    {
        string command = line.TrimStart();
        if (command.Length == 0 || command[0] == '#')
        {
            return null;
        }

        try
        {
            Run(command);
            return "ok";
        }
        catch (FormatException exception)
        {
            return $"error: {exception.Message}";
        }
    }

    private void Run(string command)
    {
        string[] words = command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        switch (words)
        {
            case ["mouse", "move", string x, string y]:
                _automation.MouseMoveTo(ParseVector(x, y));
                break;
            case ["mouse", "moveby", string dx, string dy]:
                _automation.MouseMoveBy(ParseVector(dx, dy));
                break;
            case ["mouse", "down", string button, string x, string y]:
                _automation.MouseDown(ParseEnum<MouseButton>(button), ParseVector(x, y));
                break;
            case ["mouse", "up", string button, string x, string y]:
                _automation.MouseUp(ParseEnum<MouseButton>(button), ParseVector(x, y));
                break;
            case ["mouse", "click", string button, string x, string y]:
                _automation.MouseClick(ParseEnum<MouseButton>(button), ParseVector(x, y));
                break;
            case ["mouse", "wheel", string dx, string dy, string x, string y]:
                _automation.MouseWheel(ParseVector(dx, dy), ParseVector(x, y));
                break;
            case ["key", "down", string scancode]:
                _automation.KeyDown(ParseEnum<Scancode>(scancode));
                break;
            case ["key", "up", string scancode]:
                _automation.KeyUp(ParseEnum<Scancode>(scancode));
                break;
            case ["key", "press", string scancode]:
                _automation.KeyPress(ParseEnum<Scancode>(scancode));
                break;
            case ["text", ..]:
                _automation.TextInput(command["text".Length..].TrimStart());
                break;
            default:
                throw new FormatException($"unknown command '{command}'");
        }
    }

    private static Vector2 ParseVector(string x, string y)
    {
        return new Vector2(ParseFloat(x), ParseFloat(y));
    }

    private static float ParseFloat(string word)
    {
        if (!float.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
        {
            throw new FormatException($"invalid number '{word}'");
        }

        return value;
    }

    private static TEnum ParseEnum<TEnum>(string word) where TEnum : struct, Enum
    {
        if (!Enum.TryParse(word, ignoreCase: true, out TEnum value) || !Enum.IsDefined(value))
        {
            throw new FormatException($"unknown {typeof(TEnum).Name} '{word}'");
        }

        return value;
    }
}
