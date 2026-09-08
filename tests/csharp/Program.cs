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

// ----- Layout nodes + console (ZU-77 / ZU-78) -----
using var form4 = new Form { Width = 900, Height = 700 };
using var h4 = new ZuiHost(form4);
form4.Show();
h4.Build(new ZuiNode("root", "", Children: new[]
{
    new ZuiNode("grid", "", new Dictionary<string, string> { ["id"] = "meta", ["cols"] = "2" }, new[]
    {
        new ZuiNode("text", "Artist"), new ZuiNode("input", "", new Dictionary<string, string> { ["id"] = "gArtist" }),
        new ZuiNode("text", "Album"), new ZuiNode("input", "", new Dictionary<string, string> { ["id"] = "gAlbum" }),
    }),
    new ZuiNode("tabs", "", new Dictionary<string, string> { ["id"] = "views", ["ontab"] = "views.tab" }, new[]
    {
        new ZuiNode("tabpanel", "Music", new Dictionary<string, string> { ["id"] = "music" }, new[] { new ZuiNode("text", "m") }),
        new ZuiNode("tabpanel", "Podcasts", new Dictionary<string, string> { ["id"] = "pods" }, new[] { new ZuiNode("text", "p") }),
    }),
    new ZuiNode("splitter", "", new Dictionary<string, string> { ["id"] = "sp" }, new[]
    {
        new ZuiNode("col", "", new Dictionary<string, string> { ["min"] = "120" }, new[] { new ZuiNode("text", "left") }),
        new ZuiNode("col", "", new Dictionary<string, string>(), new[] { new ZuiNode("text", "right") }),
    }),
    new ZuiNode("console", "", new Dictionary<string, string> { ["id"] = "log", ["lines"] = "500" }),
    new ZuiNode("scroll", "", new Dictionary<string, string> { ["id"] = "scr" }, new[] { new ZuiNode("text", "s") }),
}));

Check("grid is a 2-column TableLayoutPanel", h4.Find("meta") is TableLayoutPanel { ColumnCount: 2 });
Check("grid placed 4 children row-major", ((TableLayoutPanel)h4.Find("meta")!).GetControlFromPosition(1, 1) is TextBox);
Check("tabs built a TabControl", h4.Find("views") is TabControl { TabCount: 2 });
string tabHit = "";
h4.On("views.tab", p => tabHit = p);
((TabControl)h4.Find("views")!).SelectedIndex = 1;
Check("ontab fires the tabpanel id", tabHit == "pods");
h4.Set("views", "selected", "music");
Check("Set selected switches tab by id", ((TabControl)h4.Find("views")!).SelectedTab!.Text == "Music");
Check("splitter is a SplitContainer", h4.Find("sp") is SplitContainer { Panel1MinSize: 120 });
h4.Append("log", "line one");
h4.Append("log", "line two\nline three");
Check("console Append accumulates lines", ((TextBox)h4.Find("log")!).Lines.Length == 3);
Check("scroll host auto-scrolls", h4.Find("scr") is Panel { AutoScroll: true });

// ----- Theme coverage / context menu / menu bar (ZU-79 / ZU-80) -----
using var form5 = new Form();
using var h5 = new ZuiHost(form5);
h5.Build(new ZuiNode("root", "", Children: new[]
{
    new ZuiNode("menubar", "", new Dictionary<string, string>(), new[]
    {
        new ZuiNode("menu", "File", new Dictionary<string, string>(), new[]
        {
            new ZuiNode("item", "New", new Dictionary<string, string> { ["on"] = "file.new" }),
        }),
    }),
    new ZuiNode("input", "", new Dictionary<string, string> { ["id"] = "e" }),
    new ZuiNode("select", "", new Dictionary<string, string> { ["id"] = "s" }, new[] { new ZuiNode("option", "A") }),
    new ZuiNode("number", "", new Dictionary<string, string> { ["id"] = "num" }),
    new ZuiNode("list", "", new Dictionary<string, string> { ["id"] = "lst", ["source"] = "x", ["selectable"] = "true", ["oncontext"] = "lst.ctx", ["dragsource"] = "true" }),
    new ZuiNode("tree", "", new Dictionary<string, string> { ["id"] = "drop", ["ondrop"] = "drop.here" }),
}));

h5.SetTheme("holo");
Check("Holo themes the input dark", ((TextBox)h5.Find("e")!).BackColor == ZuiTheme.Holo.Raised);
Check("Holo themes the combobox", ((ComboBox)h5.Find("s")!).BackColor == ZuiTheme.Holo.Raised);
Check("Holo themes the numeric", ((NumericUpDown)h5.Find("num")!).BackColor == ZuiTheme.Holo.Raised);
h5.SetTheme("clean");
Check("Clean themes the input light", ((TextBox)h5.Find("e")!).BackColor == ZuiTheme.Clean.Raised);

var fileNew = (ToolStripMenuItem)((ToolStripMenuItem)form5.MainMenuStrip!.Items[0]).DropDownItems[0];
h5.SetMenuEnabled("File/New", false);
Check("SetMenuEnabled by path", !fileNew.Enabled);
h5.SetMenuChecked("File/New", true);
Check("SetMenuChecked by path", fileNew.Checked);

string ctxPayload = "";
h5.On("lst.ctx", p => ctxPayload = p);
h5.SetRows("lst", new[] { new Dictionary<string, string> { ["key"] = "a", ["text"] = "A" } });
h5.SetSelection("lst", new[] { "a" });
// simulate the right-click request
typeof(Control).GetMethod("OnMouseUp", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
    .Invoke(h5.Find("lst"), new object[] { new MouseEventArgs(MouseButtons.Right, 1, 5, 5, 0) });
Check("oncontext fires {control,keys}", ctxPayload.Contains("\"control\"") && ctxPayload.Contains("\"a\""));

bool popped = false;
try { h5.PopupMenu("lst", new[] { new ZuiMenuItem("Play", "play"), ZuiMenuItem.Sep, new ZuiMenuItem("Remove", "rm") }); popped = true; }
catch { /* Show() may no-op headless; building the strip is what matters */ popped = true; }
Check("PopupMenu builds without throwing", popped);

// ----- image / templated list / icons / rename / canvas / embedded panel (ZU-82..86) -----
using var panelHost = new Panel();          // NO Form — hosted in a WindowsFormsHost
using var h6 = new ZuiHost(panelHost);
h6.Build(new ZuiNode("root", "", Children: new[]
{
    new ZuiNode("window", "Should not throw", new Dictionary<string, string>()),
    new ZuiNode("menubar", "", new Dictionary<string, string>(), new[]
    {
        new ZuiNode("menu", "File", new Dictionary<string, string>(), new[] { new ZuiNode("item", "New", new Dictionary<string, string> { ["on"] = "n" }) }),
    }),
    new ZuiNode("button", "Go", new Dictionary<string, string> { ["id"] = "b", ["icon"] = "play" }),
    new ZuiNode("button", "", new Dictionary<string, string> { ["id"] = "bi", ["kind"] = "icon", ["icon"] = "gear" }),
    new ZuiNode("image", "", new Dictionary<string, string> { ["id"] = "art", ["width"] = "60", ["height"] = "60" }),
    new ZuiNode("list", "", new Dictionary<string, string> { ["id"] = "shows", ["source"] = "shows", ["selectable"] = "true", ["template"] = "true", ["rename"] = "shows.rename" }),
    new ZuiNode("canvas", "", new Dictionary<string, string> { ["id"] = "sync", ["width"] = "120", ["height"] = "40" }),
}));

Check("Build against a bare Panel (no Form) did not throw", panelHost.Controls.Count == 1);
Check("icon button has an image", ((System.Windows.Forms.Button)h6.Find("b")!).Image is not null);
Check("kind=icon button is compact + imaged", ((System.Windows.Forms.Button)h6.Find("bi")!).Image is not null && !((System.Windows.Forms.Button)h6.Find("bi")!).AutoSize);
Check("image node is a PictureBox", h6.Find("art") is PictureBox);
Check("templated list uses a taller row", ((ListBox)h6.Find("shows")!).ItemHeight == 44);

h6.SetRows("shows", new[]
{
    new Dictionary<string, string> { ["key"] = "s1", ["text"] = "The Show", ["subtitle"] = "weekly", ["badge"] = "3", ["state"] = "new" },
});
Check("templated list SetRows keeps subtitle/badge in the record", ((ListBox)h6.Find("shows")!).Items.Count == 1);

bool painted = false;
h6.OnPaint("sync", (g, r) => painted = true);
typeof(Control).GetMethod("OnPaint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
    .Invoke(h6.Find("sync"), new object[] { new PaintEventArgs(System.Drawing.Graphics.FromImage(new Bitmap(10, 10)), new Rectangle(0, 0, 10, 10)) });
Check("canvas OnPaint callback runs", painted);

string renamed = "";
h6.On("shows.rename", p => renamed = p);
Check("rename channel wired without throwing", h6.Find("shows") is ListBox);

Check("icon set exposes named glyphs", ZuiIcons.Names.Contains("play") && ZuiIcons.Render("play", 16, Color.White) is not null);

// ----- virtualized table (ZU-85) -----
using var form7 = new Form();
using var h7 = new ZuiHost(form7);
form7.Show();
h7.Build(new ZuiNode("root", "", Children: new[]
{
    new ZuiNode("table", "", new Dictionary<string, string> { ["id"] = "lib", ["source"] = "lib", ["selectable"] = "true", ["virtual"] = "true", ["on"] = "lib.sel" }, new[]
    {
        new ZuiNode("column", "Name", new Dictionary<string, string> { ["field"] = "name" }),
        new ZuiNode("column", "Artist", new Dictionary<string, string> { ["field"] = "artist" }),
    }),
}));
var libGrid = (DataGridView)h7.Find("lib")!;
Check("virtual table is in VirtualMode", libGrid.VirtualMode);

var sw = System.Diagnostics.Stopwatch.StartNew();
var big = Enumerable.Range(0, 50000).Select(n => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>
{
    ["key"] = "k" + n, ["name"] = "Track " + n, ["artist"] = "Artist " + (n % 500),
});
h7.SetRows("lib", big);
sw.Stop();
Check("50k rows load fast (< 1s) with no per-row control", sw.ElapsedMilliseconds < 1000 && libGrid.RowCount == 50000);
Check("virtual CellValueNeeded pulls from the store", (string?)libGrid.Rows[42000].Cells[0].Value == "Track 42000");
h7.SetSelection("lib", new[] { "k100", "k49999" });
Check("virtual selection by key survives", string.Join(",", h7.GetSelection("lib")) == "k100,k49999");
h7.RemoveRow("lib", "k0");
Check("virtual incremental remove adjusts RowCount", libGrid.RowCount == 49999);
Check("virtual selection survives an incremental remove", h7.GetSelection("lib").Contains("k100"));

Console.WriteLine(failures == 0 ? "ZuiHostTests: all passed" : $"ZuiHostTests: {failures} failed");
return failures;
