using System.Runtime.Versioning;
using System.Text;

namespace Pixely.Input;

/// <summary>
/// Runs <c>;</c>-terminated commands from a text stream on the frame loop, in lockstep: game time advances only through
/// <c>wait</c>, and while no command is left to run the frame blocks until the next one arrives. The end of the input quits
/// the app. The browser has no standard input, so the factory registers no console there.
/// </summary>
[UnsupportedOSPlatform("browser")]
internal sealed class InputAutomationConsole : IUpdatable
{
    private readonly InputAutomationCommandInterpreter _interpreter;
    private readonly TextReader _input;
    private readonly AppControl _appControl;
    private readonly StringBuilder _command = new();
    private int _heldFrames;

    internal InputAutomationConsole(InputAutomationCommandInterpreter interpreter, TextReader input, AppControl appControl)
    {
        _interpreter = interpreter;
        _input = input;
        _appControl = appControl;
    }

    public int UpdateOrder => UpdateOrders.Input;

    public void Update()
    {
        if (_heldFrames > 0 && --_heldFrames > 0)
        {
            return;
        }

        // After a quit the frame is the last one, so nothing after it is read.
        while (_heldFrames == 0 && !_appControl.QuitRequested)
        {
            if (ReadCommand() is not { } command)
            {
                _appControl.Quit();
                return;
            }

            _heldFrames = _interpreter.Execute(command);
        }
    }

    // One character at a time, so the read returns as soon as a ';' is buffered instead of waiting to fill a larger buffer.
    // Null at the end of the input.
    private string? ReadCommand()
    {
        int character;
        while ((character = _input.Read()) != -1)
        {
            if (character == ';')
            {
                string command = _command.ToString();
                _command.Clear();
                return command;
            }

            _command.Append((char)character);
        }

        string rest = _command.ToString().Trim();
        if (rest.Length > 0)
        {
            throw new FormatException($"unterminated command '{rest}'");
        }

        return null;
    }
}
