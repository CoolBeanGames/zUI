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

    public ZuiHost(Control parent)
    {
        _parent = parent ?? throw new ArgumentNullException(nameof(parent));
        State = new ZuiState(this);
    }

    public ZuiTheme Theme { get; private set; } = ZuiTheme.Holo;

    /// <summary>The in-process native state model (see core/RUNTIME_CONTRACT.md).</summary>
    public ZuiState State { get; }

    /// <summary>
    /// Binds a state property to the natural property of the control registered
    /// under <paramref name="controlName"/>. One-way for display controls; two-way
    /// for editable controls (text, check, slider, select). A state change updates
    /// only the bound controls; it never rebuilds.
    /// </summary>
    public void Bind(string stateName, string controlName)
    {
        State.AddBinding(stateName, controlName);
        if (_exports.TryGetValue(controlName, out var control))
            WireTwoWay(control, stateName);
    }

    private void WireTwoWay(Control control, string stateName)
    {
        switch (control)
        {
            case TextBox tb: tb.TextChanged += (_, _) => State.Set(stateName, tb.Text); break;
            case CheckBox cb: cb.CheckedChanged += (_, _) => State.Set(stateName, cb.Checked ? "true" : "false"); break;
            case TrackBar sl: sl.ValueChanged += (_, _) => State.Set(stateName, sl.Value.ToString()); break;
            case ComboBox combo: combo.SelectedIndexChanged += (_, _) => State.Set(stateName, combo.SelectedIndex.ToString()); break;
        }
    }

    /// <summary>Pushes a bound state value onto its control's natural property.</summary>
    internal void ApplyBoundValue(string controlName, string value)
    {
        if (!_exports.TryGetValue(controlName, out var control)) return;
        switch (control)
        {
            case CheckBox: Set(controlName, "checked", value); break;
            case TrackBar or ProgressBar: Set(controlName, "value", value); break;
            case ComboBox: Set(controlName, int.TryParse(value, out _) ? "selected" : "selectedvalue", value); break;
            case TextBox: Set(controlName, "text", value); break;
            default: Set(controlName, "text", value); break;
        }
    }

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
            var root = VStack(fill: true);
            root.Name = "zui-root";
            root.Dock = DockStyle.Fill;
            root.AutoScroll = true;
            _parent.Controls.Add(root);
            if (_parent.FindForm() is { } form && form.MinimumSize.IsEmpty)
                form.MinimumSize = new Size(960, 680);
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

    // ---- Layout engine --------------------------------------------------------
    //
    // Structural nodes map to TableLayoutPanels: a vertical container is a single
    // 100%-wide column with one row per child; a horizontal band is a single row
    // with one column per child. Children dock to fill their cell, so fields grow
    // with the window instead of keeping a hard-coded pixel width, and rows never
    // run off the edge. One child per container may claim the leftover space
    // (a table, tree, textarea, or a nested fill/workspace).

    private static readonly string[] VerticalKinds =
        ["root", "window", "col", "fill", "panel-body", "tabpanel"];
    private static readonly string[] HorizontalKinds =
        ["row", "statusbar", "contextbar", "nav", "tabs", "workspace"];
    private static readonly string[] LeafKinds =
        ["heading", "section-label", "text", "empty", "item", "menu", "button", "input",
         "textarea", "check", "select", "dropdown", "slider", "progress", "table", "tree",
         "spinner", "loading", "sep", "option", "column"];

    private int Gap => Theme.Gap;

    private Control AddNode(Control parent, ZuiNode node)
    {
        if (node.Kind == "window" && parent.FindForm() is Form wf && node.Text.Length > 0) wf.Text = node.Text;

        if (node.Kind == "menubar")
        {
            var strip = BuildMenuStrip(node);
            Place(parent, strip, node);
            if (parent.FindForm() is { } mf) mf.MainMenuStrip = strip;
            return strip;
        }

        Control control = node.Kind switch
        {
            "panel" => MakePanel(node),
            "sidebar" => MakeSidebar(),
            _ when VerticalKinds.Contains(node.Kind) => VStack(fill: true),
            _ when HorizontalKinds.Contains(node.Kind) => HStack(),
            "heading" or "section-label" or "text" or "empty" or "item" or "menu" => CreateLabel(node),
            "button" => new Button
            {
                Text = node.Text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlatStyle = FlatStyle.Flat, MinimumSize = new Size(96, 30),
                Padding = new Padding(12, 5, 12, 5),
            },
            "input" => new TextBox
            {
                PlaceholderText = node.Attrs.GetValueOrDefault("placeholder", ""),
                MinimumSize = new Size(160, 0),
            },
            "textarea" => new TextBox
            {
                Multiline = true, ScrollBars = ScrollBars.Vertical, WordWrap = true,
                MinimumSize = new Size(240, 96),
                PlaceholderText = node.Attrs.GetValueOrDefault("placeholder", ""),
            },
            "check" => new CheckBox { Text = node.Text, AutoSize = true },
            "select" or "dropdown" => Select(node),
            "slider" => Slider(node),
            "progress" => new ProgressBar
            {
                Minimum = 0, Maximum = 100, Value = Math.Clamp(Int(node, "value", 0), 0, 100), Height = 16,
            },
            "table" => Table(node),
            "tree" => Tree(node),
            "spinner" or "loading" => new ProgressBar { Style = ProgressBarStyle.Marquee, Width = 110, Height = 10 },
            "sep" => new Label { AutoSize = false, Height = 1, Margin = new Padding(0, Gap / 2, 0, Gap / 2) },
            _ => VStack(fill: true),
        };

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

        Place(parent, control, node);

        if (!LeafKinds.Contains(node.Kind))
        {
            var content = control is GroupBox box ? box.Controls[0] : control;
            foreach (var child in node.Nodes) AddNode(content, child);
            if (content is TableLayoutPanel { Tag: "v" } stack) TopPack(stack);
        }
        return control;
    }

    /// <summary>Inserts <paramref name="control"/> as the next row (vertical parent)
    /// or column (horizontal parent) of a layout table, sized to fill or to fit.</summary>
    private void Place(Control parent, Control control, ZuiNode node)
    {
        control.Margin = control is TableLayoutPanel or GroupBox
            ? new Padding(Gap / 2)
            : new Padding(Gap / 2, 2, Gap / 2, 2);
        if (parent is not TableLayoutPanel table)
        {
            parent.Controls.Add(control);
            return;
        }

        bool horizontal = table.Tag as string == "h";
        bool grows = Grows(node, horizontal);

        if (horizontal)
        {
            var style = grows ? new ColumnStyle(SizeType.Percent, 100f)
                : node.Kind == "sidebar" ? new ColumnStyle(SizeType.Absolute, Theme.SidebarWidth)
                : new ColumnStyle(SizeType.AutoSize);
            table.ColumnStyles.Add(style);
            table.ColumnCount = table.ColumnStyles.Count;
            bool stretch = grows || control is TableLayoutPanel or GroupBox;
            control.Anchor = stretch ? AnchorStyles.Left | AnchorStyles.Right : AnchorStyles.Left;
            control.Dock = stretch ? DockStyle.Fill : DockStyle.None;
            table.Controls.Add(control, table.ColumnCount - 1, 0);
        }
        else
        {
            table.RowStyles.Add(new RowStyle(
                grows ? SizeType.Percent : SizeType.AutoSize, grows ? 100f : 0f));
            table.RowCount = table.RowStyles.Count;
            control.Dock = DockStyle.Fill;
            table.Controls.Add(control, 0, table.RowCount - 1);
        }
    }

    private static bool Grows(ZuiNode node, bool horizontal) => horizontal
        ? node.Kind is "input" or "textarea" or "select" or "dropdown" or "slider" or "progress"
            or "table" or "tree" or "fill" or "row" or "col" or "workspace"
        : node.Kind is "table" or "tree" or "textarea" or "fill" or "workspace" or "tabpanel" or "window";

    /// <summary>Keeps a vertical stack's children pinned to the top: if nothing in
    /// it already claims the leftover height, a flexible spacer row absorbs it.</summary>
    private static void TopPack(TableLayoutPanel v)
    {
        foreach (RowStyle style in v.RowStyles)
            if (style.SizeType == SizeType.Percent) return;
        v.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        v.RowCount = v.RowStyles.Count;
        v.Controls.Add(new Label { Margin = new Padding(0), AutoSize = false }, 0, v.RowCount - 1);
    }

    private TableLayoutPanel VStack(bool fill)
    {
        var t = new TableLayoutPanel
        {
            ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = fill ? DockStyle.Fill : DockStyle.Top, Padding = new Padding(Gap / 2),
            GrowStyle = TableLayoutPanelGrowStyle.AddRows, Tag = "v",
        };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        return t;
    }

    private TableLayoutPanel HStack()
    {
        var t = new TableLayoutPanel
        {
            RowCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top, Margin = new Padding(0),
            GrowStyle = TableLayoutPanelGrowStyle.AddColumns, Tag = "h",
        };
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        return t;
    }

    private GroupBox MakePanel(ZuiNode node)
    {
        var box = new GroupBox
        {
            Text = node.Text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(Gap, Gap + 8, Gap, Gap), Margin = new Padding(Gap / 2, Gap / 2, Gap / 2, Gap),
        };
        var inner = VStack(fill: true);
        box.Controls.Add(inner);
        return box;
    }

    private TableLayoutPanel MakeSidebar()
    {
        var s = VStack(fill: true);
        s.Padding = new Padding(Gap);
        s.MinimumSize = new Size(Theme.SidebarWidth, 0);
        s.Width = Theme.SidebarWidth;
        return s;
    }

    private static Label CreateLabel(ZuiNode node) => new()
    {
        Text = node.Text, AutoSize = true, Padding = new Padding(0, 3, 0, 3),
        Font = new Font("Segoe UI", node.Kind is "heading" ? 11F : 9F,
            node.Kind is "heading" or "section-label" ? FontStyle.Bold : FontStyle.Regular),
    };

    private static ComboBox Select(ZuiNode node)
    {
        var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, MinimumSize = new Size(140, 0) };
        box.Items.AddRange(node.Nodes.Where(n => n.Kind is "option" or "item").Select(n => (object)n.Text).ToArray());
        if (box.Items.Count > 0) box.SelectedIndex = 0;
        return box;
    }

    private static TrackBar Slider(ZuiNode node)
    {
        var min = Int(node, "min", 0); var max = Int(node, "max", 100);
        return new TrackBar
        {
            Minimum = min, Maximum = max, Value = Math.Clamp(Int(node, "value", min), min, max),
            TickStyle = TickStyle.None, AutoSize = false, Height = 34, MinimumSize = new Size(160, 0),
        };
    }

    private static DataGridView Table(ZuiNode node)
    {
        var grid = new DataGridView
        {
            MinimumSize = new Size(0, 160), AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            AllowUserToAddRows = false, RowHeadersVisible = false, BorderStyle = BorderStyle.FixedSingle,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
        };
        foreach (var col in node.Nodes.Where(n => n.Kind == "column"))
            grid.Columns.Add(col.Attrs.GetValueOrDefault("field", col.Text), col.Text);
        return grid;
    }

    private static TreeView Tree(ZuiNode node)
    {
        var tree = new TreeView { MinimumSize = new Size(0, 140), BorderStyle = BorderStyle.FixedSingle, ShowLines = true };
        foreach (var child in node.Nodes) tree.Nodes.Add(TreeItem(child));
        tree.ExpandAll();
        return tree;
    }

    private static TreeNode TreeItem(ZuiNode node)
    {
        var item = new TreeNode(node.Text);
        foreach (var child in node.Nodes) item.Nodes.Add(TreeItem(child));
        return item;
    }

    private MenuStrip BuildMenuStrip(ZuiNode node)
    {
        var strip = new MenuStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
        foreach (var menu in node.Nodes) strip.Items.Add(BuildMenuItem(menu));
        return strip;
    }

    private ToolStripItem BuildMenuItem(ZuiNode node)
    {
        if (node.Kind == "sep") return new ToolStripSeparator();
        var item = new ToolStripMenuItem(node.Text);
        if (node.Attrs.TryGetValue("shortcut", out var text) && TryParseShortcut(text, out var keys))
        {
            item.ShortcutKeys = keys;
            item.ShowShortcutKeys = true;
        }
        if (node.Attrs.TryGetValue("on", out var channel))
            item.Click += (_, _) => Dispatch(channel, "");
        foreach (var child in node.Nodes) item.DropDownItems.Add(BuildMenuItem(child));
        return item;
    }

    private static bool TryParseShortcut(string text, out Keys keys)
    {
        keys = Keys.None;
        foreach (var part in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            keys |= part.ToLowerInvariant() switch
            {
                "ctrl" or "control" => Keys.Control,
                "alt" => Keys.Alt,
                "shift" => Keys.Shift,
                _ when Enum.TryParse<Keys>(part, true, out var k) => k,
                _ => Keys.None,
            };
        }
        return keys != Keys.None && (keys & Keys.KeyCode) != Keys.None;
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
        if (root is MenuStrip menu)
        {
            menu.BackColor = Theme.Raised; menu.ForeColor = Theme.Text;
            menu.RenderMode = ToolStripRenderMode.System;
            foreach (ToolStripItem item in menu.Items) ThemeMenuItem(item);
        }
        if (root is DataGridView grid)
        {
            grid.BackgroundColor = Theme.Surface; grid.DefaultCellStyle.BackColor = Theme.Surface;
            grid.DefaultCellStyle.ForeColor = Theme.Text; grid.DefaultCellStyle.SelectionBackColor = Theme.Accent;
            grid.DefaultCellStyle.SelectionForeColor = Theme.Window; grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Raised;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Text; grid.EnableHeadersVisualStyles = false;
        }
        foreach (Control child in root.Controls) ApplyTheme(child);
    }

    private void ThemeMenuItem(ToolStripItem item)
    {
        item.BackColor = Theme.Raised;
        item.ForeColor = Theme.Text;
        if (item is ToolStripMenuItem menuItem)
            foreach (ToolStripItem sub in menuItem.DropDownItems) ThemeMenuItem(sub);
    }

    private static int Int(ZuiNode node, string key, int fallback) => node.Attrs.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) ? parsed : fallback;

    public void Dispose() { if (_disposed) return; _disposed = true; _handlers.Clear(); _exports.Clear(); }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() { _dispose?.Invoke(); _dispose = null; }
    }
}

/// <summary>
/// zUI's in-process declarative state model. Values are stored as strings (the
/// same wire form the compiler emits and the event channel carries). A change to
/// one property notifies only the controls and watchers that depend on it — there
/// is no global render pass. See core/RUNTIME_CONTRACT.md.
/// </summary>
public sealed class ZuiState
{
    private readonly ZuiHost _host;
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _bindings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Action<string>>> _watchers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _propagating = new(StringComparer.Ordinal);

    internal ZuiState(ZuiHost host) => _host = host;

    public IReadOnlyCollection<string> Names => _values.Keys;

    /// <summary>Registers an initial value. Does not notify.</summary>
    public void Init(string name, string value) => _values[name] = value ?? "";

    public string GetString(string name) => _values.GetValueOrDefault(name, "");
    public int GetInt(string name) => int.TryParse(GetString(name), out var n) ? n : 0;
    public bool GetBool(string name) => GetString(name) is "true" or "1" or "on";

    /// <summary>Sets a value and notifies dependents when it actually changed.</summary>
    public void Set(string name, string value)
    {
        value ??= "";
        if (_values.TryGetValue(name, out var current) && current == value) return;
        _values[name] = value;
        Propagate(name);
    }

    /// <summary>Applies a named mutation (<c>plus1</c>, <c>minus1</c>, <c>toggle</c>).</summary>
    public void Mutate(string name, string op)
    {
        switch (op)
        {
            case "plus1": Set(name, (GetInt(name) + 1).ToString()); break;
            case "minus1": Set(name, (GetInt(name) - 1).ToString()); break;
            case "toggle" or "not": Set(name, GetBool(name) ? "false" : "true"); break;
            default: Set(name, GetString(op)); break; // treat as "copy from other var"
        }
    }

    /// <summary>Copies another property's value into this one.</summary>
    public void Assign(string name, string fromName) => Set(name, GetString(fromName));

    /// <summary>Subscribes to changes of one property.</summary>
    public IDisposable Watch(string name, Action<string> handler)
    {
        if (!_watchers.TryGetValue(name, out var list)) _watchers[name] = list = new();
        list.Add(handler);
        return new Unsub(() => list.Remove(handler));
    }

    /// <summary>Pushes every current value to its bound controls and watchers.</summary>
    public void Flush()
    {
        foreach (var name in _values.Keys.ToArray()) Propagate(name);
    }

    internal void AddBinding(string stateName, string controlName)
    {
        if (!_bindings.TryGetValue(stateName, out var list)) _bindings[stateName] = list = new();
        if (!list.Contains(controlName)) list.Add(controlName);
    }

    private void Propagate(string name)
    {
        if (!_propagating.Add(name)) return; // reentrancy guard for two-way bindings
        try
        {
            var value = _values.GetValueOrDefault(name, "");
            if (_bindings.TryGetValue(name, out var controls))
                foreach (var control in controls) _host.ApplyBoundValue(control, value);
            if (_watchers.TryGetValue(name, out var handlers))
                foreach (var handler in handlers.ToArray()) handler(value);
        }
        finally { _propagating.Remove(name); }
    }

    private sealed class Unsub(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() { _dispose?.Invoke(); _dispose = null; }
    }
}
