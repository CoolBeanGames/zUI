# zUI build 2 — native Windows rendering

This release replaces the browser-rendered widget stack with native Windows
controls from compiler output through application packaging.

## Highlights

- C# compiler output now constructs typed `ZuiNode` trees; `ZuiHost` maps them
  to WinForms controls and handles events, lookup, and Holo/Clean theme colors.
- C++ compiler output now constructs `zui::Node` trees; `zui::Host` creates
  Win32 and common-control HWNDs and dispatches events in process.
- Generated files contain native source only. ZML/ZSL and theme-source CSS are
  development inputs and are not copied into application artifacts.
- zSheets is a native `ToolStrip`/`DataGridView` CSV editor.
- Removed the former browser backend, browser package dependencies, page/script
  assets, browser demos, and browser-driven tests.
- CI and `build.ps1` enforce a native-only policy and build both native targets.

## Verification

- 11/11 compiler/parser/native-output tests passed.
- Native-only policy passed with no browser documents or active browser engine
  dependencies.
- C# runtime, generated sample, and zSheets built with zero warnings/errors.
- C++ runtime and sample built with MSVC; envelope and native HWND host CTests
  both passed.
- Debug and release smoke checks passed for the C# sample, C++ sample, and
  zSheets executables.

## Requirements

Windows 10/11, Python 3 for compilation, .NET 8 for C# targets, and Visual
Studio C++ tools/CMake/Windows SDK for C++ targets. Deployed applications need
only their normal native framework/runtime dependencies.
