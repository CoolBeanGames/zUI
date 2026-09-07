# C# native runtime

Reference `ZUI.csproj`, create `new ZuiHost(formOrPanel)`, and call the generated
screen's `Build(host)` method. `ZuiHost` maps nodes to WinForms controls, exposes
named controls with `Find`, routes control events through `On`, and applies Holo
or Clean colors with `SetTheme`.

The runtime has no external package dependencies. Accessibility, keyboard
navigation, input and painting remain in the Windows control stack.
