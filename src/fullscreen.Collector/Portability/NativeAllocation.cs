using System.Runtime.InteropServices;

namespace fullscreen.Monitoring;

internal static unsafe class NativeAllocation {
    internal static void* AllocZeroed(uint size) {
        byte* memory = (byte*)Marshal.AllocHGlobal(checked((int)size));
        for (uint i = 0; i < size; i++) {
            memory[i] = 0;
        }

        return memory;
    }

    internal static void Free(void* memory) =>
        Marshal.FreeHGlobal((nint)memory);
}
