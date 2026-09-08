// Native host tests for the incremental mutation contract (core/RUNTIME_CONTRACT.md).
// Proves: a screen is built once; many property mutations follow; the WinForms
// control instances are preserved; no rebuild happens during mutation.
using System.Drawing;
using System.Windows.Forms;
using ZUI;

int failures = 0;
void Check(string name, bool ok)
{
    Console.WriteLine((ok ? "ok   " : "FAIL ") + name);
    if (!ok) failures++;
}

using var form = new Form();
using var host = new ZuiHost(form);

// One construction call.
var tree = new ZuiNode("root", "", Children: new[]
{
    new ZuiNode("heading", "Now playing", new Dictionary<string, string> { ["id"] = "trackTitle" }),
    new ZuiNode("slider", "", new Dictionary<string, string> { ["bind"] = "progress", ["min"] = "0", ["max"] = "100" }),
    new ZuiNode("check", "Shuffle", new Dictionary<string, string> { ["export"] = "shuffle" }),
    new ZuiNode("text", "Loading", new Dictionary<string, string> { ["id"] = "loading" }),
    new ZuiNode("input", "", new Dictionary<string, string> { ["id"] = "search" }),
    new ZuiNode("select", "", new Dictionary<string, string> { ["id"] = "rate" }, new[]
    {
        new ZuiNode("option", "1x"), new ZuiNode("option", "1.5x"), new ZuiNode("option", "2x"),
    }),
});
host.Build(tree);

// Capture the concrete instances created at construction time.
var title = host.Find("trackTitle")!;
var progress = host.Find("progress")!;
var shuffle = host.Find("shuffle")!;
var loading = host.Find("loading")!;
var rate = host.Find("rate")!;

Check("lookup by id / bind / export all resolve",
    title is Label && progress is TrackBar && shuffle is CheckBox && rate is ComboBox);

// Many mutations, no Build() call.
host.SetText("trackTitle", "Paranoid Android");
host.SetValue("progress", 42);
host.SetChecked("shuffle", true);
host.SetVisible("loading", false);
host.SetEnabled("search", false);
host.SetSelected("rate", 2);
host.SetForeground("trackTitle", Color.Red);
host.SetText("trackTitle", "Let Down"); // second change to same control

Check("SetText mutated existing label", ((Label)title).Text == "Let Down");
Check("SetValue mutated existing slider", ((TrackBar)progress).Value == 42);
Check("SetChecked mutated existing checkbox", ((CheckBox)shuffle).Checked);
Check("SetVisible mutated existing control", !loading.Visible);
Check("SetEnabled mutated existing input", !host.Find("search")!.Enabled);
Check("SetSelected mutated existing combobox", ((ComboBox)rate).SelectedIndex == 2);
Check("SetForeground mutated existing control", title.ForeColor == Color.Red);

// The instances must be the SAME objects — proof nothing was recreated.
Check("label instance preserved", ReferenceEquals(title, host.Find("trackTitle")));
Check("slider instance preserved", ReferenceEquals(progress, host.Find("progress")));
Check("checkbox instance preserved", ReferenceEquals(shuffle, host.Find("shuffle")));
Check("combobox instance preserved", ReferenceEquals(rate, host.Find("rate")));

// Generic get round-trips.
Check("Get reads back text", (string)host.Get("trackTitle", "text")! == "Let Down");
Check("Get reads back value", (int)host.Get("progress", "value")! == 42);
Check("GetChecked round-trips", host.GetChecked("shuffle"));

// Container child count is unchanged -> no rebuild happened.
var root = form.Controls[0];
Check("single build produced one root", form.Controls.Count == 1 && root.Name == "zui-root");

// ----- State / bind / handler semantics (ZU-64) -----
using var form2 = new Form();
using var h2 = new ZuiHost(form2);
h2.Build(new ZuiNode("root", "", Children: new[]
{
    new ZuiNode("heading", "", new Dictionary<string, string> { ["id"] = "titleA" }),
    new ZuiNode("text", "", new Dictionary<string, string> { ["id"] = "titleB" }),
    new ZuiNode("slider", "", new Dictionary<string, string> { ["bind"] = "count", ["min"] = "0", ["max"] = "10" }),
    new ZuiNode("input", "", new Dictionary<string, string> { ["bind"] = "name" }),
    new ZuiNode("button", "x", new Dictionary<string, string> { ["id"] = "untouched" }),
}));

h2.State.Init("title", "Hello");
h2.State.Init("count", "3");
h2.State.Init("name", "ada");
h2.Bind("title", "titleA");
h2.Bind("title", "titleB");   // multiple controls, one property
h2.Bind("count", "count");
h2.Bind("name", "name");

int untouchedResizes = 0;
h2.Find("untouched")!.TextChanged += (_, _) => untouchedResizes++;
int titleWatch = 0;
h2.State.Watch("title", _ => titleWatch++);

h2.State.Flush();
Check("initial state applied to bound label A", ((Label)h2.Find("titleA")!).Text == "Hello");
Check("initial state applied to bound label B", h2.Find("titleB")!.Text == "Hello");
Check("initial numeric state applied to slider", ((TrackBar)h2.Find("count")!).Value == 3);
Check("initial text state applied to input", ((TextBox)h2.Find("name")!).Text == "ada");

// One-way: state -> every bound control.
h2.State.Set("title", "World");
Check("one-way update reaches label A", ((Label)h2.Find("titleA")!).Text == "World");
Check("one-way update reaches label B", h2.Find("titleB")!.Text == "World");
Check("watcher fired for changed property", titleWatch >= 1);

// Unrelated control never touched by an unrelated property change.
Check("unrelated control untouched by unrelated state", untouchedResizes == 0);

// Two-way: editing the control updates state.
((TextBox)h2.Find("name")!).Text = "grace";
Check("two-way: input edit writes back to state", h2.State.GetString("name") == "grace");
((TrackBar)h2.Find("count")!).Value = 7;
Check("two-way: slider edit writes back to state", h2.State.GetInt("count") == 7);

// Mutate helper (top-level handler idiom).
h2.State.Mutate("count", "plus1");
Check("Mutate plus1 advances state", h2.State.GetInt("count") == 8);
Check("Mutate plus1 propagated to slider", ((TrackBar)h2.Find("count")!).Value == 8);

// Top-level handler wired through host.On updating state, no rebuild.
h2.On("bump", _ => h2.State.Mutate("count", "minus1"));
h2.Send("bump");
Check("handler channel mutates state", h2.State.GetInt("count") == 7);

// ----- Events, option values, collection binding, selection (ZU-75 / ZU-76) -----
using var form3 = new Form();
using var h3 = new ZuiHost(form3);
form3.Show();   // realize handles so PerformClick / DataGridView selection behave
h3.Build(new ZuiNode("root", "", Children: new[]
{
    new ZuiNode("input", "", new Dictionary<string, string> { ["id"] = "q", ["on"] = "q.change", ["oncommit"] = "q.commit" }),
    new ZuiNode("number", "", new Dictionary<string, string> { ["id"] = "keep", ["min"] = "1", ["max"] = "999", ["value"] = "5", ["on"] = "keep.change" }),
    new ZuiNode("button", "Shuffle", new Dictionary<string, string> { ["id"] = "shuf", ["kind"] = "toggle", ["ontoggle"] = "shuf.toggle" }),
    new ZuiNode("nav", "", new Dictionary<string, string>(), new[]
    {
        new ZuiNode("item", "Music", new Dictionary<string, string> { ["id"] = "navMusic", ["on"] = "nav.music" }),
    }),
    new ZuiNode("select", "", new Dictionary<string, string> { ["id"] = "preset", ["on"] = "preset.change" }, new[]
    {
        new ZuiNode("option", "MP3 — 320 kbps", new Dictionary<string, string> { ["value"] = "mp3-320" }),
        new ZuiNode("option", "FLAC", new Dictionary<string, string> { ["value"] = "flac" }),
    }),
    new ZuiNode("table", "", new Dictionary<string, string> { ["id"] = "tracks", ["source"] = "tracks", ["selectable"] = "true", ["on"] = "tracks.sel", ["onactivate"] = "tracks.play" }, new[]
    {
        new ZuiNode("column", "Name", new Dictionary<string, string> { ["field"] = "name" }),
        new ZuiNode("column", "Plays", new Dictionary<string, string> { ["field"] = "plays" }),
    }),
}));

string? lastChange = null, lastCommit = null, lastSel = null, lastPlay = null, lastPreset = null, lastToggle = null;
h3.On("q.change", p => lastChange = p);
h3.On("q.commit", p => lastCommit = p);
h3.On("preset.change", p => lastPreset = p);
h3.On("shuf.toggle", p => lastToggle = p);
h3.On("tracks.sel", p => lastSel = p);
h3.On("tracks.play", p => lastPlay = p);

bool navHit = false;
h3.On("nav.music", _ => navHit = true);
// A nav item is a Label with on= — Label has no PerformClick, so raise OnClick as WinForms would.
typeof(Control).GetMethod("OnClick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
    .Invoke(h3.Find("navMusic"), new object[] { EventArgs.Empty });
Check("nav item (Label with on=) fires a click channel", navHit);

((TextBox)h3.Find("q")!).Text = "beat";
Check("input on= fires change with text", lastChange == "beat");
Check("input oncommit not fired on plain edit", lastCommit is null);

((System.Windows.Forms.Button)h3.Find("shuf")!).PerformClick();
Check("toggle button reports pressed", lastToggle == "true");
Check("toggle Get pressed", (bool)(h3.Get("shuf", "pressed") ?? false));
h3.Set("shuf", "pressed", false);
Check("toggle Set pressed", !(bool)(h3.Get("shuf", "pressed") ?? true));

((ComboBox)h3.Find("preset")!).SelectedIndex = 1;
Check("select change fires option value not label", lastPreset == "flac");
Check("Get selectedvalue is the option value", (string?)h3.Get("preset", "selectedvalue") == "flac");

h3.SetRows("tracks", new[]
{
    new Dictionary<string, string> { ["key"] = "t1", ["name"] = "Intro", ["plays"] = "3" },
    new Dictionary<string, string> { ["key"] = "t2", ["name"] = "Verse", ["plays"] = "9", ["state"] = "warn" },
    new Dictionary<string, string> { ["key"] = "t3", ["name"] = "Outro", ["plays"] = "1" },
});
var grid = (DataGridView)h3.Find("tracks")!;
Check("SetRows populated the grid", grid.Rows.Count == 3);
Check("row cells filled by field", (string?)grid.Rows[1].Cells[0].Value == "Verse");
Check("warn row is themed", grid.Rows[1].DefaultCellStyle.BackColor != grid.Rows[0].DefaultCellStyle.BackColor);

h3.SetSelection("tracks", new[] { "t2", "t3" });
Check("SetSelection by key selects rows", grid.SelectedRows.Count == 2);
Check("GetSelection returns keys", string.Join(",", h3.GetSelection("tracks")) == "t2,t3");
Check("selection channel fired JSON keys", lastSel is not null && lastSel.Contains("t2") && lastSel.Contains("t3"));

grid.Rows[0].Cells[0].Selected = false; // ensure event baseline
h3.UpdateRow("tracks", "t2", new Dictionary<string, string> { ["key"] = "t2", ["plays"] = "10" });
Check("UpdateRow merges + keeps other fields", (string?)grid.Rows[1].Cells[1].Value == "10" && (string?)grid.Rows[1].Cells[0].Value == "Verse");
Check("UpdateRow preserved selection by key", h3.GetSelection("tracks").Contains("t2"));

h3.RemoveRow("tracks", "t1");
Check("RemoveRow drops the row", grid.Rows.Count == 2 && !h3.GetRowKeys("tracks").Contains("t1"));

grid.CellDoubleClick += (_, __) => { };
if (grid.Rows.Count > 0) { grid.CurrentCell = grid.Rows[0].Cells[0]; }
h3.Send("noop");
Check("still one root after all mutations", form3.Controls.Count == 1);

Console.WriteLine(failures == 0 ? "ZuiHostTests: all passed" : $"ZuiHostTests: {failures} failed");
return failures;
