# zUI v0.1 compatibility

Version `v0.1.0` is the first integration baseline.

- Host platforms: Windows 10/11 with the Evergreen WebView2 Runtime.
- Managed binding: .NET 8 Windows applications using the WinForms WebView2
  control. A WPF application can host the binding through `WindowsFormsHost`.
- Native binding: C++17 with the supplied Win32/WebView2 backend; WebView2 SDK
  `1.0.2478.35` and WIL `1.0.240122.1` are the CI-tested package versions.
- Compiler: Python 3; `.zsl` and `.zml` inputs are AOT-compiled and are not
  required at runtime. `--backend html` plus `LoadAsync` is the stable default.
  Generated C# and C++ backends are advanced integration surfaces.
- Theme contract: Holo is the default. Themes may override the documented token
  set in `core/css/THEMES.md`; component markup and the host message envelope
  remain the same across themes.

The `0.1.x` line may add components and messages. Existing channel names, the
`{channel,payload}` envelope, documented ZML attributes, and required theme token
names will not intentionally break without a compatibility note.
