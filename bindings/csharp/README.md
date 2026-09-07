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

For faster startup, `ZuiHost` reuses one process-wide WebView2 environment.
Applications with additional WebView2 controls can share it too:

```csharp
var environment = await ZuiHost.GetSharedEnvironmentAsync(); // may be started early
await otherView.EnsureCoreWebView2Async(environment);
```

Starting `GetSharedEnvironmentAsync()` while constructing the window overlaps
the expensive browser-process warm-up with normal application initialization.

## Build

```
dotnet build bindings/csharp/ZUI.csproj -c Debug   -o builds/debug/csharp
dotnet build bindings/csharp/ZUI.csproj -c Release -o builds/release/csharp
```

The build copies `core/` to `zui/` next to the output so a host app that
references `ZUI.dll` ships the assets automatically.

## Notes

- Targets `net8.0-windows` (uses `System.Text.Json` / `IAsyncDisposable`).
- Requires the WebView2 runtime (evergreen) on the target machine.
- Channels are plain JSON `{ "channel": string, "payload": any }`.
- `InitializeAsync()` and `DisposeAsync()` are idempotent; disposal detaches the
  binding's WebView2 event handlers but does not dispose the caller-owned view.

[WebView2]: https://learn.microsoft.com/microsoft-edge/webview2/
