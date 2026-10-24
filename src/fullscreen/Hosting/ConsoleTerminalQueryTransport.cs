using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using XenoAtom.Terminal;

namespace fullscreen.Hosting;

internal sealed class ConsoleTerminalQueryTransport(TerminalInstance terminal) : ITerminalQueryTransport {
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    public long ElapsedMilliseconds => _clock.ElapsedMilliseconds;
    public bool KeyAvailable => Console.KeyAvailable;
    public bool CanQuery =>
        terminal.Backend.GetType().FullName is "XenoAtom.Terminal.Backends.WindowsConsoleTerminalBackend"
            or "XenoAtom.Terminal.Backends.UnixTerminalBackend"
        && !terminal.IsInputRunning && !Console.IsInputRedirected && !Console.IsOutputRedirected
        && terminal.Capabilities.SupportsRawMode && TerminalProbeInput.CanPreserve;

    public IDisposable EnterQueryMode() {
        // The backend has been initialized, but its input task must not exist yet.
        if (terminal.IsInputRunning) {
            throw new InvalidOperationException("Cannot probe while the terminal input reader is running.");
        }
        terminal.Options.WindowsVtInputDecoder = TerminalWindowsVtInputDecoderMode.Enabled;
        var raw = terminal.Backend.UseRawMode(TerminalRawModeKind.Raw);
        try {
            return new QueryMode(raw, WindowsConsoleInputMode.EnterQuery());
        }
        catch {
            raw.Dispose();
            throw;
        }
    }

    public ConsoleKeyInfo Read() =>
        Console.ReadKey(intercept: true);
    public void Write(string query) {
        terminal.Out.Write(query);
        terminal.Out.Flush();
    }
    public void Wait() =>
        Thread.Sleep(1);
    public void PreserveInput(IReadOnlyList<ConsoleKeyInfo> keys) =>
        TerminalProbeInput.Preserve(terminal, keys);

    private sealed class QueryMode(IDisposable raw, IDisposable vt) : IDisposable {
        public void Dispose() {
            try {
                vt.Dispose();
            }
            finally {
                raw.Dispose();
            }
        }
    }
}

/// <summary>Input pushback compatibility for pinned XenoAtom.Terminal 2.2.0.</summary>
/// <remarks>No public pushback API exists. If the queue shape changes, probing is disabled
/// before writing anything; we must be able to preserve keys before taking ownership of input.</remarks>
internal static class TerminalProbeInput {
    private static readonly Func<TerminalInstance, Lock>? ReadLock = Reader<Lock>("_defaultInputQueueLock");
    private static readonly Func<TerminalInstance, Queue<TerminalEvent>>? ReadQueue =
        Reader<Queue<TerminalEvent>>("_defaultInputQueue");
    internal static bool CanPreserve => ReadLock is not null && ReadQueue is not null;

    private static Func<TerminalInstance, T>? Reader<T>(string name) {
        if (!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported) {
            return null;
        }
        var field = typeof(TerminalInstance).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field?.FieldType != typeof(T)) {
            return null;
        }
        var terminal = Expression.Parameter(typeof(TerminalInstance), "terminal");
        return Expression.Lambda<Func<TerminalInstance, T>>(Expression.Field(terminal, field), terminal).Compile();
    }

    internal static void Preserve(TerminalInstance terminal, IReadOnlyList<ConsoleKeyInfo> keys) {
        if (!CanPreserve) {
            throw new InvalidOperationException("The terminal input queue is unavailable.");
        }
        lock (ReadLock!(terminal)) {
            var queue = ReadQueue!(terminal);
            foreach (var key in keys) {
                var modifiers = TerminalModifiers.None;
                if (key.Modifiers.HasFlag(ConsoleModifiers.Shift)) { modifiers |= TerminalModifiers.Shift; }
                if (key.Modifiers.HasFlag(ConsoleModifiers.Control)) { modifiers |= TerminalModifiers.Ctrl; }
                if (key.Modifiers.HasFlag(ConsoleModifiers.Alt)) { modifiers |= TerminalModifiers.Alt; }
                if (key.KeyChar >= ' ' && (modifiers & (TerminalModifiers.Ctrl | TerminalModifiers.Alt)) == 0) {
                    queue.Enqueue(new TerminalTextEvent { Text = key.KeyChar.ToString() });
                    continue;
                }
                TerminalKey code = key.Key switch {
                    ConsoleKey.UpArrow => TerminalKey.Up,
                    ConsoleKey.DownArrow => TerminalKey.Down,
                    ConsoleKey.LeftArrow => TerminalKey.Left,
                    ConsoleKey.RightArrow => TerminalKey.Right,
                    ConsoleKey.Spacebar => TerminalKey.Space,
                    _ => Enum.TryParse<TerminalKey>(key.Key.ToString(), out var parsed) ? parsed : TerminalKey.Unknown
                };
                char? character = key.KeyChar == '\0' ? null : key.KeyChar;
                if (modifiers.HasFlag(TerminalModifiers.Ctrl) && key.KeyChar is >= '\x01' and <= '\x1a') {
                    character = (char)('a' + key.KeyChar - 1);
                }
                queue.Enqueue(new TerminalKeyEvent { Key = code, Char = character, Modifiers = modifiers });
            }
        }
    }
}
