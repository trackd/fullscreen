namespace fullscreen.Charts;

internal readonly record struct ChartRange(double MinX, double MaxX, double MinY, double MaxY, double MinColor = 0, double MaxColor = 1) {
    internal static ChartRange From(ChartDataSeries[] data, GraphKind kind, double? minimum, double? maximum) {
        if (ChartTopology.Supports(kind)) {
            if (data.Length != 2 || data[0].Topology is not { } topology) {
                throw new ArgumentException("Relationship diagrams require separate node and edge collections.");
            }
            _ = topology.Layout(kind);
            return new(0, 1, 0, 1);
        }
        if (kind == GraphKind.Gantt) {
            return data.Length == 1 && data[0].Schedule is { } schedule ? schedule.Range
                : throw new ArgumentException("Gantt requires task intervals.");
        }
        if (kind == GraphKind.Flamegraph) {
            if (data.Length != 1 || data[0].FlameTree is not { } tree) {
                throw new ArgumentException("Flamegraph requires hierarchical frames.");
            }
            return new(0, tree.Total, 0, Math.Max(1, tree.Depth));
        }
        if (!Enum.IsDefined(kind)) {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
        if (ChartStackedArea.Supports(kind)) {
            return ChartStackedArea.Range(data, kind, minimum, maximum);
        }

        if (minimum.HasValue && !double.IsFinite(minimum.Value)
            || maximum.HasValue && !double.IsFinite(maximum.Value)) {
            throw new ArgumentException("Minimum and Maximum must be finite or null.");
        }
        if (ChartMarket.Supports(kind)) {
            ChartMarket.Validate(data, kind);
            if (kind == GraphKind.Marimekko) {
                if (minimum is not null || maximum is not null) {
                    throw new ArgumentException("Marimekko uses a fixed 0 to 100 percent composition range.");
                }
                return new(0, 1, 0, 100);
            }
        }

        double minX = double.PositiveInfinity, maxX = double.NegativeInfinity, minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
        bool distribution = kind is GraphKind.Histogram or GraphKind.BoxPlot;
        foreach (var series in data) {
            if (!series.Visible) {
                continue;
            }

            foreach (var point in series.Span) {
                if (ChartGrid.Supports(kind)) {
                    minX = Math.Min(minX, point.X);
                    maxX = Math.Max(maxX, point.X);
                }
                if (!point.Y.HasValue) {
                    continue;
                }

                double x = distribution ? point.Y.Value : point.X;
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, point.Y.Value);
                maxY = Math.Max(maxY, point.Y.Value);
                if (kind == GraphKind.ErrorBars) {
                    if (point.Lower is { } lower && double.IsFinite(lower)) {
                        minY = Math.Min(minY, lower);
                    }

                    if (point.Upper is { } upper && double.IsFinite(upper)) {
                        maxY = Math.Max(maxY, upper);
                    }
                }
            }
        }

        if (kind == GraphKind.StackedBars) {
            var totals = new Dictionary<double, (double Positive, double Negative)>();
            foreach (var series in data) {
                if (!series.Visible) {
                    continue;
                }

                foreach (var point in series.Span) {
                    if (point.Y is not { } value) {
                        continue;
                    }

                    totals.TryGetValue(point.X, out var total);
                    total = value >= 0 ? (total.Positive + value, total.Negative) : (total.Positive, total.Negative + value);
                    if (!double.IsFinite(total.Positive) || !double.IsFinite(total.Negative)) {
                        throw new ArgumentException("Stack totals must fit in finite double values.");
                    }

                    totals[point.X] = total;
                }
            }

            minY = Math.Min(0, totals.Values.Select(total => total.Negative).DefaultIfEmpty(0).Min());
            maxY = Math.Max(0, totals.Values.Select(total => total.Positive).DefaultIfEmpty(0).Max());
        }

        if (!double.IsFinite(minX)) {
            minX = 0;
            maxX = 1;
        }
        if (!double.IsFinite(minY)) {
            minY = 0;
            maxY = 1;
        }

        if (kind == GraphKind.Heatmap) {
            ChartGrid.Validate(data, kind);
            double colorLow = minimum ?? minY, colorHigh = maximum ?? maxY;
            if (colorLow > colorHigh) {
                throw new ArgumentException("Heatmap Minimum must be less than or equal to Maximum.");
            }
            return new(minX, maxX, 0, Math.Max(0, data.Length - 1), colorLow, colorHigh);
        }
        if (kind == GraphKind.Waterfall) {
            ChartGrid.Validate(data, kind);
            double total = 0, cumulativeLow = 0, cumulativeHigh = 0;
            foreach (var series in data) {
                foreach (var point in series.Span) {
                    if (point.Y is { } value) {
                        total = ChartGrid.Add(total, value);
                        cumulativeLow = Math.Min(cumulativeLow, total);
                        cumulativeHigh = Math.Max(cumulativeHigh, total);
                    }
                }
            }
            cumulativeLow = minimum ?? cumulativeLow;
            cumulativeHigh = maximum ?? cumulativeHigh;
            if (cumulativeLow > cumulativeHigh) {
                throw new ArgumentException("Minimum must be less than or equal to Maximum.");
            }
            return new(minX, maxX, cumulativeLow, cumulativeHigh);
        }

        if (kind is GraphKind.Gauge or GraphKind.RadialBars) {
            double radialMinimum = minimum ?? 0, radialMaximum = maximum ?? 100;
            if (radialMinimum < 0 || radialMinimum >= radialMaximum) {
                throw new ArgumentException("Gauge and radial bar bounds must be nonnegative with Minimum less than Maximum.");
            }
            return new(minX, maxX, radialMinimum, radialMaximum);
        }

        if (kind == GraphKind.HorizontalBars) {
            double valueMinimum = minimum ?? Math.Min(0, minY), valueMaximum = maximum ?? Math.Max(0, maxY);
            if (valueMinimum > valueMaximum) {
                throw new ArgumentException("Minimum must be less than or equal to Maximum.");
            }

            return new(
                valueMinimum,
                valueMaximum,
                0,
                Math.Max(0, data.Select(series => series.Count).DefaultIfEmpty(0).Max() - 1)
            );
        }

        if (kind is GraphKind.Area or GraphKind.Bars or GraphKind.Stem or GraphKind.StackedBars or GraphKind.ColumnLine or GraphKind.Radar) {
            minY = Math.Min(0, minY);
            maxY = Math.Max(0, maxY);
        }

        if (minimum is { } low && double.IsFinite(low)) {
            minY = low;
            if (distribution) {
                minX = low;
            }
        }

        if (maximum is { } high && double.IsFinite(high)) {
            maxY = high;
            if (distribution) {
                maxX = high;
            }
        }

        if (minY > maxY) {
            throw new ArgumentException("Minimum must be less than or equal to Maximum.");
        }

        return new(minX, maxX, minY, maxY);
    }
}
