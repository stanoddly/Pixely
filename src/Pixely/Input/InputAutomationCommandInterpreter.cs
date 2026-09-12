using System.Globalization;
using System.Numerics;
using Pixely.Content;
using Pixely.RenderOrchestration;

namespace Pixely.Input;

/// <summary>
/// Runs one line of the text command grammar against <see cref="IInputAutomation"/> and sends one reply line per command:
/// <c>ok</c>, or <c>error: ...</c> for a line that cannot run. Blank and <c>#</c> comment lines get no reply.
/// A <c>screenshot</c> replies once the next frame has been rendered and written. Exceptions from input handlers propagate, the same as for real input.
/// </summary>
internal sealed class InputAutomationCommandInterpreter
{
    private readonly IInputAutomation _automation;
    private readonly IFrameCapture? _frameCapture;
    private readonly IImageWriter _imageWriter;
    private readonly Action<string> _reply;

    public InputAutomationCommandInterpreter(IInputAutomation automation, IFrameCapture? frameCapture, IImageWriter imageWriter, Action<string> reply)
    {
        _automation = automation;
        _frameCapture = frameCapture;
        _imageWriter = imageWriter;
        _reply = reply;
    }

    public void Execute(string line)
    {
        string command = line.TrimStart();
        if (command.Length == 0 || command[0] == '#')
        {
            return;
        }

        try
        {
            string? reply = Run(command);
            if (reply is not null)
            {
                _reply(reply);
            }
        }
        catch (FormatException exception)
        {
            _reply($"error: {exception.Message}");
        }
    }

    // Returns the reply, or null when the command replies later on its own.
    private string? Run(string command)
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
            case ["screenshot", _, ..]:
                return Screenshot(command["screenshot".Length..].Trim());
            default:
                throw new FormatException($"unknown command '{command}'");
        }

        return "ok";
    }

    private string? Screenshot(string path)
    {
        if (_frameCapture is null)
        {
            return "error: screenshots need offscreen rendering, register it with UseOffscreenRendering()";
        }

        _frameCapture.CaptureNextFrame(image =>
        {
            try
            {
                _imageWriter.SavePng(image, path);
                _reply("ok");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _reply($"error: {exception.Message}");
            }
        });
        return null;
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
