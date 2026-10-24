using System.Collections;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using fullscreen.Charts;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace fullscreen.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ObjectChartCalculatedTests {
    [TestMethod]
    public void DirectScriptBlockGroupsAreCachedAndKeepOriginalObjects() {
        using var session = new ScriptSession();
        session.Runspace.SessionStateProxy.SetVariable("groupCalls", 0);
        var first = Item(2, "first");
        var second = Item(5, "second");
        var third = Item(8, "third");
        using var app = new ObjectChartApp([first, second, third], yProperties: ["Value"]);
        string group = app.Data.BindGroupBy(ScriptBlock.Create("$global:groupCalls++; if ($_.Value -lt 6) { 'small' } else { 'large' }"), TestContext.CancellationToken);
        app.SetAggregation(group, ChartAggregate.Sum);
        CollectionAssert.AreEqual(new double?[] { 7, 8 }, app.Surface.Series[0].Points.Select(point => point.Y).ToArray());
        app.SetAggregation(group, ChartAggregate.Count);
        CollectionAssert.AreEqual(new double?[] { 2, 1 }, app.Surface.Series[0].Points.Select(point => point.Y).ToArray());
        Assert.AreEqual(3, session.Runspace.SessionStateProxy.GetVariable("groupCalls"));
        Assert.AreSame(first, app.Data.Objects[0]);
        Assert.IsNull(first.Properties["Group"]);
        Assert.AreEqual(2, first.Properties["Value"].Value);
    }
    [TestMethod]
    public void DiscoveryDoesNotRunScriptProperties() {
        using var session = new ScriptSession();
        session.Runspace.SessionStateProxy.SetVariable("getterCalls", 0);
        var item = Item(2, "first");
        item.Properties.Add(new PSScriptProperty("Expensive", ScriptBlock.Create("$global:getterCalls++; throw 'not selected'")));
        var data = new ObjectChartData([item]);
        Assert.Contains("Expensive", data.Properties);
        Assert.AreEqual(0, session.Runspace.SessionStateProxy.GetVariable("getterCalls"));
        Assert.AreEqual(2d, data.Project(null, ["Value"], TestContext.CancellationToken)[0].Points[0].Y);
        Assert.AreEqual(0, session.Runspace.SessionStateProxy.GetVariable("getterCalls"));
    }
    private sealed class ScriptSession : IDisposable {
        private readonly Runspace? _previous = Runspace.DefaultRunspace;
        internal Runspace Runspace { get; } = RunspaceFactory.CreateRunspace(InitialSessionState.CreateDefault2());
        internal ScriptSession() {
            Runspace.Open();
            Runspace.DefaultRunspace = Runspace;
        }
        public void Dispose() {
            Runspace.DefaultRunspace = _previous;
            Runspace.Dispose();
        }
    }
    private static PSObject Item(int value, string name) {
        var item = new PSObject();
        item.Properties.Add(new PSNoteProperty("Value", value));
        item.Properties.Add(new PSNoteProperty("Name", name));
        return item;
    }
    [TestMethod]
    public void CalculatedParametersSupportBothAxesAliasesAndOriginalPropertiesWithTheSameHeader() {
        using var session = new ScriptSession();
        var first = Item(2, "first");
        using var app = new ObjectChartApp([first, Item(5, "second")],
            new Hashtable { ["Label"] = "Caption", ["Expression"] = ScriptBlock.Create("$PSItem.Name + '-x'") },
            ["Value", new Hashtable { ["n"] = "Value", ["e"] = ScriptBlock.Create("$_.Value * 3") }]);
        Assert.AreEqual("first-x", app.Surface.Series[0].Points[0].Label);
        Assert.AreEqual("Caption", app.Surface.XProperty);
        Assert.AreEqual(2d, app.Surface.Series[0].Points[0].Y);
        Assert.AreEqual(6d, app.Surface.Series[1].Points[0].Y);
        Assert.AreEqual("Value", app.Surface.Series[1].Property);
        Assert.AreEqual(2, first.Properties["Value"].Value);
        Assert.IsNull(first.Properties["Caption"]);
        Assert.AreSame(first, app.Data.Objects[0]);
        app.SetFilter(ObjectChartFilter.Top, 1, app.YProperties[1]);
        Assert.AreEqual(1, app.Surface.VisibleIndices.Single());
        Assert.Contains("by Value", app.Surface.FilterLabel);
    }
    [TestMethod]
    public void CalculationsAreCachedUntilEditedAndFailuresPreserveThePreviousField() {
        using var session = new ScriptSession();
        session.Runspace.SessionStateProxy.SetVariable("chartCalls", 0);
        using var app = new ObjectChartApp([Item(2, "first"), Item(5, "second")], yProperties: ["Value"]);
        string key = app.SetCalculated(false, "Scaled", "$global:chartCalls++; $_.Value * 2");
        Assert.AreEqual(2, session.Runspace.SessionStateProxy.GetVariable("chartCalls"));
        app.Configure(app.XProperty, app.YProperties, GraphKind.Bars);
        app.SetFilter(ObjectChartFilter.Top, 1, key);
        Assert.AreEqual(2, session.Runspace.SessionStateProxy.GetVariable("chartCalls"));
        Assert.AreEqual(10d, app.Surface.Series[1].Points[0].Y);
        app.SetCalculated(false, "Renamed", "{ $global:chartCalls++; $_.Value * 4 }", key);
        Assert.AreEqual(4, session.Runspace.SessionStateProxy.GetVariable("chartCalls"));
        Assert.AreEqual("Renamed", app.Surface.Series[1].Property);
        Assert.AreEqual(20d, app.Surface.Series[1].Points[0].Y);
        Assert.ThrowsExactly<ParseException>(() => app.SetCalculated(false, "Broken", "(", key));
        Assert.ThrowsExactly<ArgumentException>(() => app.SetCalculated(false, "Broken", "throw 'bad expression'", key));
        Assert.ThrowsExactly<ArgumentException>(() => app.SetCalculated(false, "Multiple", "1; 2"));
        Assert.AreEqual("Renamed", app.Data.Label(key));
        Assert.HasCount(1, app.Data.Calculated);
        Assert.AreEqual(20d, app.Surface.Series[1].Points[0].Y);
        app.RemoveCalculated(key);
        Assert.IsEmpty(app.Data.Calculated);
        Assert.AreEqual(ObjectChartFilter.All, app.FilterMode);
        Assert.HasCount(2, app.Surface.VisibleIndices);
        Assert.AreEqual("Value", app.Surface.Series.Single().Property);
    }
    [TestMethod]
    public void PartialFailuresBecomeGapsAndStringExpressionsAliasLiteralProperties() {
        using var session = new ScriptSession();
        using var app = new ObjectChartApp([Item(2, "first"), Item(5, "second")],
            new Hashtable { ["l"] = "Alias", ["e"] = "Name" },
            [new Hashtable { ["Name"] = "Partial", ["Expression"] = ScriptBlock.Create("if ($_.Value -eq 5) { throw 'unavailable' }; $_.Value") }]);
        Assert.AreEqual("first", app.Surface.Series[0].Points[0].Label);
        Assert.AreEqual(2d, app.Surface.Series[0].Points[0].Y);
        Assert.IsNull(app.Surface.Series[0].Points[1].Y);
        Assert.AreEqual("Partial (custom; 1 error)", app.Data.MenuLabel(app.YProperties.Single()));
        Assert.ThrowsExactly<ArgumentException>(() => ObjectChartProperty.Parse(new Hashtable { ["Name"] = "Bad", ["Expression"] = 42 }));
        Assert.ThrowsExactly<ArgumentException>(() => ObjectChartProperty.Parse(new Hashtable { ["Expression"] = ScriptBlock.Create("1") }));
        Assert.ThrowsExactly<ArgumentException>(() => ObjectChartProperty.Parse(new Hashtable { ["Name"] = "Bad", ["Expression"] = ScriptBlock.Create("1"), ["Unknown"] = true }));
    }
    [TestMethod]
    public async Task CustomPopupValidatesAddsEditsAndRemovesComputedFields() {
        using var session = new ScriptSession();
        using var app = new ObjectChartApp([Item(2, "first"), Item(5, "second")], yProperties: ["Value"]);
        await using var ui = new DesignerUxTests.UiSession(app.Root, () => app.Tick());
        ui.Text("y", () => ui.Output.Contains("Custom…", StringComparison.Ordinal));
        ui.Key(TerminalKey.Down, () => true);
        ui.Key(TerminalKey.Down, () => true);
        ui.Key(TerminalKey.Enter, () => ui.App.Root.EnumerateVisualsDepthFirst().OfType<Dialog>().Any());
        Dialog CurrentDialog() =>
            ui.App.Root.EnumerateVisualsDepthFirst().OfType<Dialog>().Single();
        Button Button(string name) =>
            CurrentDialog().EnumerateVisualsDepthFirst().OfType<Button>().Single(button => button.Content is TextBlock label && label.Text == name);
        var header = CurrentDialog().EnumerateVisualsDepthFirst().OfType<TextBox>().Single();
        var code = CurrentDialog().EnumerateVisualsDepthFirst().OfType<TextArea>().Single();
        ui.App.Focus(code);
        ui.Key(TerminalKey.Tab, () => ui.App.FocusedElement is Button);
        header.Text = "Doubled";
        code.Text = "(";
        ui.Activate(Button("Apply"), () => true);
        Assert.IsEmpty(app.Data.Calculated);
        Assert.HasCount(1, ui.App.Root.EnumerateVisualsDepthFirst().OfType<Dialog>());
        code.Text = "$_.Value * 2";
        ui.Activate(Button("Apply"), () => app.Data.Calculated.Count == 1);
        string key = app.Data.Calculated.Single().Key;
        Assert.AreEqual(10d, app.Surface.Series[1].Points[1].Y);
        ui.Text("x", () => ui.Output.Contains("Doubled (custom)", StringComparison.Ordinal));
        for (int index = 0; index < 3; index++) {
            ui.Key(TerminalKey.Down, () => true);
        }
        ui.Key(TerminalKey.Enter, () => app.XProperty == key);
        ui.Text("y", () => ui.Output.Contains("Edit custom…", StringComparison.Ordinal));
        for (int index = 0; index < 4; index++) {
            ui.Key(TerminalKey.Down, () => true);
        }
        ui.Key(TerminalKey.Right, () => true);
        ui.Key(TerminalKey.Enter, () => ui.App.Root.EnumerateVisualsDepthFirst().OfType<Dialog>().Any());
        header = CurrentDialog().EnumerateVisualsDepthFirst().OfType<TextBox>().Single();
        code = CurrentDialog().EnumerateVisualsDepthFirst().OfType<TextArea>().Single();
        Assert.AreEqual("Doubled", header.Text);
        Assert.Contains("$_.Value * 2", code.Text!);
        header.Text = "Squared";
        code.Text = "{ $_.Value * $_.Value }";
        ui.Activate(Button("Apply"), () => app.Surface.XProperty == "Squared");
        Assert.AreEqual(key, app.XProperty);
        Assert.AreEqual(25d, app.Surface.Series[1].Points[1].Y);
        app.RemoveCalculated(key);
        ui.Tick();
        Assert.IsNull(app.XProperty);
        Assert.AreEqual("Value", app.Surface.Series.Single().Property);
        Assert.AreEqual(5, app.Data.Objects[1].Properties["Value"].Value);
    }

    public TestContext TestContext { get; set; } = null!;
}
