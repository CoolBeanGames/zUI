using System;
using System.Windows.Forms;
using ZUI;
using ZuiSample.Generated;

namespace ZuiSample;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        using var form = new Form { Text = "zUI native C# sample", Width = 1180, Height = 820 };
        using var host = new ZuiHost(form);
        var ui = new ShowcaseUi();
        ui.Build(host);
        host.On("view.holo", _ => host.SetTheme("holo"));
        host.On("view.clean", _ => host.SetTheme("clean"));
        host.On("file.exit", _ => form.Close());
        if (Array.Exists(args, x => x == "--self-test"))
            return host.Find("tracks") is DataGridView ? 0 : 1;
        Application.Run(form);
        return 0;
    }
}
