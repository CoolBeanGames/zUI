# zui - C++ binding

Wraps a platform web view and bridges the zUI JSON message bus to C++. Shares the
exact `../../core` CSS/JS with the C# binding.

## Files

| file                | role                                                        |
|---------------------|-------------------------------------------------------------|
| `zui.h` / `zui.cpp` | platform-independent `Host`, envelope reader/writer         |
| `zui_webview2.cpp`  | Windows backend (WebView2) - added by the host build        |
| `CMakeLists.txt`    | static `zui` lib + core-asset staging + optional tests      |
| `tests/`            | dependency-free unit checks                                 |

## Use

```cpp
#include "zui.h"

zui::Host ui(hwnd);
ui.set_core_root("zui");
ui.load("showcase/index.html");
ui.on("selection", [](const std::string& json) { /* json = ["3","4"] */ });
ui.send("device", R"({"name":"HAPTICS' IPOD","freeGb":234.6})");
ui.set_theme("holo");
```

## Build

```
cmake -S bindings/cpp -B builds/test/cpp -DZUI_BUILD_TESTS=ON
cmake --build builds/test/cpp
ctest --test-dir builds/test/cpp
```

The Windows backend depends on the WebView2 SDK (`Microsoft.Web.WebView2` NuGet
or the standalone SDK) and `WebView2Loader.dll` at runtime.
