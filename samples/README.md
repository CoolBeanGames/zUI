# zUI host samples

Two minimal host apps that open the **same** `showcase/index.html` through their
binding and round-trip the same messages (`device`, `now-playing`, `selection`,
`theme-changed`, `transport`). Because both embed the identical `core/` assets,
they render a pixel-identical UI.

## C# (`csharp/`)

WinForms + WebView2, references `bindings/csharp/ZUI.csproj`.

```
dotnet build samples/csharp/ZuiSample.csproj -c Debug -o builds/debug/sample-csharp
builds/debug/sample-csharp/ZuiSample.exe
```

`build.ps1` builds this automatically wherever `dotnet` is available.

## C++ (`cpp/`)

Win32 + WebView2 (`bindings/cpp/zui.cpp` + `zui_webview2.cpp`).

### Exact verified recipe

Prereqs: MSVC (VS 2022 Build Tools, "Desktop development with C++"), CMake ≥ 3.20,
NuGet CLI. Then, from the repo root:

```
nuget install Microsoft.Web.WebView2 -Version 1.0.2478.35 ^
      -OutputDirectory pkgs -ExcludeVersion
nuget install Microsoft.Windows.ImplementationLibrary -Version 1.0.240122.1 ^
      -OutputDirectory pkgs -ExcludeVersion

cmake -S samples/cpp -B build/sample-cpp ^
      -DWEBVIEW2_DIR=%CD%/pkgs/Microsoft.Web.WebView2 ^
      -DWIL_DIR=%CD%/pkgs/Microsoft.Windows.ImplementationLibrary
cmake --build build/sample-cpp --config Release
build\sample-cpp\Release\zui_sample.exe
```

This is exactly what `.github/workflows/ci.yml` runs on every push, so the
Windows CI job is the canonical proof the C++ side builds and its unit tests
pass. `build.ps1` runs the same steps locally when `cmake` is present and
`WEBVIEW2_DIR` / `WIL_DIR` are set; it skips them otherwise.

The pure-core translation unit (`zui.cpp`, no WebView2) plus the envelope unit
test build with just CMake + a compiler — no SDK:

```
cmake -S bindings/cpp -B build/cpp -DZUI_BUILD_TESTS=ON
cmake --build build/cpp && ctest --test-dir build/cpp
```

## Runtime

Both need the evergreen **WebView2 runtime** installed (standard on current
Windows 10/11).
