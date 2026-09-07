# zUI_build1

First GitHub release of zUI, based on the merged `refactor` branch and the
`v0.1.0` compatibility baseline.

## Highlights

- Ahead-of-time ZSL/ZML compiler with HTML, C#, and C++ output. Generated C++
  screens now compile and link directly into native applications through an
  explicit handler map.
- Native C# and C++ WebView2 host bindings using the same Holo-themed component
  assets and JSON message protocol.
- Source-bound table rendering fixed for real WebView2 DOM normalization, with
  a generated-document headless regression test.
- Holo UI component system, theme tokens, icons, interaction layer, component
  IO, virtual lists, and standalone showcase/documentation surfaces.

## Applications

- zSheets: native .NET host with an editable Holo spreadsheet, keyboard cell
  navigation, row/column operations, and quoted/multiline CSV open and save.
- WPF browser comparison: separate native-control and Holo zUI-chrome builds,
  both with Back, Forward, Refresh, Home, URL normalization, and title/history
  state. The zUI build communicates through the normal host message bus.
- C# and C++ native integration samples, including a generated UI translation
  unit in the C++ executable.

## Reliability and performance

- Fixed the zSheets table/SVG `grid` ID collision; its test now proves every
  generated cell belongs to the visible `sheetGrid` table.
- C# initialization/disposal is idempotent and detaches WebView2 subscriptions.
- C++ injected backends receive the same startup bridge as default backends;
  queued messages flush on DOM readiness or successful navigation completion.
- Test builds now fail immediately on missing runtime markers or failed Python,
  .NET, CMake, CTest, or sample commands instead of reporting false success.
- Startup improvements include early/shared WebView2 environment warm-up,
  selective component CSS, removal of unused sprite parsing, coalesced virtual
  list frames, and deferred large-grid construction.
- Automated Holo policy checks cover every first-party zUI surface and prohibit
  undefined tokens or hard-coded application/component colors.

## Verification

- 11 compiler tests.
- Token and Holo policy checks across 19 core files and five application/docs
  surfaces.
- Headless IO, navigation/reactivity, generated table, zSheets grid, and zUI
  browser toolbar checks.
- zSheets CSV and both browser URI self-tests.
- C# binding and all .NET application builds.
- C++ envelope and injected-backend tests plus the WebView2 sample link.
- Debug, test, and release builds; release executables started and remained
  responsive in smoke testing.
- No vulnerable NuGet dependencies reported by the configured package sources.

## Compatibility

Windows 10/11 with the Evergreen WebView2 Runtime is supported. The C# binding
targets .NET 8; the native binding uses C++17. Python 3 is required only to run
the compiler. `--backend html` with `ZuiHost.LoadAsync` is the recommended app
integration path; generated C# and C++ are advanced AOT options. See
`docs/COMPATIBILITY.md` and `docs/QUICKSTART.md`.
