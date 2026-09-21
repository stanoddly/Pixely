using System.Globalization;
using System.Numerics;
using Pixely.Content;

namespace Pixely.Input;

/// <summary>
/// Runs one line of the text command grammar against <see cref="InputAutomation"/>. A blank or <c>#</c> comment line does
/// nothing; a line that cannot run throws <see cref="FormatException"/>. Exceptions from input handlers propagate, the same
/// as for real input.
/// </summary>
internal sealed class InputAutomationCommandInterpreter
{
    private readonly InputAutomation _automation;
    private readonly WindowRegistry _windowRegistry;
    private readonly IImageWriter _imageWriter;

    public InputAutomationCommandInterpreter(InputAutomation automation, WindowRegistry windowRegistry, IImageWriter imageWriter)
    {
        _automation = automation;
        _windowRegistry = windowRegistry;
        _imageWriter = imageWriter;
    }

    public void Execute(string line)
    {
        string command = line.TrimStart();
        if (command.Length == 0 || command[0] == '#')
        {
            return;
        }

        ViewScope viewScope = default;
        // An optional "@<n>" prefix picks the window by ViewScope value; the default scope needs none.
        if (command[0] == '@')
        {
            int scopeEnd = command.IndexOf(' ');
            string scopeWord = scopeEnd < 0 ? command[1..] : command[1..scopeEnd];
            if (!int.TryParse(scopeWord, NumberStyles.Integer, CultureInfo.InvariantCulture, out int scopeValue))
            {
                throw new FormatException($"invalid view scope '{scopeWord}'");
            }

            viewScope = new ViewScope(scopeValue);
            command = scopeEnd < 0 ? string.Empty : command[(scopeEnd + 1)..].TrimStart();
        }

        if (!_windowRegistry.TryGetWindow(viewScope, out Window window))
        {
            throw new FormatException($"no window for view scope {viewScope.Value}");
        }

        string[] words = command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        switch (words)
        {
            case ["mouse", "move", string x, string y]:
                _automation.MouseMoveTo(ParseVector(x, y), viewScope);
                break;
            case ["mouse", "moveby", string dx, string dy]:
                _automation.MouseMoveBy(ParseVector(dx, dy), viewScope);
                break;
            case ["mouse", "down", string button, string x, string y]:
                _automation.MouseDown(ParseEnum<MouseButton>(button), ParseVector(x, y), viewScope);
                break;
            case ["mouse", "up", string button, string x, string y]:
                _automation.MouseUp(ParseEnum<MouseButton>(button), ParseVector(x, y), viewScope);
                break;
            case ["mouse", "click", string button, string x, string y]:
                _automation.MouseClick(ParseEnum<MouseButton>(button), ParseVector(x, y), viewScope);
                break;
            case ["mouse", "wheel", string dx, string dy, string x, string y]:
                _automation.MouseWheel(ParseVector(dx, dy), ParseVector(x, y), viewScope);
                break;
            case ["mouse", "leave"]:
                _automation.MouseLeave(viewScope);
                break;
            case ["key", "down", string scancode]:
                _automation.KeyDown(ParseEnum<Scancode>(scancode), viewScope);
                break;
            case ["key", "up", string scancode]:
                _automation.KeyUp(ParseEnum<Scancode>(scancode), viewScope);
                break;
            case ["key", "press", string scancode]:
                _automation.KeyPress(ParseEnum<Scancode>(scancode), viewScope);
                break;
            case ["text", ..]:
                _automation.TextInput(command["text".Length..].TrimStart(), viewScope);
                break;
            case ["screenshot", _, ..]:
                Screenshot(window, command["screenshot".Length..].Trim());
                break;
            default:
                throw new FormatException($"unknown command '{command}'");
        }
    }

    private void Screenshot(Window window, string path)
    {
#if BROWSER
        throw new PlatformNotSupportedException("Headless mode is not supported in the browser.");
#else
        // Every window is offscreen in headless mode, which is the only mode this console exists in.
        using Image image = ((OffscreenWindow)window).CaptureLastFrame();
        _imageWriter.SavePng(image, path);
#endif
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
