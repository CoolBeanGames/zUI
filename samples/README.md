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

### Build it (verified locally + on CI)

`build.ps1 -Config test` builds and runs the whole C++ side automatically: it
finds CMake (standalone, or the one bundled with Visual Studio via `vswhere`),
runs inside `vcvars64`, builds `bindings/cpp` (+ `ctest`), and builds
`samples/cpp` when the WebView2 SDK is available.

For the WebView2 sample it looks for the SDK in `pkgs/` at the repo root. To
populate it (any of: NuGet CLI, or just download the `.nupkg` which are zips):

```
mkdir pkgs
curl -L -o pkgs/wv2.zip https://www.nuget.org/api/v2/package/Microsoft.Web.WebView2/1.0.2478.35
curl -L -o pkgs/wil.zip https://www.nuget.org/api/v2/package/Microsoft.Windows.ImplementationLibrary/1.0.240122.1
tar -xf pkgs/wv2.zip -C pkgs/Microsoft.Web.WebView2
tar -xf pkgs/wil.zip -C pkgs/Microsoft.Windows.ImplementationLibrary
```

Manual CMake (if not using `build.ps1`), from a Developer prompt:

```
cmake -S samples/cpp -B build/sample-cpp -G Ninja -DCMAKE_BUILD_TYPE=Release ^
      -DWEBVIEW2_DIR=%CD%/pkgs/Microsoft.Web.WebView2 ^
      -DWIL_DIR=%CD%/pkgs/Microsoft.Windows.ImplementationLibrary
cmake --build build/sample-cpp
build\sample-cpp\zui_sample.exe
```

`.github/workflows/ci.yml` runs the equivalent (via NuGet) on every push — the
canonical cross-check.

Requires the evergreen **WebView2 runtime** at run time (standard on Win 10/11).
The Win32 sample calls `CoInitializeEx(STA)` — WebView2 needs a single-threaded
apartment on the UI thread.

The pure-core translation unit (`zui.cpp`, no WebView2) plus the envelope unit
test build with just CMake + a compiler — no SDK:

```
cmake -S bindings/cpp -B build/cpp -DZUI_BUILD_TESTS=ON
cmake --build build/cpp && ctest --test-dir build/cpp
```

## Runtime

Both need the evergreen **WebView2 runtime** installed (standard on current
Windows 10/11).
