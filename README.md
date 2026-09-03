# zUI

A visual UI subsystem that sits on top of native applications (a music app, a game
engine, a project-management app) and gives all of them one consistent, themeable
UI experience.

## Design

The visual language is defined in [`design.txt`](design.txt): a restrained,
native-desktop productivity aesthetic. The first shipped theme is **holo**
(`core/css/themes/holo.css`); additional themes can be dropped in later without
touching component markup.

## Architecture

zUI is a **CSS + small JS runtime** rendered inside a host-provided web view. Both
a C++ and a C# binding embed the exact same assets, so every host application gets
a pixel-identical result.

```
core/
  css/
    tokens.css          design tokens (colour, spacing, type, border) as CSS vars
    base.css            reset + platform typography
    components/*.css     one file per widget, all driven by tokens
    themes/holo.css     concrete token values for the holo theme
  js/
    zui.js              runtime: component behaviours + host<->UI message bus
bindings/
  csharp/               ZUI .NET library (WebView2 host)
  cpp/                  zui C++ wrapper (host-webview agnostic)
showcase/               standalone app demoing every component
```

### The message bus

UI and host talk over a single JSON channel. From the UI:
`zui.send(channel, payload)`; the host replies / pushes with
`zui.receive(channel, handler)`. Bindings map this onto their platform's web-view
IPC (`postMessage` / `CoreWebView2.WebMessageReceived`).

## Using it

### C#

```csharp
using ZUI;

var ui = new ZuiHost(webView2Control);
await ui.LoadAsync("showcase/index.html");   // or your own zUI document
ui.On("save", json => Save(json));
ui.Send("theme", "holo");
```

### C++

```cpp
#include "zui.h"

zui::Host ui(hwnd);            // wraps a platform web view
ui.load("showcase/index.html");
ui.on("save", [](const std::string& json){ save(json); });
ui.send("theme", "holo");
```

## Building

`build.ps1` copies `core/` into `builds/<debug|test|release>/zui/` and builds the
bindings. The showcase app is pure static assets - open `showcase/index.html`.

## Status

Task ZU-1: base systems / "bones". Component CSS, the token system, the JS runtime,
both bindings and the showcase shell are in place. Later tasks flesh out
individual widgets (ZU-2), the UI scripting language (ZU-3) and the full holo
implementation (ZU-4).
