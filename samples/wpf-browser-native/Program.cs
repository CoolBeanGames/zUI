using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Wpf;

namespace ZBrowser.Native;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--self-test"))
            return NormalizeAddress("example.com").AbsoluteUri == "https://example.com/"
                && NormalizeAddress("https://openai.com/docs").Host == "openai.com" ? 0 : 1;

        var app = new Application();
        app.Run(CreateWindow());
        return 0;
    }

    internal static Uri NormalizeAddress(string text)
    {
        text = text.Trim();
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            uri = new Uri("https://" + text);
        return uri;
    }

    private static Window CreateWindow()
    {
        var window = new Window { Title = "zBrowser — Native WPF", Width = 1180, Height = 800 };
        var root = new DockPanel();
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6) };
        DockPanel.SetDock(toolbar, Dock.Top);
        var back = new Button { Content = "Back", MinWidth = 64, Margin = new Thickness(0, 0, 4, 0) };
        var forward = new Button { Content = "Forward", MinWidth = 64, Margin = new Thickness(0, 0, 4, 0) };
        var refresh = new Button { Content = "Refresh", MinWidth = 64, Margin = new Thickness(0, 0, 6, 0) };
        var address = new TextBox { Text = "https://example.com", MinWidth = 600, VerticalContentAlignment = VerticalAlignment.Center };
        var go = new Button { Content = "Go", MinWidth = 52, Margin = new Thickness(6, 0, 0, 0) };
        toolbar.Children.Add(back); toolbar.Children.Add(forward); toolbar.Children.Add(refresh);
        toolbar.Children.Add(address); toolbar.Children.Add(go);
        var view = new WebView2();
        root.Children.Add(toolbar); root.Children.Add(view); window.Content = root;

        async void Navigate()
        {
            try { await view.EnsureCoreWebView2Async(); view.CoreWebView2.Navigate(NormalizeAddress(address.Text).AbsoluteUri); }
            catch (Exception ex) { window.Title = "zBrowser — " + ex.Message; }
        }
        go.Click += (_, _) => Navigate();
        address.KeyDown += (_, e) => { if (e.Key == Key.Enter) Navigate(); };
        back.Click += (_, _) => { if (view.CanGoBack) view.GoBack(); };
        forward.Click += (_, _) => { if (view.CanGoForward) view.GoForward(); };
        refresh.Click += (_, _) => view.Reload();
        window.Loaded += async (_, _) =>
        {
            await view.EnsureCoreWebView2Async();
            view.CoreWebView2.NavigationCompleted += (_, _) =>
            {
                address.Text = view.Source?.AbsoluteUri ?? address.Text;
                back.IsEnabled = view.CanGoBack; forward.IsEnabled = view.CanGoForward;
                window.Title = "zBrowser — " + (view.CoreWebView2.DocumentTitle ?? "Native WPF");
            };
            Navigate();
        };
        return window;
    }
}
