# Build and help ownership

Build from the repository root with PowerShell 7.6 and the .NET 10 SDK:

```powershell
./build.ps1
```

The build publishes into output, validates the manifest, generates help and runs
configured tests/formatting checks. Build-time tools are distinct from runtime
dependencies. The collector remains netstandard2.0; optional telemetry must not
add runtime packages.

## Documentation sources

- README.md is the entry point: installation, a few examples and links.
- docs/en-US/*.md is authored cmdlet help. PlatyPS generates MAML in output/en-US.
  The private library does not ship a second copy of MAML.
- UpdateHelp refreshes command metadata from the built module while preserving
  authored descriptions. Review that diff; metadata refresh does not validate
  examples or rewrite guides.
- C# XML comments describe developer APIs. Compiler XML is removed from the
  distributed private library and is not the source of shipped cmdlet help.
- docs/guides contains user workflows; examples/README.md catalogs runnable scripts.
- docs/maintainers contains stable implementation and contributor references.

Keep guides outside docs/en-US: GenerateHelp treats Markdown there as command
help. Keep parameter contracts in help when moving tutorials into guides, and add
related links to the maintained guide.

## Validation

Check local documentation links, parse PowerShell examples, regenerate MAML and
run help tests after moving content. Verify claims against source, not scratch
notes. Implementation support and live hardware validation are separate claims.

Use the repository Format task and solution builds. Roslyn cannot express every
layout preference: keep auto-properties compact, but write executable blocks
expanded with one statement per line. Isolate formatting from behavior changes so
the latter can be reviewed independently.
