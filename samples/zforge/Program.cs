using System.Windows.Forms;
using ZUI;
using ZForge.Generated;

namespace ZForge;

/// <summary>
/// zForge — a small but real zUI application: an RPG character-sheet builder.
///
/// The window is authored in <c>CharacterForge.zsl</c> and compiled ahead of
/// time to <c>generated/CharacterForgeUi.g.cs</c>. This file is the host: it
/// calls <see cref="ZuiHost"/> once to construct the control tree, then wires
/// behaviour on top of it. Per core/RUNTIME_CONTRACT.md it never calls
/// <c>Build()</c> a second time — every later change is an in-place mutation
/// (<c>host.State.Set</c>, <c>host.SetText</c>) or a direct tweak of a looked-up
/// native control.
/// </summary>
internal static class Program
{
    private static readonly string[] Attributes = ["str", "dex", "wits", "cha"];

    private static readonly string[] Ancestries = ["Human", "Elf", "Dwarf", "Halfling", "Orc"];
    private static readonly string[] Classes = ["Wanderer", "Knight", "Mage", "Ranger", "Rogue"];

    private static readonly string[] NamePool =
        ["Mirena Vos", "Talic Bramblefoot", "Korga Ironhide", "Selwyn Ashgrove",
         "Dima Solvenn", "Ptolomer Quill", "Rless of Harrow", "Vanya Coldwater"];

    private static readonly Random Rng = new();

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--self-test"))
            return SelfTest();

        ApplicationConfiguration.Initialize();
        using var form = new Form { Text = "zForge", Width = 1180, Height = 860 };
        using var host = new ZuiHost(form);

        new CharacterForgeUi().Build(host);
        Wire(host, form);

        Application.Run(form);
        return 0;
    }

    /// <summary>Attaches all application behaviour to an already-built host.</summary>
    private static void Wire(ZuiHost host, Form form)
    {
        var pack = (DataGridView)host.Find("pack")!;

        // Derived read-outs recomputed whenever a source property changes. Each
        // watch fires only for its own property (contract §3 isolation), so the
        // sidebar stays in sync without a global refresh.
        void RecomputeBudget()
        {
            var spent = Attributes.Sum(a => host.State.GetInt(a) - 10);
            var left = Math.Max(0, 40 - spent);
            host.State.Set("points", Math.Clamp(left, 0, 100).ToString());
            host.SetText("sumPoints", $"Points left: {left}");
        }

        foreach (var attr in Attributes)
            host.State.Watch(attr, _ => RecomputeBudget());

        host.State.Watch("name", v =>
            host.SetText("sumName", string.IsNullOrWhiteSpace(v) ? "Name: —" : $"Name: {v}"));
        host.State.Watch("cls", v =>
            host.SetText("sumClass", $"Class: {Classes[Clamp(v, Classes.Length)]}"));

        // Menu / button channels.
        host.On("view.holo", _ => host.SetTheme("holo"));
        host.On("view.clean", _ => host.SetTheme("clean"));
        host.On("file.exit", _ => form.Close());
        host.On("help.about", _ => Info(form,
            "zForge 1.0\n\nA zUI sample: an RPG character-sheet builder authored in ZSL " +
            "and compiled to native Windows controls."));

        host.On("roll.attrs", _ =>
        {
            foreach (var attr in Attributes)
                host.State.Set(attr, Roll4d6DropLowest().ToString());
            Touch(host);
        });

        host.On("file.new", _ =>
        {
            host.State.Set("name", "");
            host.State.Set("title", "");
            host.State.Set("bio", "");
            host.State.Set("ancestry", "0");
            host.State.Set("cls", "0");
            host.State.Set("leftHanded", "false");
            host.State.Set("veteran", "false");
            foreach (var attr in Attributes) host.State.Set(attr, "10");
            pack.Rows.Clear();
            host.SetText("statusRight", "No unsaved changes");
        });

        host.On("forge.random", _ =>
        {
            host.State.Set("name", NamePool[Rng.Next(NamePool.Length)]);
            host.State.Set("ancestry", Rng.Next(Ancestries.Length).ToString());
            host.State.Set("cls", Rng.Next(Classes.Length).ToString());
            host.State.Set("veteran", Rng.Next(4) == 0 ? "true" : "false");
            foreach (var attr in Attributes)
                host.State.Set(attr, Roll4d6DropLowest().ToString());
            Touch(host);
        });

        host.On("pack.add", _ =>
        {
            var item = host.State.GetString("newItem").Trim();
            if (item.Length == 0) return;
            pack.Rows.Add(item, "Pack", $"{Rng.Next(1, 6)} lb");
            host.State.Set("newItem", "");
            Touch(host);
        });

        host.On("pack.drop", _ =>
        {
            if (pack.CurrentRow is { IsNewRow: false } row) pack.Rows.Remove(row);
            Touch(host);
        });

        host.On("file.save", _ =>
        {
            var path = Path.Combine(Path.GetTempPath(),
                $"zforge-{Slug(host.State.GetString("name"))}.txt");
            File.WriteAllText(path, SheetSummary(host, pack));
            host.SetText("statusRight", $"Saved → {path}");
            Info(form, $"Character saved to:\n{path}");
        });

        // Seed the derived read-outs from the flushed initial state.
        RecomputeBudget();
        host.SetText("sumName", "Name: —");
        host.SetText("sumClass", $"Class: {Classes[0]}");
    }

    private static string SheetSummary(ZuiHost host, DataGridView pack)
    {
        var s = host.State;
        var gear = pack.Rows.Cast<DataGridViewRow>()
            .Where(r => !r.IsNewRow)
            .Select(r => $"  - {r.Cells[0].Value} ({r.Cells[1].Value}, {r.Cells[2].Value})");
        return string.Join(Environment.NewLine,
        [
            $"Name:     {s.GetString("name")}",
            $"Title:    {s.GetString("title")}",
            $"Ancestry: {Ancestries[Clamp(s.GetString("ancestry"), Ancestries.Length)]}",
            $"Class:    {Classes[Clamp(s.GetString("cls"), Classes.Length)]}",
            $"Veteran:  {s.GetBool("veteran")}",
            "",
            $"STR {s.GetInt("str"),2}   DEX {s.GetInt("dex"),2}   " +
            $"INT {s.GetInt("wits"),2}   CHA {s.GetInt("cha"),2}",
            "",
            "Bio:",
            s.GetString("bio"),
            "",
            "Pack:",
            gear.Any() ? string.Join(Environment.NewLine, gear) : "  (empty)",
        ]);
    }

    private static int Roll4d6DropLowest()
    {
        Span<int> d = [Rng.Next(1, 7), Rng.Next(1, 7), Rng.Next(1, 7), Rng.Next(1, 7)];
        d.Sort();
        return d[1] + d[2] + d[3];
    }

    private static void Touch(ZuiHost host) => host.SetText("statusRight", "Unsaved changes");

    private static int Clamp(string value, int count) =>
        int.TryParse(value, out var n) ? Math.Clamp(n, 0, count - 1) : 0;

    private static string Slug(string name)
    {
        var slug = new string(name.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        return slug.Length == 0 ? "unnamed" : slug;
    }

    private static void Info(IWin32Window owner, string text) =>
        MessageBox.Show(owner, text, "zForge", MessageBoxButtons.OK, MessageBoxIcon.Information);

    /// <summary>
    /// Headless check used by build.ps1: construct the sheet, drive it through
    /// the state layer, and assert the bound native controls followed.
    /// </summary>
    private static int SelfTest()
    {
        ApplicationConfiguration.Initialize();
        using var form = new Form();
        using var host = new ZuiHost(form);
        new CharacterForgeUi().Build(host);
        Wire(host, form);

        var checks = new (string name, bool ok)[]
        {
            ("pack is a grid", host.Find("pack") is DataGridView),
            ("skills tree built", host.Find("skills") is TreeView t && t.Nodes.Count == 3),
            ("initial budget shown", host.GetText("sumPoints") == "Points left: 40"),
        };

        host.State.Set("str", "16");
        checks = [.. checks,
            ("two-way state -> slider", ((TrackBar)host.Find("str")!).Value == 16),
            ("watch recomputed budget", host.GetText("sumPoints") == "Points left: 34"),
        ];

        host.State.Set("name", "Test Subject");
        checks = [.. checks, ("name watch -> sidebar", host.GetText("sumName") == "Name: Test Subject")];

        host.Send("roll.attrs");
        var rolled = Attributes.All(a => host.State.GetInt(a) is >= 3 and <= 18);
        checks = [.. checks, ("roll.attrs in range", rolled)];

        host.State.Set("newItem", "Torch");
        host.Send("pack.add");
        checks = [.. checks, ("pack.add appended a row", ((DataGridView)host.Find("pack")!).Rows.Count == 1)];

        var failed = checks.Where(c => !c.ok).ToArray();
        foreach (var c in checks) Console.WriteLine($"{(c.ok ? "ok  " : "FAIL")} {c.name}");
        Console.WriteLine(failed.Length == 0 ? "zForge self-test: all passed" : $"zForge self-test: {failed.Length} failed");
        return failed.Length;
    }
}
