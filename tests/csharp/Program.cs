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

Console.WriteLine(failures == 0 ? "ZuiHostTests: all passed" : $"ZuiHostTests: {failures} failed");
return failures;
