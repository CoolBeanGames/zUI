# Native C# quickstart

1. Author `screen.zml`.
2. Compile it: `py compiler/zslc.py screen.zml --backend csharp --class ScreenUi --namespace MyApp.Generated -o ScreenUi.g.cs`.
3. Reference `bindings/csharp/ZUI.csproj` from a `net8.0-windows` WinForms app.
4. Construct and build:

```csharp
using var host = new ZUI.ZuiHost(form);
new MyApp.Generated.ScreenUi().Build(host);
host.On("save", payload => Save());
```

The generated `.cs` file is the complete UI input to the executable. The source
ZML and theme-source CSS are development inputs only and are not copied to output.
