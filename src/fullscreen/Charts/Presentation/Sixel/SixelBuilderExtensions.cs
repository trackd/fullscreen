using System.Runtime.CompilerServices;
using System.Text;
using XenoAtom.Terminal.UI;

namespace fullscreen.Charts;

internal static class SixelBuilderExtensions {
    internal static void AddSixelCanvas(this StringBuilder output, int width, int height) {
        output.Append("\x1bP0;1q\"1;1;");
        output.Append(width);
        output.Append(';');
        output.Append(height);
    }

    internal static void AddSixelColor(this StringBuilder output, int color, Color value) {
        var rgb = value.ToRgb();
        output.SelectSixelColor(color);
        output.Append(";2;");
        output.Append((rgb.R * 100 + 127) / 255);
        output.Append(';');
        output.Append((rgb.G * 100 + 127) / 255);
        output.Append(';');
        output.Append((rgb.B * 100 + 127) / 255);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void SelectSixelColor(this StringBuilder output, int color) {
        output.Append('#');
        output.Append(color);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void EndSixel(this StringBuilder output) => output.Append("\x1b\\");

    internal static void AddSixelRepeat(this StringBuilder output, int mask, int count) {
        if (count == 0) {
            return;
        }
        char value = (char)(63 + mask);
        int digits = count < 10 ? 1
            : count < 100 ? 2
            : count < 1000 ? 3
            : count < 10000 ? 4
            : count < 100000 ? 5
            : count < 1000000 ? 6
            : count < 10000000 ? 7
            : count < 100000000 ? 8
            : count < 1000000000 ? 9 : 10;
        if (count > digits + 2) {
            output.Append('!');
            output.Append(count);
            output.Append(value);
        }
        else {
            output.Append(value, count);
        }
    }
}
