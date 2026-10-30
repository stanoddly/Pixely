using System.Collections.Concurrent;
using System.Runtime.Versioning;
using System.Text;

namespace Pixely.Input;

/// <summary>
/// Runs a game's <see cref="AutomationCommand"/>s and <c>quit</c> from a text stream outside headless mode
/// (<see cref="PixelyConfig.EnableCommandConsole"/>). A background thread reads the input, so a frame never waits for it, and
/// each update runs the commands read since the last one. A newline or a <c>;</c> ends a command. A malformed or unknown
/// command is written to the error writer and the app goes on; any other exception from a command ends the app.
/// </summary>
[UnsupportedOSPlatform("browser")]
internal sealed class AutomationCommandConsole : IUpdatable
{
    private readonly AutomationCommandRegistry _commands;
    private readonly TextReader _input;
    private readonly TextWriter _error;
    private readonly AppControl _appControl;
    private readonly ConcurrentQueue<string> _readCommands = new();
    private Thread? _reader;

    internal AutomationCommandConsole(AutomationCommandRegistry commands, TextReader input, TextWriter error, AppControl appControl)
    {
        _commands = commands;
        _input = input;
        _error = error;
        _appControl = appControl;
    }

    public int UpdateOrder => UpdateOrders.Input;

    public void Update()
    {
        // Started by the first frame rather than at build, so an app that is built but never run reads nothing. A background
        // thread, because a read blocked on a terminal cannot be cancelled and must not keep the process alive after the app ends.
        if (_reader is null)
        {
            _reader = new Thread(Read) { IsBackground = true, Name = "Pixely command console" };
            _reader.Start();
        }

        // After a quit the frame is the last one, so the commands after it do not run.
        while (!_appControl.QuitRequested && _readCommands.TryDequeue(out string? command))
        {
            Execute(command.Trim());
        }
    }

    private void Execute(string command)
    {
        if (command.Length == 0)
        {
            return;
        }

        try
        {
            string rest = AutomationCommandRegistry.SplitViewScope(command, out ViewScope? viewScope);
            if (rest == "quit")
            {
                if (viewScope is not null)
                {
                    throw new FormatException("'quit' takes no view scope");
                }

                _appControl.Quit();
            }
            else if (!_commands.TryExecute(rest, viewScope))
            {
                throw new FormatException($"unknown command '{rest}'");
            }
        }
        catch (FormatException exception)
        {
            _error.WriteLine($"Command '{command}' failed: {exception.Message}");
        }
    }

    // Ends at the end of the input, which leaves the app running without the console.
    private void Read()
    {
        StringBuilder command = new();
        int character;
        while ((character = ReadCharacter()) != -1)
        {
            if (character is ';' or '\n')
            {
                _readCommands.Enqueue(command.ToString());
                command.Clear();
            }
            else
            {
                command.Append((char)character);
            }
        }

        _readCommands.Enqueue(command.ToString());
    }

    // A failed read ends the input. The reader is a background thread, so an exception escaping it would end the process,
    // and a console for cheats must not take the game down with it. Such a read is, for example, standard input whose
    // terminal has gone away, or an interrupted system call (EINTR), which a blocked read of a pipe has failed with on Linux CI.
    private int ReadCharacter()
    {
        try
        {
            return _input.Read();
        }
        catch (IOException exception)
        {
            _error.WriteLine($"Reading commands failed, so the command console stops: {exception.Message}");
            return -1;
        }
    }
}
