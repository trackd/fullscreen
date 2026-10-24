using System.Collections;
using System.Management.Automation;
using XenoAtom.Terminal.UI.Controls;

namespace fullscreen.Charts;

internal sealed partial class ObjectChartApp {
    private IEnumerable<MenuItem> CustomEntries(bool xAxis) {
        yield return new MenuItem("Custom…", () => EditCustom(xAxis));
        if (Data.Calculated.Count == 0) {
            yield break;
        }

        var edit = new MenuItem("Edit custom…");
        foreach (var column in Data.Calculated) {
            edit.Items.Add(new MenuItem(column.Definition.Name, () => EditCustom(xAxis, column.Key)));
        }

        yield return edit;
    }

    internal string SetCalculated(bool xAxis, string name, string body, string? key = null) {
        var definition = ObjectChartProperty.Parse(new Hashtable {
            ["Name"] = name,
            ["Expression"] = ObjectChartProperty.ParseBody(body)
        });
        var previous = key is null ? null : Data.GetCalculated(key) ?? throw new ArgumentException("The custom field no longer exists.");
        var column = Data.Calculate(definition, key, _token);
        Data.Store(column);
        try {
            if (key is not null) {
                Replot();
            }
            else if (xAxis) {
                Configure(column.Key, YProperties, Surface.GraphType);
            }
            else {
                Configure(XProperty, [.. YProperties, column.Key], Surface.GraphType);
            }

            return column.Key;
        }
        catch {
            if (previous is null) {
                Data.RemoveCalculated(column.Key);
            }
            else {
                Data.Store(previous);
            }

            throw;
        }
    }

    internal void RemoveCalculated(string key) {
        var previous = Data.GetCalculated(key) ?? throw new ArgumentException("The custom field no longer exists.");
        Data.RemoveCalculated(key);
        try {
            Apply(
                XProperty == key ? null : XProperty,
                [.. YProperties.Where(name => name != key)],
                Surface.GraphType,
                RankProperty == key ? ObjectChartFilter.All : FilterMode,
                Count,
                RankProperty == key ? null : RankProperty
            );
        }
        catch {
            Data.Store(previous);
            throw;
        }
    }

    private void EditCustom(bool xAxis, string? key = null) {
        var previous = key is null ? null : Data.GetCalculated(key);
        var header = new TextBox(previous?.Definition.Name ?? "");
        var expression = new TextArea(previous?.Definition.Body ?? "") {
            MinHeight = 6,
            MaxHeight = 6,
            AcceptTab = false
        };
        var error = new TextBlock("");
        Dialog? dialog = null;
        var buttons = new HStack(new Button("Cancel").Click(() => dialog!.Close()), new Button("Apply").Click(() => {
            try {
                SetCalculated(xAxis, header.Text ?? "", expression.Text ?? "", key);
                dialog!.Close();
            }
            catch (PipelineStoppedException) {
                throw;
            }
            catch (OperationCanceledException) when (_token.IsCancellationRequested) {
                throw;
            }
            catch (Exception failure) {
                error.Text = failure is ParseException parse && parse.Errors.Length > 0 ? parse.Errors[0].Message : failure.Message;
            }
        }));
        if (key is not null) {
            buttons.Children.Add(new Button("Remove").Click(() => {
                RemoveCalculated(key);
                dialog!.Close();
            }));
        }

        dialog = new Dialog {
            Title = new TextBlock(previous is null ? $"Custom {(xAxis ? "X" : "Y")} property" : "Edit custom property"),
            IsModal = true,
            Width = Math.Max(25, Math.Min(90, Surface.Bounds.Width - 4)),
            Content = new VStack(
                new TextBlock("Header"),
                header,
                new TextBlock("PowerShell expression · $_ is the input object"),
                expression,
                new TextBlock("Example: $_.WorkingSet64 / 1MB · evaluated when applied"),
                error,
                buttons
            )
        };
        dialog.Show();
    }
}
