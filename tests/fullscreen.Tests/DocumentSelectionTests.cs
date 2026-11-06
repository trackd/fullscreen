using fullscreen.Objects;
using fullscreen.Tables;
using System.Management.Automation;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Tests;

[TestClass]
[DoNotParallelize]
public sealed class DocumentSelectionTests {
    [TestMethod]
    public void JsonHighlightingDoesNotAllocateForOffscreenTokens() {
        var surface = new TableSurface(new TableModel(["Value"]), "JSON");
        long RenderAllocations(string json) {
            surface.SetDocument(json);
            for (int i = 0; i < 10; i++) {
                _ = VisualSnapshotRenderer.Render(surface, 80, 12);
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            _ = VisualSnapshotRenderer.Render(surface, 80, 12);
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
        string prefix = "[" + string.Concat(Enumerable.Repeat("\"value\",42,true,", 10));
        long shortLine = RenderAllocations(prefix + "null]");
        long longLine = RenderAllocations(prefix + string.Concat(Enumerable.Repeat("\"hidden\",123,false,", 20_000)) + "null]");
        Assert.IsLessThanOrEqualTo(shortLine + 1024, longLine,
            "Offscreen JSON must not allocate a line copy or per-token match objects.");
    }

    [TestMethod]
    public async Task JsonSupportsKeyboardSelectionCopyAndReadOnlyText() {
        var surface = new TableSurface(new TableModel(["Value"]), "JSON");
        const string json = "{\n  \"name\": \"hello\",\n  \"count\": 42\n}";
        surface.SetDocument(json);
        await using var ui = new DesignerUxTests.UiSession(surface, () => { });
        ui.Key(TerminalKey.Unknown, () => surface.SelectedDocumentText == json, TerminalModifiers.Ctrl, 'a');
        ui.Key(TerminalKey.Unknown, () => surface.Status == "Selection copied",
            TerminalModifiers.Ctrl, 'c');
        Assert.AreEqual(json, ui.App.Terminal.Clipboard.Text);
        Assert.IsFalse(surface.ExitRequested);
        ui.Key(TerminalKey.Delete, () => surface.SelectedDocumentText == json);
        ui.Text("x", () => surface.SelectedDocumentText == json);
        ui.Key(TerminalKey.Escape, () => surface.SelectedDocumentText.Length == 0);
        ui.Key(TerminalKey.Home, () => surface.SelectedDocumentText == "}", TerminalModifiers.Shift);
        ui.Key(TerminalKey.Unknown, () => surface.SelectedDocumentText == json, TerminalModifiers.Ctrl, 'a');
        surface.SetDocument("replacement");
        Assert.AreEqual("", surface.SelectedDocumentText);
    }

    [TestMethod]
    public async Task JsonDragSelectsExactTextAcrossLinesAndCopiesFromContextMenu() {
        var surface = new TableSurface(new TableModel(["Value"]), "JSON");
        surface.SetDocument("first line\nsecond line\nthird");
        await using var ui = new DesignerUxTests.UiSession(surface, () => { });
        ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, 4, 3, () => surface.HasFocus);
        ui.Mouse(TerminalMouseKind.Drag, TerminalMouseButton.Left, 8, 4,
            () => surface.SelectedDocumentText == "rst line\nsecond");
        ui.Mouse(TerminalMouseKind.Up, TerminalMouseButton.Left, 8, 4,
            () => surface.SelectedDocumentText == "rst line\nsecond");
        Assert.Contains("rst line", ui.App.CaptureSvg());
        ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Right, 8, 4,
            () => ui.Output.Contains("Copy selection", StringComparison.Ordinal));
        ui.Key(TerminalKey.Enter, () => surface.Status == "Selection copied");
        Assert.AreEqual("rst line\nsecond", ui.App.Terminal.Clipboard.Text);
        Assert.AreEqual("rst line\nsecond", surface.SelectedDocumentText);
        Assert.IsFalse(surface.ExitRequested);
    }

    [TestMethod]
    public async Task UnicodeSelectionAndHorizontalPanUseTerminalCellWidths() {
        var surface = new TableSurface(new TableModel(["Value"]), "JSON");
        surface.SetDocument("A\u754c\U0001f600B");
        await using var ui = new DesignerUxTests.UiSession(surface, () => { });
        ui.Key(TerminalKey.Right, () => surface.SelectedDocumentText == "A", TerminalModifiers.Shift);
        ui.Key(TerminalKey.Right, () => surface.SelectedDocumentText == "A\u754c", TerminalModifiers.Shift);
        ui.Key(TerminalKey.Right, () => surface.SelectedDocumentText == "A\u754c\U0001f600", TerminalModifiers.Shift);
        ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, 3, 3, () => surface.SelectedDocumentText == "");
        ui.Mouse(TerminalMouseKind.Up, TerminalMouseButton.Left, 7, 3,
            () => surface.SelectedDocumentText == "\u754c\U0001f600");
        Assert.DoesNotContain('\ufffd', surface.SelectedDocumentText);
        surface.SetDocument("01234567A\u754c\U0001f600B");
        ui.Key(TerminalKey.Right, () => surface.SelectedDocumentText == "");
        ui.Mouse(TerminalMouseKind.Down, TerminalMouseButton.Left, 3, 3, () => surface.SelectedDocumentText == "");
        ui.Mouse(TerminalMouseKind.Up, TerminalMouseButton.Left, 7, 3,
            () => surface.SelectedDocumentText == "\u754c\U0001f600");
    }

    [TestMethod]
    public void InspectAdaptsToCollectionsAndDictionaryEntriesWithoutOldViewNames() {
        CollectionAssert.AreEqual(new[] { "Formatted", "Inspect", "Json" }, Enum.GetNames<ObjectView>());
        using var items = new ObjectTableApp([PSObject.AsPSObject(new[] { new { Name = "item", Count = 2 } })],
            new() { View = ObjectView.Inspect });
        CollectionAssert.AreEqual(new[] { "Name", "Count" }, items.Surface.Model.Columns);
        items.InspectSelected(items.Surface.Model.Current!);
        Assert.AreEqual(1, items.NavigationDepth);
        Assert.IsTrue(items.Surface.Model.Rows.Any(row => row.Cells[2] == "Name" && row.Values[3] is "item"));

        using var dictionary = new ObjectTableApp([PSObject.AsPSObject(new System.Collections.Hashtable {
            ["Child"] = new { Answer = 42 }, ["Empty"] = null
        })], new() { View = ObjectView.Inspect });
        var entry = dictionary.Surface.Model.Rows.Single(row => row.Cells[0] == "Child");
        dictionary.InspectSelected(entry);
        Assert.AreEqual("$[0].Child", dictionary.NodePath);
        Assert.AreEqual(42, dictionary.Surface.Model.Rows.Single(row => row.Cells[2] == "Answer").Values[3]);
        Assert.IsTrue(dictionary.GoBack());
        Assert.IsTrue(dictionary.Surface.Model.Rows.Any(row => row.Cells[0] == "Empty" && row.Values[1] is null));
    }
}
