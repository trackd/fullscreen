using System.Reflection;
using System.Runtime.Loader;

namespace fullscreen.ALC;
/// <summary>Loads Fullscreen and its managed dependencies privately so other modules' versions cannot conflict.</summary>
public sealed class LoadContext : AssemblyLoadContext {
    private static readonly Lock s_sync = new();
    private static Assembly? s_module;
    private readonly string _directory;
    private LoadContext(string directory) : base("fullscreen", isCollectible: false) =>
        _directory = directory;
    /// <inheritdoc/>
    protected override Assembly? Load(AssemblyName assemblyName) {
        // Share the shim itself; System.Management.Automation and the framework are not shipped, so they fall through to the default context.
        if (assemblyName.Name == "fullscreen.ALC") {
            return typeof(LoadContext).Assembly;
        }

        // Private copies win even when another module preloaded a compatible version.
        string path = Path.Join(_directory, assemblyName.Name + ".dll");
        return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
    }

    /// <summary>Loads the module entry assembly using the isolated dependency context.</summary>
    /// <returns>The loaded fullscreen assembly.</returns>
    public static Assembly Initialize() {
        lock (s_sync) {
            if (s_module is null) {
                string directory = Path.GetDirectoryName(typeof(LoadContext).Assembly.Location) ?? throw new InvalidOperationException("Unable to determine the Fullscreen module directory.");
                s_module = new LoadContext(directory).LoadFromAssemblyPath(Path.Join(directory, "fullscreen.dll"));
            }

            return s_module;
        }
    }
}
