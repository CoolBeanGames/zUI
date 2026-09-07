namespace ZSheets;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--self-test")) return SelfTest();
        ApplicationConfiguration.Initialize();
        var form = new Form { Text = "zSheets — Untitled", Width = 1100, Height = 760 };
        var grid = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersWidth = 55 };
        var bar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
        var open = new ToolStripButton("Open"); var save = new ToolStripButton("Save");
        var saveAs = new ToolStripButton("Save As"); var addRow = new ToolStripButton("Add row");
        bar.Items.AddRange([open, save, saveAs, new ToolStripSeparator(), addRow]);
        form.Controls.Add(grid); form.Controls.Add(bar);
        string? currentPath = null;

        void LoadSheet(SheetData sheet)
        {
            grid.Columns.Clear(); grid.Rows.Clear();
            foreach (var header in sheet.Headers) grid.Columns.Add(header, header);
            foreach (var row in sheet.Rows) grid.Rows.Add(row.Cast<object>().ToArray());
        }

        SheetData ReadSheet()
        {
            var headers = grid.Columns.Cast<DataGridViewColumn>().Select(c => c.HeaderText).ToArray();
            var rows = grid.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow)
                .Select(r => r.Cells.Cast<DataGridViewCell>().Select(c => Convert.ToString(c.Value) ?? "").ToArray()).ToList();
            return new SheetData(headers, rows);
        }

        void Save(bool choose)
        {
            if (choose || currentPath is null)
            {
                using var dialog = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*", DefaultExt = "csv", FileName = currentPath is null ? "Untitled.csv" : Path.GetFileName(currentPath) };
                if (dialog.ShowDialog(form) != DialogResult.OK) return;
                currentPath = dialog.FileName;
            }
            File.WriteAllText(currentPath, CsvCodec.Write(ReadSheet()));
            form.Text = "zSheets — " + Path.GetFileName(currentPath);
        }

        open.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*" };
            if (dialog.ShowDialog(form) != DialogResult.OK) return;
            try { currentPath = dialog.FileName; LoadSheet(CsvCodec.Parse(File.ReadAllText(currentPath))); form.Text = "zSheets — " + Path.GetFileName(currentPath); }
            catch (Exception ex) { MessageBox.Show(form, ex.Message, "Open failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        save.Click += (_, _) => Save(false);
        saveAs.Click += (_, _) => Save(true);
        addRow.Click += (_, _) => grid.Rows.Add();
        LoadSheet(new SheetData(["A", "B", "C", "D"], []));
        Application.Run(form);
        return 0;
    }

    private static int SelfTest()
    {
        const string input = "Name,Note\r\nAlpha,One\r\n\"Beta, B\",\"line 1\r\nline \"\"2\"\"\"\r\n";
        var parsed = CsvCodec.Parse(input); var roundTrip = CsvCodec.Parse(CsvCodec.Write(parsed));
        return roundTrip.Headers.SequenceEqual(parsed.Headers) && roundTrip.Rows.Count == 2
            && roundTrip.Rows[1][0] == "Beta, B" && roundTrip.Rows[1][1] == "line 1\r\nline \"2\"" ? 0 : 1;
    }
}
