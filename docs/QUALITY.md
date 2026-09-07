# zUI quality baseline

The repository treats `build.ps1 -Config test` as the local release gate. It
must finish with a zero exit code; required external tools and runtime checks do
not degrade to warning-only success.

The gate covers:

- ZSL/ZML parsing and HTML/C#/C++ generation, including source-bound tables.
- Holo/theme token policy across core CSS and every first-party zUI surface.
- Headless component IO, navigation/reactivity, generated table binding,
  zSheets visible-grid operations, and zBrowser Holo toolbar behavior.
- C# binding plus every .NET sample, including CSV and URI self-tests.
- C++ message-envelope and injected-backend tests.
- The native C++ WebView2 sample, including its generated UI translation unit.

Build outputs are isolated under `builds/test`, `builds/debug`, and
`builds/release`. Runnable application smoke targets are:

```text
builds/<config>/sample-csharp/ZuiSample.exe
builds/<config>/zsheets/zSheets.exe
builds/<config>/wpf-browser-native/zBrowser.Native.exe
builds/<config>/wpf-browser-zui/zBrowser.zUI.exe
builds/<config>/sample-cpp/zui_sample.exe
```

## UI policy

Holo is the required default for new first-party application UI. Component and
app-specific CSS uses the shared `--zui-*` tokens, so alternate themes remain a
palette swap rather than a component fork. The stock-WPF browser sample is kept
only as the explicit “before” half of a comparison; the zUI version is the
implementation model.

## Startup policy

Start the shared WebView2 environment early with
`ZuiHost.GetSharedEnvironmentAsync()`, reuse it for additional views in the
process, load only the component CSS needed by small screens, omit unused icon
sprites, and add large dynamic DOMs after the static zUI chrome is wired. The
three comparison applications exercise these patterns in automated smoke tests.
