using System.Text.Json;
using Microsoft.Web.WebView2.WinForms;
using ZUI;

namespace ZSheets;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--self-test")) return SelfTest();

        ApplicationConfiguration.Initialize();
        var form = new Form { Text = "zSheets — Untitled", Width = 1100, Height = 760 };
        var view = new WebView2 { Dock = DockStyle.Fill };
        form.Controls.Add(view);
        var host = new ZuiHost(view);
        string? currentPath = null;

        void SetPath(string? path)
        {
            currentPath = path;
            form.Text = $"zSheets — {(path is null ? "Untitled" : Path.GetFileName(path))}";
            host.Send("file-status", new { name = path is null ? "Untitled.csv" : Path.GetFileName(path), saved = true });
        }

        void OpenCsv()
        {
            using var dialog = new OpenFileDialog { Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*" };
            if (dialog.ShowDialog(form) != DialogResult.OK) return;
            try
            {
                var data = CsvCodec.Parse(File.ReadAllText(dialog.FileName));
                host.Send("sheet-load", new { headers = data.Headers, rows = data.Rows });
                SetPath(dialog.FileName);
            }
            catch (Exception ex) { host.Send("file-error", ex.Message); }
        }

        void SaveCsv(JsonElement payload, bool choosePath)
        {
            var target = currentPath;
            if (choosePath || target is null)
            {
                using var dialog = new SaveFileDialog
                {
                    Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                    DefaultExt = "csv",
                    FileName = target is null ? "Untitled.csv" : Path.GetFileName(target)
                };
                if (dialog.ShowDialog(form) != DialogResult.OK) return;
                target = dialog.FileName;
            }

            try
            {
                var headers = payload.GetProperty("headers").EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
                var rows = payload.GetProperty("rows").EnumerateArray()
                    .Select(r => r.EnumerateArray().Select(x => x.GetString() ?? "").ToArray()).ToList();
                File.WriteAllText(target!, CsvCodec.Write(new SheetData(headers, rows)));
                SetPath(target);
            }
            catch (Exception ex) { host.Send("file-error", ex.Message); }
        }

        form.Load += async (_, _) =>
        {
            await host.InitializeAsync();
            host.On("file.open", _ => OpenCsv());
            host.On("file.save", p => SaveCsv(p, false));
            host.On("file.save-as", p => SaveCsv(p, true));
            host.On("file.new", _ => SetPath(null));
            await host.LoadAsync("app.html");
            SetPath(null);
        };

        Application.Run(form);
        return 0;
    }

    private static int SelfTest()
    {
        const string input = "Name,Note\r\nAlpha,One\r\n\"Beta, B\",\"line 1\r\nline \"\"2\"\"\"\r\n";
        var parsed = CsvCodec.Parse(input);
        var roundTrip = CsvCodec.Parse(CsvCodec.Write(parsed));
        return roundTrip.Headers.SequenceEqual(parsed.Headers)
            && roundTrip.Rows.Count == 2
            && roundTrip.Rows[1][0] == "Beta, B"
            && roundTrip.Rows[1][1] == "line 1\r\nline \"2\"" ? 0 : 1;
    }
}
