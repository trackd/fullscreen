using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace fullscreen.Hosting;
/// <summary>Preserves the mode from before the terminal backend enables VT input.</summary>
 // XenoAtom.Terminal 2.2.0 enables VT during Initialize, then saves that modified
// mode when StartInput runs. Remove this scope when upstream preserves the mode
// from before Initialize. Do not extend it into a second console-input owner.
internal sealed partial class WindowsConsoleInputMode : IDisposable {
    private readonly nint _handle;
    private bool _restore;
    internal uint? OriginalMode { get; }

    private WindowsConsoleInputMode(nint handle, uint? mode) {
        _handle = handle;
        OriginalMode = mode;
        _restore = mode.HasValue;
    }

    internal static WindowsConsoleInputMode Capture() {
        if (!OperatingSystem.IsWindows()) {
            return new(0, null);
        }

        // GetStdHandle returns a borrowed console handle; this scope never closes it.
        nint handle = GetStdHandle(-10);
        return handle != 0 && handle != -1 && GetConsoleMode(handle, out uint mode) != 0 ? new(handle, mode) : new(0, null);
    }

    internal static WindowsConsoleInputMode EnterQuery() {
        var scope = Capture();
        if (OperatingSystem.IsWindows()) {
            if (scope.OriginalMode is not { } original
                || SetConsoleMode(scope._handle, (original | 0x0200u) & ~0x0007u) == 0) {
                scope.Dispose();
                throw new IOException("Unable to acquire raw VT input for terminal capability queries.");
            }
        }
        return scope;
    }

    public void Dispose() {
        if (!_restore) {
            return;
        }

        _restore = false;
        if (OperatingSystem.IsWindows()) {
            _ = SetConsoleMode(_handle, OriginalMode!.Value);
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    private static partial nint GetStdHandle(int kind);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    private static partial int GetConsoleMode(nint handle, out uint mode);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    private static partial int SetConsoleMode(nint handle, uint mode);
}
