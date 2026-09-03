# zUI

A visual UI subsystem that sits on top of native applications (a music app, a game
engine, a project-management app) and gives all of them one consistent, themeable
UI experience.

## Design

The default theme is **holo** — Google's Android Holo look: dark chrome, the
Holo-blue `#33b5e5` accent, thin dividers, uppercase accent-coloured section
headers. It is the `:root` token set in `core/css/tokens.css` and is restated in
`core/css/themes/holo.css` for explicit selection.

A light, neutral "native Windows utility" alternative — the aesthetic spelled out
in [`design.txt`](design.txt) — ships as the **clean** theme
(`core/css/themes/clean.css`). Structure, spacing, typography and interaction are
identical between themes; only the palette differs. A theme is just a file that
re-declares tokens — new themes need no component changes. See
[`core/css/THEMES.md`](core/css/THEMES.md).

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

### The message bus & component IO

UI and host talk over a single JSON channel. From the UI:
`zui.send(channel, payload)`; the host replies / pushes with
`zui.receive(channel, handler)`. Bindings map this onto their platform's web-view
IPC (`postMessage` / `CoreWebView2.WebMessageReceived`).

Any component tagged `data-zui-id` (or ZSL/ZML `export="…"`) is readable and
writable both ways — text, checkboxes, selects, progress bars, labels, button
clicks/state, and scroll positions. `zui.values()` / `zui.field(id)` /
`zui.set(id, value)` in the page; `value` / `set` / `set-many` / `query` /
`submit` on the bus. Full contract in [`core/PROTOCOL.md`](core/PROTOCOL.md).

## ZSL - the UI scripting language

UI is written in **ZSL** and compiled ahead of time by
[`compiler/zslc.py`](compiler/README.md) - never interpreted at runtime. Two
interchangeable syntaxes, auto-detected:

```
panel "Tracks" { button "Play" }                          # brace  .zsl
<panel title="Tracks"><button>Play</button></panel>       # ZML    .zml
```

Backends: `html` (a self-contained zUI document), `csharp` and `cpp` (native
source built into the host). See [`compiler/GRAMMAR.md`](compiler/GRAMMAR.md) and
[`examples/`](examples/).

```
py compiler/zslc.py examples/showcase.zml --backend html -o showcase.html
```

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

## Documentation

`docs/index.html` — a click-through documentation site (styled with zUI itself)
covering embedding, the ZSL/ZML language, components, the message-bus/IO
protocol, theming, icons and building. Each section points at the authoritative
markdown file. `design.txt` is the integration + visual-standard guide for
agents working in host projects.

## CI

`.github/workflows/ci.yml` builds the whole project on `windows-latest` on every
push: the Python compiler + token tests, the headless runtime self-tests
(IO + reactivity), the C# binding and sample, the C++ core (+ envelope ctest),
and the C++ WebView2 backend + sample against pinned WebView2/WIL packages.

## Status

Task ZU-1: base systems / "bones". Component CSS, the token system, the JS runtime,
both bindings and the showcase shell are in place. Later tasks flesh out
individual widgets (ZU-2), the UI scripting language (ZU-3) and the full holo
implementation (ZU-4).
