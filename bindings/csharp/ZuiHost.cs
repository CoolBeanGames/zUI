using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ZUI;

public sealed record ZuiNode(string Kind, string Text = "",
    IReadOnlyDictionary<string, string>? Attributes = null,
    IReadOnlyList<ZuiNode>? Children = null)
{
    public IReadOnlyDictionary<string, string> Attrs { get; } = Attributes ?? new Dictionary<string, string>();
    public IReadOnlyList<ZuiNode> Nodes { get; } = Children ?? Array.Empty<ZuiNode>();
}

public sealed record ZuiTheme(Color Window, Color Surface, Color Raised, Color Text,
    Color Muted, Color Accent, Color Border, int Gap = 8, int SidebarWidth = 210)
{
    public static ZuiTheme Holo { get; } = new(Color.FromArgb(0x10, 0x13, 0x16),
        Color.FromArgb(0x17, 0x1b, 0x20), Color.FromArgb(0x20, 0x25, 0x2b),
        Color.FromArgb(0xee, 0xf4, 0xf7), Color.FromArgb(0x99, 0xaa, 0xb3),
        Color.FromArgb(0x33, 0xb5, 0xe5), Color.FromArgb(0x35, 0x3d, 0x45));
    public static ZuiTheme Clean { get; } = new(Color.FromArgb(0xf1, 0xf3, 0xf5), Color.White,
        Color.FromArgb(0xf8, 0xf9, 0xfa), Color.FromArgb(0x20, 0x24, 0x28),
        Color.FromArgb(0x64, 0x6b, 0x73), Color.FromArgb(0x00, 0x78, 0xd4), Color.FromArgb(0xd3, 0xd7, 0xdb));
}

/// <summary>Builds compiled nodes as operating-system WinForms controls. There is no browser engine.</summary>
public sealed class ZuiHost : IDisposable
{
    private readonly Control _parent;
    private readonly Dictionary<string, List<Action<string>>> _handlers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Control> _exports = new(StringComparer.Ordinal);
    private bool _disposed;
    private int _buildCount;

    public ZuiHost(Control parent) => _parent = parent ?? throw new ArgumentNullException(nameof(parent));
    public ZuiTheme Theme { get; private set; } = ZuiTheme.Holo;

    /// <summary>
    /// Constructs the native control tree from compiler-emitted nodes. This is a
    /// construction operation only: call it once per screen. It is NOT a render,
    /// refresh, or update pass. To change text, values, visibility, selection, or
    /// collection contents after construction, mutate the existing controls via
    /// <see cref="Find(string)"/> / the incremental mutation API. See
    /// core/RUNTIME_CONTRACT.md.
    /// </summary>
    public Control Build(ZuiNode tree)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (++_buildCount > 1)
        {
            const string message =
                "ZuiHost.Build() called more than once on the same host. Build() is " +
                "construction-only and destroys existing controls, selection, and focus. " +
                "Use Find()/SetText()/SetValue()/collection updates to mutate the existing " +
                "tree instead. See core/RUNTIME_CONTRACT.md.";
            System.Diagnostics.Trace.TraceWarning(message);
            System.Diagnostics.Debug.WriteLine("[zUI] " + message);
        }
        _parent.SuspendLayout();
        try
        {
            _parent.Controls.Clear();
            _exports.Clear();
            var root = Container(true);
            root.Name = "zui-root";
            root.Dock = DockStyle.Fill;
            root.AutoScroll = true;
            _parent.Controls.Add(root);
            foreach (var child in tree.Nodes) AddNode(root, child);
            ApplyTheme(root);
            return root;
        }
        finally { _parent.ResumeLayout(true); }
    }

    public IDisposable On(string channel, Action<string> handler)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_handlers.TryGetValue(channel, out var list)) _handlers[channel] = list = new();
        list.Add(handler);
        return new Subscription(() => list.Remove(handler));
    }

    public void Send(string channel, string payload = "") => Dispatch(channel, payload);

    public void SetTheme(string name)
    {
        Theme = string.Equals(name, "clean", StringComparison.OrdinalIgnoreCase) ? ZuiTheme.Clean : ZuiTheme.Holo;
        if (_parent.Controls.Count > 0) ApplyTheme(_parent.Controls[0]);
        Dispatch("theme-changed", name);
    }

    public Control? Find(string export) => _exports.GetValueOrDefault(export);

    // ----- Incremental native control mutation (see core/RUNTIME_CONTRACT.md) -----
    // These mutate the EXISTING native control registered under an id/bind/export
    // name. They never reconstruct a control and never call Build().

    private Control Require(string name) => _exports.GetValueOrDefault(name)
        ?? throw new KeyNotFoundException($"zUI: no control registered as '{name}'. " +
            "Names come from id/export/bind on the node; lookup order is export > bind > id.");

    /// <summary>Generic property write against an existing native control.</summary>
    public bool Set(string name, string property, object? value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var c = Require(name);
        switch (property.ToLowerInvariant())
        {
            case "text": c.Text = Str(value); return true;
            case "visible": c.Visible = Bool(value); return true;
            case "enabled": c.Enabled = Bool(value); return true;
            case "focus": if (Bool(value)) c.Focus(); return true;
            case "width": c.Width = IntOf(value); return true;
            case "height": c.Height = IntOf(value); return true;
            case "fg": case "foreground": c.ForeColor = ColorOf(value); return true;
            case "bg": case "background": c.BackColor = ColorOf(value); return true;
            case "checked":
                if (c is CheckBox cb) { cb.Checked = Bool(value); return true; }
                if (c is RadioButton rb) { rb.Checked = Bool(value); return true; }
                return false;
            case "value":
                switch (c)
                {
                    case TrackBar tb: tb.Value = Math.Clamp(IntOf(value), tb.Minimum, tb.Maximum); return true;
                    case ProgressBar pb: pb.Value = Math.Clamp(IntOf(value), pb.Minimum, pb.Maximum); return true;
                    case NumericUpDown nud: nud.Value = IntOf(value); return true;
                    case TextBox txt: txt.Text = Str(value); return true;
                    default: return false;
                }
            case "selected": case "selectedindex":
                switch (c)
                {
                    case ComboBox combo: combo.SelectedIndex = IntOf(value); return true;
                    case ListBox lb: lb.SelectedIndex = IntOf(value); return true;
                    case DataGridView grid:
                        grid.ClearSelection();
                        var i = IntOf(value);
                        if (i >= 0 && i < grid.Rows.Count) { grid.Rows[i].Selected = true; grid.CurrentCell = grid.Rows[i].Cells[0]; }
                        return true;
                    default: return false;
                }
            case "selectedvalue": case "selectedtext":
                if (c is ComboBox cbv)
                {
                    var idx = cbv.Items.IndexOf(Str(value));
                    if (idx >= 0) { cbv.SelectedIndex = idx; return true; }
                }
                return false;
            default: return false;
        }
    }

    /// <summary>Generic property read from an existing native control.</summary>
    public object? Get(string name, string property)
    {
        var c = Require(name);
        return property.ToLowerInvariant() switch
        {
            "text" => c.Text,
            "visible" => c.Visible,
            "enabled" => c.Enabled,
            "width" => c.Width,
            "height" => c.Height,
            "checked" => c is CheckBox cb ? cb.Checked : c is RadioButton rb ? rb.Checked : null,
            "value" => c switch { TrackBar tb => tb.Value, ProgressBar pb => pb.Value, NumericUpDown n => (int)n.Value, TextBox t => t.Text, _ => null },
            "selected" or "selectedindex" => c switch { ComboBox cx => cx.SelectedIndex, ListBox l => l.SelectedIndex, DataGridView g => g.CurrentRow?.Index ?? -1, _ => null },
            "selectedvalue" or "selectedtext" => c is ComboBox cv ? cv.SelectedItem?.ToString() : null,
            _ => null,
        };
    }

    public void SetText(string name, string text) => Set(name, "text", text);
    public void SetVisible(string name, bool visible) => Set(name, "visible", visible);
    public void SetEnabled(string name, bool enabled) => Set(name, "enabled", enabled);
    public void SetChecked(string name, bool value) => Set(name, "checked", value);
    public void SetValue(string name, int value) => Set(name, "value", value);
    public void SetSelected(string name, int index) => Set(name, "selected", index);
    public void SetSelectedValue(string name, string value) => Set(name, "selectedvalue", value);
    public void SetForeground(string name, Color color) => Set(name, "fg", color);
    public void SetBackground(string name, Color color) => Set(name, "bg", color);
    public void SetSize(string name, int width, int height) { Set(name, "width", width); Set(name, "height", height); }
    public void Focus(string name) => Set(name, "focus", true);

    public string GetText(string name) => (string)(Get(name, "text") ?? "");
    public bool GetChecked(string name) => (bool)(Get(name, "checked") ?? false);
    public int GetValue(string name) => (int)(Get(name, "value") ?? 0);
    public int GetSelected(string name) => (int)(Get(name, "selected") ?? -1);

    private static string Str(object? v) => v?.ToString() ?? "";
    private static bool Bool(object? v) => v switch { bool b => b, string s => s is "true" or "1" or "on", null => false, _ => Convert.ToBoolean(v) };
    private static int IntOf(object? v) => v switch { int i => i, null => 0, string s => int.TryParse(s, out var p) ? p : 0, _ => Convert.ToInt32(v) };
    private static Color ColorOf(object? v) => v switch
    {
        Color c => c,
        string s when s.StartsWith('#') => ColorTranslator.FromHtml(s),
        string s => Color.FromName(s),
        _ => Color.Empty,
    };

    private void Dispatch(string channel, string payload)
    {
        if (!_handlers.TryGetValue(channel, out var list)) return;
        foreach (var handler in list.ToArray()) handler(payload);
    }

    private Control AddNode(Control parent, ZuiNode node)
    {
        if (node.Kind == "window" && parent.FindForm() is Form form && node.Text.Length > 0) form.Text = node.Text;
        Control control = node.Kind switch
        {
            "root" or "window" or "col" or "fill" or "workspace" or "panel-body" => Container(true),
            "row" or "statusbar" or "contextbar" or "nav" or "tabs" or "menubar" => Container(false),
            "panel" => new GroupBox { Text = node.Text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(Theme.Gap) },
            "sidebar" => new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Width = Theme.SidebarWidth, Dock = DockStyle.Left },
            "heading" or "section-label" or "text" or "empty" or "item" or "menu" => CreateLabel(node),
            "button" => new Button { Text = node.Text, AutoSize = true, FlatStyle = FlatStyle.Flat },
            "input" => new TextBox { PlaceholderText = node.Attrs.GetValueOrDefault("placeholder", ""), Width = 220 },
            "textarea" => new TextBox { Multiline = true, Width = 320, Height = 100, ScrollBars = ScrollBars.Vertical },
            "check" => new CheckBox { Text = node.Text, AutoSize = true },
            "select" or "dropdown" => Select(node),
            "slider" => Slider(node),
            "progress" => Progress(node),
            "table" => Table(node),
            "tree" => Tree(node),
            "spinner" or "loading" => new ProgressBar { Style = ProgressBarStyle.Marquee, Width = 90, Height = 8 },
            "sep" => new Label { AutoSize = false, Height = 1, Width = 120 },
            _ => Container(true)
        };
        control.Margin = new Padding(Theme.Gap / 2);
        if (node.Attrs.TryGetValue("id", out var id)) control.Name = id;
        // Register every lookup name. Resolution priority is export > bind > id:
        // a more specific name wins, but all three resolve to the control.
        foreach (var key in new[]
        {
            node.Attrs.GetValueOrDefault("id", ""),
            node.Attrs.GetValueOrDefault("bind", ""),
            node.Attrs.GetValueOrDefault("export", ""),
        })
            if (key.Length > 0) _exports[key] = control;
        if (node.Attrs.ContainsKey("disabled")) control.Enabled = false;
        if (node.Attrs.TryGetValue("on", out var channel)) WireEvent(control, channel);
        parent.Controls.Add(control);
        if (control is not (DataGridView or TreeView or ComboBox or TrackBar or ProgressBar or TextBox or Button or CheckBox or Label))
            foreach (var child in node.Nodes) AddNode(control, child);
        return control;
    }

    private FlowLayoutPanel Container(bool vertical) => new()
    {
        AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        FlowDirection = vertical ? FlowDirection.TopDown : FlowDirection.LeftToRight,
        WrapContents = false, Dock = DockStyle.Top, Padding = new Padding(Theme.Gap / 2)
    };

    private static Label CreateLabel(ZuiNode node) => new()
    {
        Text = node.Text, AutoSize = true,
        Font = new Font("Segoe UI", 9F, node.Kind is "heading" or "section-label" ? FontStyle.Bold : FontStyle.Regular)
    };

    private static ComboBox Select(ZuiNode node)
    {
        var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        box.Items.AddRange(node.Nodes.Where(n => n.Kind is "option" or "item").Select(n => (object)n.Text).ToArray());
        if (box.Items.Count > 0) box.SelectedIndex = 0;
        return box;
    }

    private static TrackBar Slider(ZuiNode node)
    {
        var min = Int(node, "min", 0); var max = Int(node, "max", 100);
        return new TrackBar { Minimum = min, Maximum = max, Value = Math.Clamp(Int(node, "value", min), min, max), TickStyle = TickStyle.None, Width = 220 };
    }

    private static ProgressBar Progress(ZuiNode node) => new() { Minimum = 0, Maximum = 100, Value = Math.Clamp(Int(node, "value", 0), 0, 100), Width = 220, Height = 12 };

    private static DataGridView Table(ZuiNode node)
    {
        var grid = new DataGridView { Width = 720, Height = 320, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            AllowUserToAddRows = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
        foreach (var col in node.Nodes.Where(n => n.Kind == "column")) grid.Columns.Add(col.Attrs.GetValueOrDefault("field", col.Text), col.Text);
        return grid;
    }

    private static TreeView Tree(ZuiNode node)
    {
        var tree = new TreeView { Width = 240, Height = 320, BorderStyle = BorderStyle.FixedSingle };
        foreach (var child in node.Nodes) tree.Nodes.Add(TreeItem(child));
        return tree;
    }

    private static TreeNode TreeItem(ZuiNode node)
    {
        var item = new TreeNode(node.Text);
        foreach (var child in node.Nodes) item.Nodes.Add(TreeItem(child));
        return item;
    }

    private void WireEvent(Control control, string channel)
    {
        if (control is ComboBox combo) combo.SelectedValueChanged += (_, _) => Dispatch(channel, combo.Text);
        else if (control is TrackBar slider) slider.ValueChanged += (_, _) => Dispatch(channel, slider.Value.ToString());
        else if (control is CheckBox check) check.CheckedChanged += (_, _) => Dispatch(channel, check.Checked.ToString().ToLowerInvariant());
        else control.Click += (_, _) => Dispatch(channel, "");
    }

    private void ApplyTheme(Control root)
    {
        root.BackColor = root is TextBox or DataGridView ? Theme.Raised : Theme.Surface;
        root.ForeColor = Theme.Text;
        if (root is Button button) { button.FlatAppearance.BorderColor = Theme.Border; button.BackColor = Theme.Raised; }
        if (root is GroupBox) root.ForeColor = Theme.Accent;
        if (root is DataGridView grid)
        {
            grid.BackgroundColor = Theme.Surface; grid.DefaultCellStyle.BackColor = Theme.Surface;
            grid.DefaultCellStyle.ForeColor = Theme.Text; grid.DefaultCellStyle.SelectionBackColor = Theme.Accent;
            grid.DefaultCellStyle.SelectionForeColor = Theme.Window; grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Raised;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Text; grid.EnableHeadersVisualStyles = false;
        }
        foreach (Control child in root.Controls) ApplyTheme(child);
    }

    private static int Int(ZuiNode node, string key, int fallback) => node.Attrs.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) ? parsed : fallback;

    public void Dispose() { if (_disposed) return; _disposed = true; _handlers.Clear(); _exports.Clear(); }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() { _dispose?.Invoke(); _dispose = null; }
    }
}
