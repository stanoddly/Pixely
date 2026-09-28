using System.Collections.Concurrent;
using System.Runtime.Versioning;
using System.Text;

namespace Pixely.Input;

/// <summary>
/// Reads <c>;</c>-terminated commands from a text stream on a background thread and runs them on the frame loop. The browser has
/// neither a standard input nor a reader thread, so the factory registers no console there.
/// </summary>
[UnsupportedOSPlatform("browser")]
internal sealed class InputAutomationConsole : IUpdatable
{
    private readonly InputAutomationCommandInterpreter _interpreter;
    private readonly TextReader _input;
    private readonly ConcurrentQueue<ReadCommand> _readCommands = new();
    // Commands taken from the reader that a wait still holds.
    private readonly Queue<ReadCommand> _pendingCommands = new();
    private int _heldFrames;
    private Thread? _readerThread;

    internal InputAutomationConsole(InputAutomationCommandInterpreter interpreter, TextReader input)
    {
        _interpreter = interpreter;
        _input = input;
    }

    public int UpdateOrder => UpdateOrders.Input;

    public void Update()
    {
        _readerThread ??= StartReader();

        // Only what was read when the console runs, so a fast writer cannot hold the frame.
        int readCount = _readCommands.Count;
        for (int i = 0; i < readCount && _readCommands.TryDequeue(out ReadCommand command); i++)
        {
            _pendingCommands.Enqueue(command);
        }

        if (_heldFrames > 0 && --_heldFrames > 0)
        {
            return;
        }

        while (_heldFrames == 0 && _pendingCommands.TryDequeue(out ReadCommand command))
        {
            if (!command.IsTerminated)
            {
                throw new FormatException($"unterminated command '{command.Text.Trim()}'");
            }

            _heldFrames = _interpreter.Execute(command.Text);
        }
    }

    private Thread StartReader()
    {
        Thread thread = new(ReadCommands) { IsBackground = true, Name = "Pixely input automation reader" };
        thread.Start();
        return thread;
    }

    // An error thrown here would end the process, so an unterminated command at the end is queued for the frame loop to throw.
    private void ReadCommands()
    {
        StringBuilder command = new();
        int character;
        while ((character = _input.Read()) >= 0)
        {
            if (character == ';')
            {
                _readCommands.Enqueue(new ReadCommand(command.ToString(), IsTerminated: true));
                command.Clear();
            }
            else
            {
                command.Append((char)character);
            }
        }

        string rest = command.ToString();
        if (!string.IsNullOrWhiteSpace(rest))
        {
            _readCommands.Enqueue(new ReadCommand(rest, IsTerminated: false));
        }
    }

    private readonly record struct ReadCommand(string Text, bool IsTerminated);
}
