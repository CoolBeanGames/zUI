using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using Microsoft.Web.WebView2.WinForms;
using ZUI;

namespace ZBrowser.Zui;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--self-test"))
            return NormalizeAddress("example.com").AbsoluteUri == "https://example.com/"
                && NormalizeAddress("https://openai.com/docs").Host == "openai.com" ? 0 : 1;

        var app = new System.Windows.Application();
        app.Run(CreateWindow());
        return 0;
    }

    private static Uri NormalizeAddress(string text)
    {
        text = text.Trim();
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            uri = new Uri("https://" + text);
        return uri;
    }

    private static Window CreateWindow()
    {
        var window = new Window { Title = "zBrowser — zUI", Width = 1180, Height = 800 };
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(92) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var chromeView = new WebView2 { Dock = System.Windows.Forms.DockStyle.Fill };
        var contentView = new WebView2 { Dock = System.Windows.Forms.DockStyle.Fill };
        var chromeSurface = new WindowsFormsHost { Child = chromeView };
        var contentSurface = new WindowsFormsHost { Child = contentView };
        Grid.SetRow(chromeSurface, 0); Grid.SetRow(contentSurface, 1);
        layout.Children.Add(chromeSurface); layout.Children.Add(contentSurface);
        window.Content = layout;
        var chrome = new ZuiHost(chromeView);
        var environment = ZuiHost.GetSharedEnvironmentAsync();

        void PublishState(string status = "Ready")
        {
            chrome.Send("browser-state", new
            {
                url = contentView.Source?.AbsoluteUri ?? "",
                title = contentView.CoreWebView2?.DocumentTitle ?? "",
                canBack = contentView.CanGoBack,
                canForward = contentView.CanGoForward,
                status
            });
        }

        void Navigate(string address)
        {
            try { contentView.CoreWebView2.Navigate(NormalizeAddress(address).AbsoluteUri); PublishState("Loading…"); }
            catch (Exception ex) { PublishState(ex.Message); }
        }

        window.Loaded += async (_, _) =>
        {
            var sharedEnvironment = await environment;
            await Task.WhenAll(
                chrome.InitializeAsync(),
                contentView.EnsureCoreWebView2Async(sharedEnvironment));
            chrome.On("browser.navigate", p => Navigate(p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : p.GetProperty("url").GetString() ?? ""));
            chrome.On("browser.action", p =>
            {
                switch (p.GetString())
                {
                    case "back" when contentView.CanGoBack: contentView.GoBack(); break;
                    case "forward" when contentView.CanGoForward: contentView.GoForward(); break;
                    case "refresh": contentView.Reload(); break;
                    case "home": Navigate("https://example.com"); break;
                }
            });
            contentView.CoreWebView2.NavigationStarting += (_, _) => PublishState("Loading…");
            contentView.CoreWebView2.NavigationCompleted += (_, e) =>
            {
                PublishState(e.IsSuccess ? "Ready" : $"Navigation error {e.WebErrorStatus}");
                window.Title = "zBrowser — " + (contentView.CoreWebView2.DocumentTitle ?? "zUI");
            };
            await chrome.LoadAsync("toolbar.html");
            Navigate("https://example.com");
        };
        return window;
    }
}
