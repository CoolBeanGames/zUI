# zUI quickstart: ZML to a native .NET app

This is the shortest supported path from one UI file to a native Windows
executable. The ZML is compiled ahead of time; the executable hosts the compiled
document in WebView2 and uses the same Holo-themed zUI widgets as every other
binding.

## Prerequisites

- Python 3 (runs `compiler/zslc.py`).
- .NET 8 SDK (builds the C# host).
- Microsoft Edge WebView2 Runtime. It is included on current Windows 10/11, and
  the Evergreen Runtime installer is available from Microsoft for deployment.
- Only for the C++ path: Visual Studio/MSVC with the Windows SDK, CMake, the
  WebView2 SDK, and WIL. None of these C++ tools are needed for this quickstart.

For application work, use `--backend html` plus `ZuiHost.LoadAsync`. The
`csharp` and `cpp` compiler backends are advanced AOT-integration options.

## 1. Author one screen

Create `HelloZui/hello.zml`:

```xml
<window title="Hello zUI">
  <col>
    <text bind="status"/>
    <button on="hello.clicked">Say hello</button>
  </col>
</window>
<state>
  <var name="status" value="Waiting for the host"/>
</state>
```

From the zUI repository root, compile it. `--asset-base zui` points the generated
document at the assets copied beside the executable by the zUI project reference:

```powershell
py compiler/zslc.py HelloZui/hello.zml --backend html --asset-base zui -o HelloZui/hello.html
```

## 2. Create the native host

```powershell
dotnet new winforms -n HelloZui -f net8.0
dotnet add HelloZui/HelloZui.csproj reference bindings/csharp/ZUI.csproj
```

Add this item to `HelloZui/HelloZui.csproj` so the compiled screen ships beside
the executable:

```xml
<ItemGroup>
  <None Include="hello.html" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

Replace `HelloZui/Program.cs` with:

```csharp
using Microsoft.Web.WebView2.WinForms;
using ZUI;

ApplicationConfiguration.Initialize();

var form = new Form { Text = "Hello zUI", Width = 640, Height = 420 };
var view = new WebView2 { Dock = DockStyle.Fill };
form.Controls.Add(view);
var ui = new ZuiHost(view);

form.Load += async (_, _) =>
{
    await ui.InitializeAsync();

    // UI -> native host: the ZML button emits this channel.
    ui.On("hello.clicked", _ => form.Text = "The UI called .NET");

    await ui.LoadAsync("hello.html");

    // Native host -> UI: generated state glue updates the bound text widget.
    ui.Send("state", new { status = "Hello from .NET" });
};

Application.Run(form);
```

Build and run:

```powershell
dotnet run --project HelloZui/HelloZui.csproj
```

The window should show the dark Holo theme and “Hello from .NET”. Clicking the
button changes the native window title, proving one message in each direction.

For the complete message contract see `core/PROTOCOL.md`; for all ZML elements
and attributes see `compiler/GRAMMAR.md`.
