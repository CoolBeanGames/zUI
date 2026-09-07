# zUI

zUI is an ahead-of-time UI compiler and native Windows widget runtime. ZML or
ZSL describes a screen; `zslc.py` emits C# that builds WinForms controls or C++
that builds Win32/common controls. Markup and theme-source CSS are never shipped
or interpreted by the application.

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
Runnable native examples are in `samples/csharp`, `samples/cpp`, and
`samples/zsheets`. See `docs/QUICKSTART.md` for integration.
