using System.Globalization;
using System.Numerics;
using System.Runtime.Versioning;
using Pixely.Content;
using SDL;

namespace Pixely.Input;

/// <summary>
/// Runs one command of the text command grammar, without its terminating <c>;</c>, against <see cref="InputAutomation"/> and
/// returns how many frames to hold the commands after it. A blank command does nothing; a command that cannot run throws
/// <see cref="FormatException"/>. Exceptions from input handlers propagate, the same as for real input.
/// </summary>
[UnsupportedOSPlatform("browser")]
internal sealed class InputAutomationCommandInterpreter
{
    // Below this a single frame would take over 3 seconds of real time.
    private const double MinimumSpeed = 0.01;

    private readonly InputAutomation _automation;
    private readonly WindowRegistry _windowRegistry;
    private readonly IImageWriter _imageWriter;
    private readonly HeadlessClock _headlessClock;
    private readonly AppControl _appControl;

    public InputAutomationCommandInterpreter(InputAutomation automation, WindowRegistry windowRegistry, IImageWriter imageWriter, HeadlessClock headlessClock,
        AppControl appControl)
    {
        _automation = automation;
        _windowRegistry = windowRegistry;
        _imageWriter = imageWriter;
        _headlessClock = headlessClock;
        _appControl = appControl;
    }

    public int Execute(string command)
    {
        command = command.Trim();
        if (command.Length == 0)
        {
            return 0;
        }

        ViewScope? viewScope = null;
        // An optional "@<n>" prefix picks the window by ViewScope value; the default scope needs none.
        if (command[0] == '@')
        {
            int scopeEnd = 1;
            while (scopeEnd < command.Length && !char.IsWhiteSpace(command[scopeEnd]))
            {
                scopeEnd++;
            }

            string scopeWord = command[1..scopeEnd];
            if (!int.TryParse(scopeWord, NumberStyles.Integer, CultureInfo.InvariantCulture, out int scopeValue))
            {
                throw new FormatException($"invalid view scope '{scopeWord}'");
            }

            viewScope = new ViewScope(scopeValue);
            command = command[scopeEnd..].TrimStart();
        }

        string[] words = command.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        switch (words)
        {
            case ["wait", string frames]:
                RejectViewScope(viewScope, command);
                return ParseFrameCount(frames);
            case ["speed", string factor]:
                RejectViewScope(viewScope, command);
                _headlessClock.Speed = ParseSpeed(factor);
                return 0;
            case ["quit"]:
                RejectViewScope(viewScope, command);
                _appControl.Quit();
                return 0;
            case ["gamepad", ..]:
                RejectViewScope(viewScope, command);
                ExecuteGamepad(words, command);
                return 0;
        }

        ExecuteWindowCommand(words, command, viewScope ?? default);
        return 0;
    }

    private void ExecuteWindowCommand(string[] words, string command, ViewScope viewScope)
    {
        if (!_windowRegistry.TryGetWindow(viewScope, out Window window))
        {
            throw new FormatException($"no window for view scope {viewScope.Value}");
        }

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
                _automation.TextInput(command["text".Length..].Trim(), viewScope);
                break;
            case ["screenshot", _, ..]:
                Screenshot(window, command["screenshot".Length..].Trim());
                break;
            default:
                throw new FormatException($"unknown command '{command}'");
        }
    }

    private void ExecuteGamepad(string[] words, string command)
    {
        switch (words)
        {
            case ["gamepad", "connect"]:
                if (_automation.IsGamepadConnected)
                {
                    throw new FormatException("gamepad is already connected");
                }

                _automation.GamepadConnect();
                break;
            case ["gamepad", "disconnect"]:
                RequireGamepad();
                _automation.GamepadDisconnect();
                break;
            case ["gamepad", "down", string button]:
            {
                GamepadButton gamepadButton = ParseGamepadButton(button);
                RequireGamepad();
                _automation.GamepadButtonDown(gamepadButton);
                break;
            }
            case ["gamepad", "up", string button]:
            {
                GamepadButton gamepadButton = ParseGamepadButton(button);
                RequireGamepad();
                _automation.GamepadButtonUp(gamepadButton);
                break;
            }
            case ["gamepad", "press", string button]:
            {
                GamepadButton gamepadButton = ParseGamepadButton(button);
                RequireGamepad();
                _automation.GamepadButtonPress(gamepadButton);
                break;
            }
            case ["gamepad", "stick", ("left" or "right") and string side, string x, string y]:
            {
                float xValue = ParseAxisValue(x, -1);
                float yValue = ParseAxisValue(y, -1);
                RequireGamepad();
                bool isLeft = side == "left";
                _automation.GamepadAxisMotion(isLeft ? SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX : SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTX, xValue);
                _automation.GamepadAxisMotion(isLeft ? SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTY : SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTY, yValue);
                break;
            }
            case ["gamepad", "trigger", ("left" or "right") and string side, string value]:
            {
                float triggerValue = ParseAxisValue(value, 0);
                RequireGamepad();
                _automation.GamepadAxisMotion(side == "left" ? SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER : SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHT_TRIGGER, triggerValue);
                break;
            }
            default:
                throw new FormatException($"unknown command '{command}'");
        }
    }

    private void RequireGamepad()
    {
        if (!_automation.IsGamepadConnected)
        {
            throw new FormatException("gamepad is not connected");
        }
    }

    private static void RejectViewScope(ViewScope? viewScope, string command)
    {
        if (viewScope != null)
        {
            throw new FormatException($"'{command}' takes no view scope");
        }
    }

    private void Screenshot(Window window, string path)
    {
        // Every window is offscreen in headless mode, which is the only mode this console exists in.
        using Image image = ((OffscreenWindow)window).CaptureLastFrame();
        _imageWriter.SavePng(image, path);
    }

    private static int ParseFrameCount(string word)
    {
        if (!int.TryParse(word, NumberStyles.None, CultureInfo.InvariantCulture, out int frames))
        {
            throw new FormatException($"invalid frame count '{word}'");
        }

        return frames;
    }

    private static double ParseSpeed(string word)
    {
        if (!double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out double speed) || !double.IsFinite(speed))
        {
            throw new FormatException($"invalid number '{word}'");
        }

        if (speed != 0 && speed < MinimumSpeed)
        {
            throw new FormatException($"invalid speed '{word}', expected 0 or at least {MinimumSpeed.ToString(CultureInfo.InvariantCulture)}");
        }

        return speed;
    }

    private static float ParseAxisValue(string word, float minimum)
    {
        float value = ParseFloat(word);
        if (value < minimum || value > 1)
        {
            throw new FormatException($"invalid axis value '{word}', expected {minimum.ToString(CultureInfo.InvariantCulture)} to 1");
        }

        return value;
    }

    private static GamepadButton ParseGamepadButton(string word)
    {
        GamepadButton button = ParseEnum<GamepadButton>(word);
        if (button is GamepadButton.Invalid or GamepadButton.Count)
        {
            throw new FormatException($"unknown GamepadButton '{word}'");
        }

        return button;
    }

    private static Vector2 ParseVector(string x, string y)
    {
        return new Vector2(ParseFloat(x), ParseFloat(y));
    }

    private static float ParseFloat(string word)
    {
        if (!float.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || !float.IsFinite(value))
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
