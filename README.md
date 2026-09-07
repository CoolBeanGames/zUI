# zUI

zUI is a **native Windows UI framework with web-like authoring**. It is an
ahead-of-time UI compiler and native Windows widget runtime: ZML or ZSL describes
a screen; `zslc.py` emits C# that builds WinForms controls or C++ that builds
Win32/common controls. Markup and theme-source CSS are never shipped or
interpreted by the application.

zUI is **not** a web UI framework. There is no WebView2, browser process, DOM,
stylesheet cascade, or JavaScript runtime — ZML/ZSL is declarative authoring
syntax only. A coding agent should not reach for browser technology to implement
any zUI feature.

## Runtime contract

Applications build a screen **once** with `Build()` and afterwards mutate the
existing native controls in place — changing text, values, visibility,
selection, or list contents never rebuilds the tree. `Build()` is a
construction operation only. The frozen public contract is
[core/RUNTIME_CONTRACT.md](core/RUNTIME_CONTRACT.md); the architecture rules are
in [design.txt](design.txt). C# and C++ backends expose equivalent zUI semantics.

## Build

```powershell
./build.ps1 -Config test
./build.ps1 -Config debug
./build.ps1 -Config release
```

Requirements are Python 3, .NET 8, Visual Studio C++ tools, CMake, and the
Windows SDK. The test gate rejects browser documents, browser packages, and
browser-rendering references in active code.

## Compile a screen

```powershell
py compiler/zslc.py examples/showcase.zml --backend csharp -o ShowcaseUi.g.cs
py compiler/zslc.py examples/showcase.zml --backend cpp -o showcase.g.cpp
```

The C# runtime is in `bindings/csharp`; the C++ runtime is in `bindings/cpp`.
Runnable native examples are in `samples/csharp`, `samples/cpp`, `samples/zforge`
(state / bind / tabbed layout), and `samples/zsheets`. See `docs/QUICKSTART.md`
for the minimal path and [docs/INTEGRATION.md](docs/INTEGRATION.md) for the full
guide to pulling zUI into a host project (plus the visual design standard).
