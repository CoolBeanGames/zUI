# ZUI - .NET binding

Embeds the shared zUI core (`../../core`) in a [WebView2] control and bridges the
zUI message bus to managed code.

```csharp
var host = new ZuiHost(webView2Control);
await host.InitializeAsync();
await host.LoadAsync("showcase/index.html");

host.On("save", payload => File.WriteAllText("out.json", payload.GetRawText()));
host.SetTheme("holo");
host.Send("device", new { name = "HAPTICS' IPOD", freeGb = 234.6 });
```

## Build

```
dotnet build bindings/csharp/ZUI.csproj -c Debug   -o builds/debug/csharp
dotnet build bindings/csharp/ZUI.csproj -c Release -o builds/release/csharp
```

The build copies `core/` to `zui/` next to the output so a host app that
references `ZUI.dll` ships the assets automatically.

## Notes

- `net8.0-windows` and `net472` are both targeted.
- Requires the WebView2 runtime (evergreen) on the target machine.
- Channels are plain JSON `{ "channel": string, "payload": any }`.

[WebView2]: https://learn.microsoft.com/microsoft-edge/webview2/
