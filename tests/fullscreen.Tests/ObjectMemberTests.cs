using System.Management.Automation;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using fullscreen.Objects;
using fullscreen.Themes;
using XenoAtom.Terminal.UI.Rendering;

namespace fullscreen.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ObjectMemberTests {
    [TestMethod]
    [DataRow("System.String", "string")]
    [DataRow("System.IO.DirectoryInfo[]", "DirectoryInfo[]")]
    [DataRow("System.Int32[,]", "int[,]")]
    [DataRow("System.Collections.Generic.IEnumerable[System.IO.DirectoryInfo]", "IEnumerable<DirectoryInfo>")]
    [DataRow("System.Collections.Generic.Dictionary`2[System.String,System.Collections.Generic.List`1[System.Int32[]]]",
        "Dictionary<string,List<int[]>>")]
    public void FallbackTypeNamesUseShortReadableNames(string source, string expected) {
        Assert.AreEqual(expected, ObjectMembers.ShortType(source));
    }

    [TestMethod]
    public void MemberDisplayShortensMetadataButPreservesOrdinaryPropertyText() {
        var source = new PSObject();
        source.Properties.Add(new PSNoteProperty("Text", "System.IO.DirectoryInfo"));
        source.Properties.Add(new PSNoteProperty("Type", typeof(Dictionary<string, int[]>)));
        var model = ObjectMembers.Create(source);
        Assert.AreEqual("string", model.Rows.Single(row => row.Cells[2] == "Text").Cells[1]);
        Assert.AreEqual("System.IO.DirectoryInfo", model.Rows.Single(row => row.Cells[2] == "Text").Cells[3]);
        Assert.AreEqual("Dictionary<string, int[]>", model.Rows.Single(row => row.Cells[2] == "Type").Cells[3]);
        var directory = ObjectMembers.Create(new DirectoryInfo("."));
        var overload = directory.Rows.First(row => row.Cells[2] == "EnumerateDirectories");
        Assert.AreEqual("IEnumerable<DirectoryInfo>", overload.Cells[1]);
        Assert.StartsWith("public IEnumerable<DirectoryInfo> EnumerateDirectories(", overload.Cells[3]);
    }

    public sealed class Fixture {
        public int Calls { get; private set; }
        public int Count { get; } = 42;
        public string Failure => throw new InvalidOperationException($"fixture getter failed: {Calls}");
        public int Compute(int value) { Calls++; return value; }
        public string Compute(string value) { Calls++; return value; }
    }

    public class BaseFixture {
        public virtual int Evaluate(int value) => value;
    }

    public sealed class ReflectionFixture : BaseFixture {
        private readonly int _count = 7;
        public override int Evaluate(int value) => value + _count;
        public int? Optional { get; set; }
        public int this[int index] { get => index + _count; set => throw new InvalidOperationException("Never set"); }
        public void Arguments(ref int reference, out string output, in double input, params int[] values) {
            reference += values.Length + _count;
            output = input.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        public int Defaults(string value = "System.IO.FileInfo", bool enabled = false, char quote = '\'',
            string escaped = "a\\b\n\"c\"", FileMode mode = FileMode.Open) =>
            value.Length + escaped.Length + quote + (enabled ? 1 : 0) + (int)mode + _count;
        public T Generic<T>(T value, List<T[]> items) => items.Count == _count ? items[0][0] : value;
        public void ComOptional([Optional] object value) { if (_count > 0) { GC.KeepAlive(value); } }
        public ref readonly int Reference() => ref _count;
        public int Collection(params ReadOnlySpan<int> values) => values.Length + _count;
    }

    [TestMethod]
    public void ReflectionSignaturesPreserveModifiersParametersDefaultsAndGenerics() {
        var model = ObjectMembers.Create(new ReflectionFixture());
        string Signature(string name) => model.Rows.First(row => row.Cells[2] == name).Cells[3];
        Assert.AreEqual("public override int Evaluate(int value);", Signature("Evaluate"));
        Assert.AreEqual("public void Arguments(ref int reference, out string output, in double input, params int[] values);",
            Signature("Arguments"));
        Assert.AreEqual("public int Defaults(string value = \"System.IO.FileInfo\", bool enabled = false, char quote = '\\'', "
            + "string escaped = \"a\\\\b\\n\\\"c\\\"\", FileMode mode = FileMode.Open);", Signature("Defaults"));
        Assert.AreEqual("public T Generic<T>(T value, List<T[]> items);", Signature("Generic"));
        Assert.AreEqual("public void ComOptional([Optional] object value);", Signature("ComOptional"));
        Assert.AreEqual("public ref readonly int Reference();", Signature("Reference"));
        Assert.AreEqual("public int Collection(params ReadOnlySpan<int> values);", Signature("Collection"));
        Assert.AreEqual("int?", model.Rows.Single(row => row.Cells[2] == "Optional").Cells[1]);
        var indexer = model.Rows.Single(row => row.Cells[2] == "Item");
        Assert.AreEqual("public int this[int index] { get; set; }", indexer.Cells[3]);
        Assert.IsInstanceOfType<ObjectMembers.Signature>(indexer.Original);
    }

    [TestMethod]
    public void ReflectionPreservesInterfaceOverloadsAndNestedGenericTypes() {
        var model = ObjectMembers.Create(new Dictionary<string, int[]>());
        var methods = model.Rows.Where(row => row.Cells[2] == "GetEnumerator").ToArray();
        Assert.HasCount(4, methods);
        Assert.IsTrue(methods.Any(row => row.Cells[1] == "Dictionary<string, int[]>.Enumerator"));
        Assert.IsTrue(methods.Any(row => row.Cells[3] ==
            "IEnumerator<KeyValuePair<string, int[]>> IEnumerable<KeyValuePair<string, int[]>>.GetEnumerator();"));
        Assert.IsTrue(methods.Any(row => row.Cells[3] == "IEnumerator IEnumerable.GetEnumerator();"));
        Assert.HasCount(2, model.Rows.Where(row => row.Cells[2] == "Item").ToArray());
        var source = PSObject.AsPSObject(new Dictionary<string, int[]>());
        foreach (var member in source.Members.OfType<PSMethodInfo>()) {
            if (member.Name.StartsWith("get_", StringComparison.Ordinal)
                || member.Name.StartsWith("set_", StringComparison.Ordinal)) { continue; }
            Assert.AreEqual(member.OverloadDefinitions.Count, model.Rows.Count(row => row.Cells[2] == member.Name),
                member.Name);
        }
    }

    private static int s_scriptCalls;
    public static int ScriptCalls() { return ++s_scriptCalls; }
    public static string CodeEcho(PSObject target, int count) => target.ToString() + count;
    public static string? CodeText(PSObject target) => target.Properties["Text"]?.Value as string;

    [TestMethod]
    public void ScriptAndCodeMembersUseExistingMetadataWithoutRunningDefaultsOrMethods() {
        s_scriptCalls = 0;
        var source = new PSObject();
        source.Properties.Add(new PSNoteProperty("Text", null));
        source.Properties.Add(new PSCodeProperty("CodeText", typeof(ObjectMemberTests).GetMethod(nameof(CodeText))!));
        source.Members.Add(new PSCodeMethod("CodeEcho", typeof(ObjectMemberTests).GetMethod(nameof(CodeEcho))!));
        source.Members.Add(new PSScriptMethod("Echo", ScriptBlock.Create(
            "param([int]$count = 3, [string]$path = 'System.IO.FileInfo', "
            + "$effect = [fullscreen.Tests.ObjectMemberTests]::ScriptCalls()) $path")));
        var model = ObjectMembers.Create(source);
        Assert.AreEqual("object Echo(int count = 3, string path = \"System.IO.FileInfo\", "
            + "object effect = [fullscreen.Tests.ObjectMemberTests]::ScriptCalls());",
            model.Rows.Single(row => row.Cells[2] == "Echo").Cells[3]);
        Assert.AreEqual("public string CodeEcho(int count);",
            model.Rows.Single(row => row.Cells[2] == "CodeEcho").Cells[3]);
        Assert.AreEqual("string", model.Rows.Single(row => row.Cells[2] == "CodeText").Cells[1]);
        Assert.AreEqual(0, s_scriptCalls);
        Assert.IsFalse(model.Rows.Any(row => row.Cells[3].Contains("PSObject target", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void PropertyTypesUseDeclarationsAndReadGettersOnlyOnce() {
        var fixture = new GetterFixture();
        var source = PSObject.AsPSObject(fixture);
        source.Properties.Add(new PSAliasProperty("Alias", "Text"));
        var model = ObjectMembers.Create(source);
        Assert.AreEqual(2, fixture.Reads, "One read each for the real property and its alias.");
        Assert.AreEqual("string", model.Rows.Single(row => row.Cells[2] == "Text").Cells[1]);
        Assert.AreEqual("string", model.Rows.Single(row => row.Cells[2] == "Alias").Cells[1]);
        Assert.AreEqual("int[][,]", model.Rows.Single(row => row.Cells[2] == "Arrays").Cells[1]);
        Assert.AreEqual("Type", model.Rows.Single(row => row.Cells[2] == "GetType").Cells[1]);
    }

    public sealed class GetterFixture {
        public int Reads { get; private set; }
        public string? Text { get { Reads++; return null; } }
        public int[][,]? Arrays { get; set; }
    }

    [TestMethod]
    public void SemanticSignatureColorsCanBeReappliedWithoutParsingText() {
        var model = ObjectMembers.Create(new ReflectionFixture());
        var row = model.Rows.Single(row => row.Cells[2] == "Defaults");
        var signature = Assert.IsInstanceOfType<ObjectMembers.Signature>(row.Original);
        Assert.IsTrue(signature.Tokens.Any(token => token.Kind == ObjectMembers.TokenKind.Keyword
            && row.Cells[3].Substring(token.Start, token.Length) == "public"));
        Assert.IsTrue(signature.Tokens.Any(token => token.Kind == ObjectMembers.TokenKind.Literal
            && row.Cells[3].Substring(token.Start, token.Length) == "\"System.IO.FileInfo\""));
        var palette = ThemeCatalog.Load("Monokai");
        ObjectMembers.Style(model, palette);
        var tokens = signature.Tokens;
        ObjectMembers.Style(model, palette);
        Assert.AreSame(tokens, signature.Tokens);
        Assert.HasCount(tokens.Length + 1, model.CellStyles[(row.Id, 3)]);
        for (int index = 0; index < tokens.Length; index++) {
            var token = tokens[index];
            var expected = token.Kind switch {
                ObjectMembers.TokenKind.Keyword => palette.Keyword,
                ObjectMembers.TokenKind.Type => palette.Type,
                ObjectMembers.TokenKind.Name => palette.Function,
                ObjectMembers.TokenKind.Literal => palette.StringLiteral,
                ObjectMembers.TokenKind.Number => palette.Number,
                ObjectMembers.TokenKind.Parameter => palette.Parameter,
                ObjectMembers.TokenKind.Operator => palette.Operator,
                _ => palette.DefaultToken
            };
            Assert.IsTrue(model.CellStyles[(row.Id, 3)][index + 1].Style.TryGetForeground(out var actual));
            Assert.AreEqual(expected, actual);
        }
    }

    private sealed class AdapterMethod : PSMethodInfo {
        internal AdapterMethod() { SetMemberName("Remote"); }
        public override PSMemberTypes MemberType => PSMemberTypes.ScriptMethod;
        public override string TypeNameOfValue => "System.Management.Automation.PSMethod";
        public override Collection<string> OverloadDefinitions =>
            ["Acme.Widget Remote(string path = \"System.IO.FileInfo\")"];
        public override PSMemberInfo Copy() => new AdapterMethod();
        public override object Invoke(params object[] arguments) => throw new InvalidOperationException("Never invoke");
    }

    [TestMethod]
    public void UnsupportedAdapterSignaturesRemainVerbatim() {
        var source = new PSObject();
        source.Members.Add(new AdapterMethod());
        var model = ObjectMembers.Create(source);
        var row = model.Rows.Single(entry => entry.Cells[2] == "Remote");
        Assert.AreEqual("Acme.Widget Remote(string path = \"System.IO.FileInfo\")", row.Cells[3]);
        Assert.AreEqual("PSMethod", row.Cells[1]);
        ObjectMembers.Style(model, ThemePalette.Default);
        Assert.HasCount(1, model.CellStyles[(row.Id, 3)]);
    }

    private sealed class AdapterProperty : PSPropertyInfo {
        private readonly string _typeName;
        internal AdapterProperty(string typeName) { _typeName = typeName; SetMemberName("TypedNull"); }
        public override PSMemberTypes MemberType => PSMemberTypes.NoteProperty;
        public override string TypeNameOfValue => _typeName;
        public override bool IsGettable => true;
        public override bool IsSettable => false;
        public override object? Value { get => null; set => throw new InvalidOperationException("Read-only"); }
        public override PSMemberInfo Copy() => new AdapterProperty(_typeName);
    }

    [TestMethod]
    [DataRow("SInt32", "int")]
    [DataRow("Real32", "float")]
    [DataRow("System.Type", "Type")]
    [DataRow("System.Collections.Generic.IEnumerable[System.IO.DirectoryInfo]", "IEnumerable<DirectoryInfo>")]
    [DataRow("Acme.Widget", "Widget")]
    public void NamedFallbackTypesResolvePowerShellAndCimAliases(string name, string expected) {
        var source = new PSObject();
        source.Properties.Add(new AdapterProperty(name));
        var row = ObjectMembers.Create(source).Rows.Single(entry => entry.Cells[2] == "TypedNull");
        Assert.AreEqual(expected, row.Cells[1]);
        Assert.IsNull(row.Values[3]);
    }

    [TestMethod]
    public void ReflectionOverloadCoverageMatchesRepresentativePowerShellObjects() {
        object[] samples = [new FileInfo("LICENSE"), new DirectoryInfo("."), "hello", new List<int>()];
        foreach (object value in samples) {
            var source = PSObject.AsPSObject(value);
            var model = ObjectMembers.Create(source);
            foreach (var member in source.Members.OfType<PSMethodInfo>()) {
                if (member.Name.StartsWith("get_", StringComparison.Ordinal)
                    || member.Name.StartsWith("set_", StringComparison.Ordinal)) { continue; }
                Assert.AreEqual(member.OverloadDefinitions.Count, model.Rows.Count(row => row.Cells[2] == member.Name),
                    $"{value.GetType().Name}.{member.Name}");
            }
        }
    }

    [TestMethod]
    public void MemberMetadataContainsOverloadsTypesAndGetterErrorsWithoutInvokingMethods() {
        var fixture = new Fixture();
        var model = ObjectMembers.Create(fixture);
        var methods = model.Rows.Where(row => row.Cells[2] == "Compute").ToArray();
        Assert.HasCount(2, methods);
        Assert.IsTrue(methods.All(row => row.Cells[0] == "Method" && row.Cells[1].Length > 0));
        Assert.IsTrue(methods.Any(row => row.Cells[3].Contains("int value", StringComparison.Ordinal)));
        Assert.IsTrue(methods.Any(row => row.Cells[3].Contains("string value", StringComparison.Ordinal)));
        Assert.AreEqual(0, fixture.Calls);
        Assert.IsFalse(model.Rows.Any(row => row.Cells[2].StartsWith("get_", StringComparison.Ordinal)));
        Assert.AreEqual(42, model.Rows.Single(row => row.Cells[2] == "Count").Values[3]);
        Assert.Contains("property error", model.Rows.Single(row => row.Cells[2] == "Failure").Cells[3]);
        ObjectMembers.Style(model, ThemePalette.Default);
        Assert.IsGreaterThan(1, model.CellStyles[(methods[0].Id, 3)].Length);
    }

    [TestMethod]
    public void MembersPreservePowerShellExtensionsNullAndFalseValues() {
        var source = new PSObject();
        source.Properties.Add(new PSNoteProperty("Empty", null));
        source.Properties.Add(new PSNoteProperty("Enabled", false));
        source.Members.Add(new PSScriptMethod("Echo", ScriptBlock.Create("param([int]$value) $value")));
        var model = ObjectMembers.Create(source);
        Assert.IsNull(model.Rows.Single(row => row.Cells[2] == "Empty").Values[3]);
        Assert.IsFalse((bool)model.Rows.Single(row => row.Cells[2] == "Enabled").Values[3]!);
        Assert.IsTrue(model.Rows.Any(row => row.Cells[2] == "Echo" && row.Cells[0] == "ScriptMethod"));
        Assert.AreEqual(0, source.Properties.Count(property => property.Name == "ParentType"),
            "Inspection must not add metadata to source objects.");
    }

    [TestMethod]
    public async Task MembersTabUsesSelectedObjectAndReturnsToItsNavigationContext() {
        var first = new PSObject();
        first.Properties.Add(new PSNoteProperty("Label", "first"));
        var second = new PSObject();
        second.Properties.Add(new PSNoteProperty("Child", new Fixture()));
        using var app = new ObjectTableApp([first, second], new() { PassThru = true, Property = ["Label", "Child"] });
        app.Surface.Model.Select(1, 20);
        await using var ui = new DesignerUxTests.UiSession(app.Root, () => app.Tick());
        ui.Text("2", () => app.View == ObjectView.Inspect);
        Assert.IsFalse(app.Surface.EnterAccepts);
        var child = app.Surface.Model.Rows.Single(row => row.Cells[2] == "Child");
        app.InspectSelected(child);
        Assert.AreEqual(1, app.NavigationDepth);
        app.SwitchView(ObjectView.Inspect);
        Assert.IsTrue(app.Surface.Model.Rows.Any(row => row.Cells[2] == "Compute"));
        Assert.IsTrue(app.GoBack());
        Assert.AreEqual(ObjectView.Inspect, app.View);
        Assert.IsFalse(app.Surface.EnterAccepts);
        Assert.IsTrue(app.Surface.Model.Rows.Any(row => row.Cells[2] == "Child"));
        app.SwitchView(ObjectView.Formatted);
        Assert.AreEqual(1L, app.Surface.Model.Current!.Id);
        ui.Key(XenoAtom.Terminal.TerminalKey.Enter, () => app.Surface.Accepted);
        Assert.AreSame(second, app.Chosen.Single());
    }

    [TestMethod]
    public void MembersCanOpenDirectlyWithoutLoadingOtherViewsAndHandleEmptyInput() {
        using var app = new ObjectTableApp([PSObject.AsPSObject("hello")], new() { View = ObjectView.Inspect });
        Assert.IsFalse(app.IsLoaded(ObjectView.Formatted));
        Assert.IsFalse(app.IsLoaded(ObjectView.Json));
        Assert.IsTrue(app.IsLoaded(ObjectView.Inspect));
        VisualSnapshotRenderer.Render(app.Surface, 120, 25);
        Assert.IsTrue(app.Surface.Model.Rows.Any(row => row.Cells[2] == "Substring"));
        using var empty = new ObjectTableApp([], new() { View = ObjectView.Inspect });
        Assert.IsEmpty(empty.Surface.Model.Rows);
    }

    [TestMethod]
    public void MemberSignaturesRenderUsingTheCurrentTheme() {
        using var app = new ObjectTableApp([PSObject.AsPSObject(new Fixture())], new() { View = ObjectView.Inspect });
        app.Surface.Model.Filter = "Compute";
        app.Surface.Model.Rebuild();
        var svg = XDocument.Parse(CellBufferSvgExporter.Export(VisualSnapshotRenderer.Render(app.Surface, 160, 25)));
        var rgb = app.Surface.Palette.Accent.ToRgb();
        Assert.IsTrue(svg.Descendants().Any(element => element.Name.LocalName == "text"
            && element.Value.Contains("int", StringComparison.Ordinal)
            && (string?)element.Attribute("fill") == $"rgb({rgb.R},{rgb.G},{rgb.B})"),
            "Method types should be highlighted in the native cell renderer.");
    }

    [TestMethod]
    public void MemberWidthsLeaveRoomForValuesAndAdaptOnResize() {
        var source = new PSObject();
        source.Properties.Add(new PSNoteProperty("LongPropertyNameForResponsiveColumns", new Dictionary<string, int[]>()));
        using var app = new ObjectTableApp([source], new() { View = ObjectView.Inspect });
        VisualSnapshotRenderer.Render(app.Surface, 80, 25);
        app.Tick();
        int narrow = app.Surface.PreferredWidths!.Take(3).Sum();
        Assert.IsLessThan(48, narrow);
        VisualSnapshotRenderer.Render(app.Surface, 160, 25);
        app.Tick();
        Assert.IsGreaterThan(narrow, app.Surface.PreferredWidths!.Take(3).Sum());
        Assert.AreEqual(0, app.Surface.PreferredWidths![3], "Values/signatures use the remaining width.");
    }
}
