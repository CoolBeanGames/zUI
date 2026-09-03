using System;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using ZUI;

namespace ZuiSample;

/// <summary>
/// Minimal host: a WinForms window that runs the zUI showcase through ZuiHost
/// and round-trips a few messages with it. The C++ sample (../cpp) does the
/// same thing against the same core assets, so both render an identical UI.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var form = new Form { Text = "zUI sample (C#)", Width = 1180, Height = 820 };
        var view = new WebView2 { Dock = DockStyle.Fill };
        form.Controls.Add(view);

        var ui = new ZuiHost(view);

        form.Load += async (_, _) =>
        {
            await ui.InitializeAsync();

            // UI -> host
            ui.On("selection", p => form.Text = $"zUI sample (C#) - {p.GetArrayLength()} selected");
            ui.On("theme-changed", p => Console.WriteLine($"theme -> {p.GetString()}"));
            ui.On("save", p => Console.WriteLine($"save: {p.GetRawText()}"));
            ui.On("transport", p => Console.WriteLine($"transport: {p.GetString()}"));

            // Two ways to render a screen:
            //  (a) load a hand-authored / html-compiled document:
            await ui.LoadAsync("showcase/index.html");
            //  (b) render a ZSL/ZML screen compiled with `zslc --backend csharp`:
            //        new ZuiSample.Generated.ShowcaseUi().Attach(ui);
            _ = typeof(ZuiSample.Generated.ShowcaseUi);   // keep the generated class compiled

            // host -> UI (buffered until the DOM is ready)
            ui.Send("device", new { name = "HAPTICS' IPOD", capacity = "238.2 GB", free = "234.6 GB" });
            ui.Send("now-playing", new { title = "Nightdrive", sub = "Aria Kane - Long Exposure", position = 108, duration = 281 });
        };

        Application.Run(form);
    }
}
