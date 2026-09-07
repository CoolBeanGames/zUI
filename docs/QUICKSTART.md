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

## Build once, then mutate

`Build(host)` **constructs** the control tree. Call it once. After that, update
the screen by mutating the existing native controls — look them up by their
`id`/`export`/`bind` name and change the property:

```csharp
var ui = new MyApp.Generated.ScreenUi();
ui.Build(host);

host.On("save", _ => Save());

// later, in response to app state — NOT another Build() call:
((Label)host.Find("status")!).Text = "Saved";
((TrackBar)host.Find("progress")!).Value = 42;
```

Calling `Build()` again destroys controls, selection, and focus and emits a
warning. The frozen rules — construction vs. mutation, control lookup, C#/C++
parity, and the no-browser policy — are in
[../core/RUNTIME_CONTRACT.md](../core/RUNTIME_CONTRACT.md).
