using System.Globalization;
using System.Collections;
using System.Management.Automation;
using fullscreen.Tables;

namespace fullscreen.Custom;
// Runs only when data changes, on the PowerShell/UI thread. Cells are cached by TableModel.
internal static class TuiTableData {
    internal static (List<object> Originals, List<PSObject> Objects) Snapshot(
        object input,
        CancellationToken token = default
    ) {
        if (input is PSObject wrapped && wrapped.BaseObject is not PSCustomObject) {
            input = wrapped.BaseObject;
        }

        IEnumerable sequence = input is IEnumerable items and not string and not IDictionary ? items : new object[]
        {
            input
        };
        var originals = new List<object>();
        var objects = new List<PSObject>();
        int examined = 0;
        foreach (object? item in sequence) {
            token.ThrowIfCancellationRequested();
            if (++examined > 10000) {
                throw new ArgumentException("Embedded tables examine at most 10,000 items per snapshot, including nulls.");
            }

            if (item is null) {
                continue;
            }

            object value = item is PSObject wrapper && wrapper.BaseObject is not PSCustomObject ? wrapper.BaseObject : item;
            originals.Add(item);
            if (value is IDictionary dictionary) {
                var record = new PSObject();
                foreach (DictionaryEntry pair in dictionary) {
                    record.Properties.Add(new PSNoteProperty(TableModel.Text(pair.Key), pair.Value));
                }

                objects.Add(record);
            }
            else {
                objects.Add(PSObject.AsPSObject(value));
            }
        }

        return (originals, objects);
    }

    public static TableModel Create(object input, TuiNode node, CancellationToken token = default) {
        var (originals, objects) = Snapshot(input, token);
        if (node.Type == "FLogView") {
            var logs = new TableModel(["Timestamp", "Level", "Source", "Message"]);
            for (int i = 0; i < originals.Count; i++) {
                token.ThrowIfCancellationRequested();
                string line = Convert.ToString(originals[i], CultureInfo.InvariantCulture) ?? "";
                var row = Logs.LogParser.Parse(i, line, "custom", DateTimeOffset.UtcNow).Row();
                logs.Rows.Add(row with { Original = originals[i] });
            }

            logs.Rebuild();
            return logs;
        }

        var result = Objects.ObjectSnapshot.CreateCancellable(objects, node.Columns.Length > 0 ? node.Columns : null, token);
        for (int i = 0; i < result.Rows.Count; i++) {
            result.Rows[i] = result.Rows[i] with {
                Original = originals[i]
            };
        }

        result.Rebuild();
        return result;
    }
}
