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
    private const int ReadBufferSize = 4096;

    private readonly InputAutomationCommandInterpreter _interpreter;
    private readonly TextReader _input;
    private readonly AppControl _appControl;
    // The commands completed by one read stay together, so a chain sent in one write is not split across frames.
    private readonly ConcurrentQueue<ReadCommand[]> _readBatches = new();
    // Commands taken from the reader that a wait still holds.
    private readonly Queue<ReadCommand> _pendingCommands = new();
    private int _heldFrames;
    private Thread? _readerThread;

    internal InputAutomationConsole(InputAutomationCommandInterpreter interpreter, TextReader input, AppControl appControl)
    {
        _interpreter = interpreter;
        _input = input;
        _appControl = appControl;
    }

    public int UpdateOrder => UpdateOrders.Input;

    public void Update()
    {
        _readerThread ??= StartReader();

        // Only what was read when the console runs, so a fast writer cannot hold the frame.
        int batchCount = _readBatches.Count;
        for (int i = 0; i < batchCount && _readBatches.TryDequeue(out ReadCommand[]? batch); i++)
        {
            foreach (ReadCommand command in batch)
            {
                _pendingCommands.Enqueue(command);
            }
        }

        if (_heldFrames > 0 && --_heldFrames > 0)
        {
            return;
        }

        // After a quit the frame is the last one, so the commands after it do not run.
        while (_heldFrames == 0 && !_appControl.QuitRequested && _pendingCommands.TryDequeue(out ReadCommand command))
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
        char[] buffer = new char[ReadBufferSize];
        StringBuilder command = new();
        List<ReadCommand> batch = new();
        int readCount;
        while ((readCount = _input.Read(buffer, 0, buffer.Length)) > 0)
        {
            foreach (char character in buffer.AsSpan(0, readCount))
            {
                if (character == ';')
                {
                    batch.Add(new ReadCommand(command.ToString(), IsTerminated: true));
                    command.Clear();
                }
                else
                {
                    command.Append(character);
                }
            }

            if (batch.Count > 0)
            {
                _readBatches.Enqueue(batch.ToArray());
                batch.Clear();
            }
        }

        string rest = command.ToString();
        if (!string.IsNullOrWhiteSpace(rest))
        {
            _readBatches.Enqueue([new ReadCommand(rest, IsTerminated: false)]);
        }
    }

    private readonly record struct ReadCommand(string Text, bool IsTerminated);
}
