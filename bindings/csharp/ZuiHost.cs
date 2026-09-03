using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ZUI
{
    /// <summary>
    /// Hosts a zUI document inside a WebView2 control and exposes the zUI
    /// message bus to managed code. The same CSS/JS core is used by the C++
    /// binding, so both languages render an identical UI.
    /// </summary>
    public sealed class ZuiHost : IAsyncDisposable
    {
        private readonly WebView2 _view;
        private readonly Dictionary<string, List<Action<JsonElement>>> _handlers = new();
        private bool _ready;

        public ZuiHost(WebView2 view)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
        }

        /// <summary>Directory holding the copied `zui/` core assets. Defaults to
        /// the folder next to this assembly.</summary>
        public string CoreRoot { get; set; } =
            Path.Combine(AppContext.BaseDirectory, "zui");

        public event EventHandler? Ready;

        public async Task InitializeAsync()
        {
            var env = await CoreWebView2Environment.CreateAsync();
            await _view.EnsureCoreWebView2Async(env);

            var core = _view.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;

            // Map a virtual host so zUI assets and host documents load over https.
            core.SetVirtualHostNameToFolderMapping(
                "zui.app", Path.GetDirectoryName(CoreRoot)!,
                CoreWebView2HostResourceAccessKind.Allow);

            core.WebMessageReceived += (_, e) => Dispatch(e.WebMessageAsJson);

            await core.AddScriptToExecuteOnDocumentCreatedAsync(
                "window.__zuiHost = { postMessage: m => window.chrome.webview.postMessage(m) };");

            _ready = true;
            Ready?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Load a zUI document by path relative to the core root's parent
        /// (so "showcase/index.html" resolves next to "zui/").</summary>
        public Task LoadAsync(string relativePath)
        {
            EnsureReady();
            _view.CoreWebView2.Navigate($"https://zui.app/{relativePath.Replace('\\', '/')}");
            return Task.CompletedTask;
        }

        /// <summary>Push a message to the UI (host -&gt; UI).</summary>
        public void Send(string channel, object? payload = null)
        {
            EnsureReady();
            var json = JsonSerializer.Serialize(new { channel, payload });
            _view.CoreWebView2.PostWebMessageAsString(json);
        }

        /// <summary>Subscribe to a UI channel (UI -&gt; host).</summary>
        public IDisposable On(string channel, Action<JsonElement> handler)
        {
            if (!_handlers.TryGetValue(channel, out var list))
                _handlers[channel] = list = new();
            list.Add(handler);
            return new Subscription(() => list.Remove(handler));
        }

        public void SetTheme(string name) => Send("theme", name);

        private void Dispatch(string webMessageJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(webMessageJson);
                var root = doc.RootElement;
                if (!root.TryGetProperty("channel", out var ch)) return;
                var name = ch.GetString();
                if (name is null || !_handlers.TryGetValue(name, out var list)) return;
                var payload = root.TryGetProperty("payload", out var p) ? p.Clone() : default;
                foreach (var h in list.ToArray()) h(payload);
            }
            catch (JsonException) { /* ignore malformed */ }
        }

        private void EnsureReady()
        {
            if (!_ready)
                throw new InvalidOperationException("Call InitializeAsync() before using ZuiHost.");
        }

        public async ValueTask DisposeAsync()
        {
            _handlers.Clear();
            await Task.CompletedTask;
        }

        private sealed class Subscription : IDisposable
        {
            private readonly Action _dispose;
            public Subscription(Action dispose) => _dispose = dispose;
            public void Dispose() => _dispose();
        }
    }
}
