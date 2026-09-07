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

Console.WriteLine(failures == 0 ? "ZuiHostTests: all passed" : $"ZuiHostTests: {failures} failed");
return failures;
