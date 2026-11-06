namespace fullscreen.Charts;

internal static class ChartMath {
    internal static double Fraction(double value, double minimum, double maximum) {
        if (minimum == maximum) {
            return .5;
        }

        double scale = Math.Max(Math.Abs(minimum), Math.Abs(maximum));
        return Math.Clamp((value / scale - minimum / scale) / (maximum / scale - minimum / scale), 0, 1);
    }

    internal static int BarCenter(double value, double minimum, double maximum, int width, int count) {
        int span = Math.Max(1, width / Math.Max(1, count) - 1);
        return span / 2 + (int)Math.Round(Fraction(value, minimum, maximum) * (width - span));
    }
}
