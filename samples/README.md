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

Win32 + WebView2 (`bindings/cpp/zui.cpp` + `zui_webview2.cpp`). Needs the
**WebView2 SDK** and **WIL** headers — pass their locations to CMake:

```
cmake -S samples/cpp -B builds/test/sample-cpp ^
  -DWEBVIEW2_DIR=C:/pkgs/Microsoft.Web.WebView2 ^
  -DWIL_DIR=C:/pkgs/Microsoft.Windows.ImplementationLibrary
cmake --build builds/test/sample-cpp
```

`build.ps1` attempts this when `cmake` is present and skips it otherwise (the
SDK paths still have to be supplied for a real build).

## Runtime

Both need the evergreen **WebView2 runtime** installed (standard on current
Windows 10/11).
