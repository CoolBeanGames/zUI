using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.Json;
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
    Color Muted, Color Accent, Color Border, Color Warn, Color Error, Color Ok,
    int Gap = 8, int SidebarWidth = 210)
{
    public static ZuiTheme Holo { get; } = new(Color.FromArgb(0x10, 0x13, 0x16),
        Color.FromArgb(0x17, 0x1b, 0x20), Color.FromArgb(0x20, 0x25, 0x2b),
        Color.FromArgb(0xee, 0xf4, 0xf7), Color.FromArgb(0x99, 0xaa, 0xb3),
        Color.FromArgb(0x33, 0xb5, 0xe5), Color.FromArgb(0x35, 0x3d, 0x45),
        Color.FromArgb(0xff, 0xb3, 0x00), Color.FromArgb(0xff, 0x52, 0x52), Color.FromArgb(0x99, 0xcc, 0x00));
    public static ZuiTheme Clean { get; } = new(Color.FromArgb(0xf1, 0xf3, 0xf5), Color.White,
        Color.FromArgb(0xf8, 0xf9, 0xfa), Color.FromArgb(0x20, 0x24, 0x28),
        Color.FromArgb(0x64, 0x6b, 0x73), Color.FromArgb(0x00, 0x78, 0xd4), Color.FromArgb(0xd3, 0xd7, 0xdb),
        Color.FromArgb(0x9a, 0x6a, 0x00), Color.FromArgb(0xc0, 0x39, 0x2b), Color.FromArgb(0x2e, 0x7d, 0x32));

    /// <summary>Background / foreground for a row or item in a non-default state
    /// (see <c>core/RUNTIME_CONTRACT.md</c>). <c>null</c> for <c>normal</c>.</summary>
    public (Color back, Color fore)? RowState(string? state) => state switch
    {
        "warn" => (Blend(Surface, Warn, 0.22), Text),
        "error" => (Blend(Surface, Error, 0.24), Text),
        "active" => (Blend(Surface, Accent, 0.28), Text),
        _ => null,
    };

    internal static Color Blend(Color a, Color b, double t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
}

/// <summary>One item in a context menu or menu-bar dropdown (see ZU-80).</summary>
public sealed record ZuiMenuItem(string Label = "", string Channel = "", string Payload = "",
    bool Enabled = true, bool Checked = false, bool Separator = false,
    IReadOnlyList<ZuiMenuItem>? Submenu = null)
{
    public static ZuiMenuItem Sep { get; } = new(Separator: true);
}

/// <summary>ToolStrip colours driven by the active <see cref="ZuiTheme"/>.</summary>
internal sealed class ZuiColorTable(ZuiTheme theme) : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => theme.Raised;
    public override Color ImageMarginGradientBegin => theme.Raised;
    public override Color ImageMarginGradientMiddle => theme.Raised;
    public override Color ImageMarginGradientEnd => theme.Raised;
    public override Color MenuBorder => theme.Border;
    public override Color MenuItemBorder => theme.Accent;
    public override Color MenuItemSelected => ZuiTheme.Blend(theme.Raised, theme.Accent, 0.30);
    public override Color MenuItemSelectedGradientBegin => MenuItemSelected;
    public override Color MenuItemSelectedGradientEnd => MenuItemSelected;
    public override Color MenuItemPressedGradientBegin => theme.Raised;
    public override Color MenuItemPressedGradientEnd => theme.Raised;
    public override Color MenuStripGradientBegin => theme.Raised;
    public override Color MenuStripGradientEnd => theme.Raised;
    public override Color SeparatorDark => theme.Border;
    public override Color SeparatorLight => theme.Border;
}

/// <summary>Builds compiled nodes as operating-system WinForms controls. There is no browser engine.</summary>
public sealed class ZuiHost : IDisposable
{
    private readonly Control _parent;
    private readonly Dictionary<string, List<Action<string>>> _handlers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Control> _exports = new(StringComparer.Ordinal);
    private readonly Dictionary<Control, RowStore> _rows = new();
    private readonly Dictionary<string, ToolStripMenuItem> _menuItems = new(StringComparer.OrdinalIgnoreCase);
    private ToolStripRenderer _menuRenderer;
    private bool _disposed;
    private int _buildCount;

    /// <summary>Shared native tooltip host for every <c>tooltip="…"</c> node.</summary>
    public ToolTip Tooltip { get; } = new() { AutoPopDelay = 12000, InitialDelay = 500, ReshowDelay = 200 };

    public ZuiHost(Control parent)
    {
        _parent = parent ?? throw new ArgumentNullException(nameof(parent));
        State = new ZuiState(this);
        _menuRenderer = new ToolStripProfessionalRenderer(new ZuiColorTable(Theme));
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
        _menuRenderer = new ToolStripProfessionalRenderer(new ZuiColorTable(Theme));
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
            case "pressed":
                if (c is Button tgl) { tgl.Tag = Bool(value); StyleToggle(tgl, Bool(value)); return true; }
                return false;
            case "attention":
                c.Font = new Font(c.Font, Bool(value) ? FontStyle.Bold : FontStyle.Regular);
                if (c is Label al) al.ForeColor = Bool(value) ? Theme.Accent : Theme.Text;
                return true;
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
                    case TabControl or Panel when _tabs.ContainsKey(c):
                        SelectTab(name, Str(value)); return true;
                    default: return false;
                }
            case "source":
                if (c is PictureBox img) { SetImageSource(img, value); return true; }
                return false;
            case "selectedvalue": case "selectedtext":
                if (c is ComboBox cbv)
                {
                    var target = Str(value);
                    var idx = cbv.Tag is string[] vals ? Array.IndexOf(vals, target) : -1;
                    if (idx < 0) idx = cbv.Items.IndexOf(target);
                    if (idx >= 0) { cbv.SelectedIndex = idx; return true; }
                }
                return false;
            case "selection":
                SetSelection(name, value switch
                {
                    IEnumerable<string> keys => keys,
                    string s when s.TrimStart().StartsWith('[') => JsonSerializer.Deserialize<string[]>(s) ?? [],
                    string s => [s],
                    _ => [],
                });
                return true;
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
            "pressed" => c is Button pbtn && pbtn.Tag is bool pv && pv,
            "value" => c switch { TrackBar tb => tb.Value, ProgressBar pb => pb.Value, NumericUpDown n => (int)n.Value, TextBox t => t.Text, _ => null },
            "selected" or "selectedindex" => c switch { ComboBox cx => cx.SelectedIndex, ListBox l => l.SelectedIndex, DataGridView g => g.CurrentRow?.Index ?? -1, _ => null },
            "selectedvalue" or "selectedtext" => c is ComboBox cv ? SelectedOptionValue(cv) : null,
            "selection" => GetSelection(name),
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
        ["root", "window", "col", "fill", "panel-body"];
    private static readonly string[] HorizontalKinds =
        ["row", "statusbar", "contextbar", "nav", "workspace"];
    private static readonly string[] LeafKinds =
        ["heading", "section-label", "text", "empty", "item", "menu", "button", "input",
         "textarea", "check", "select", "dropdown", "slider", "progress", "table", "tree",
         "list", "number", "console", "image", "spinner", "loading", "sep", "option", "column"];

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

        // Containers whose children are placed by a bespoke rule rather than the
        // generic vertical/horizontal stack.
        switch (node.Kind)
        {
            case "grid": return Finish(parent, node, BuildGrid(node));
            case "scroll": return Finish(parent, node, BuildScroll(node));
            case "tabs": return Finish(parent, node, BuildTabs(node));
            case "splitter": return Finish(parent, node, BuildSplitter(node));
        }

        Control control = node.Kind switch
        {
            "panel" => MakePanel(node),
            "sidebar" => MakeSidebar(),
            "console" => MakeConsole(node),
            "image" => MakeImage(node),
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
            "number" => new NumericUpDown
            {
                Minimum = Int(node, "min", 0), Maximum = Int(node, "max", int.MaxValue / 2),
                Increment = Math.Max(1, Int(node, "step", 1)),
                Value = Math.Clamp(Int(node, "value", Int(node, "min", 0)), Int(node, "min", 0), Int(node, "max", int.MaxValue / 2)),
                MinimumSize = new Size(90, 0),
            },
            "slider" => Slider(node),
            "progress" => new ProgressBar
            {
                Minimum = 0, Maximum = 100, Value = Math.Clamp(Int(node, "value", 0), 0, 100), Height = 16,
            },
            "table" => Table(node),
            "tree" => Tree(node),
            "list" => MakeList(node),
            "spinner" or "loading" => new ProgressBar { Style = ProgressBarStyle.Marquee, Width = 110, Height = 10 },
            "sep" => new Label { AutoSize = false, Height = 1, Margin = new Padding(0, Gap / 2, 0, Gap / 2) },
            _ => VStack(fill: true),
        };

        Register(control, node);
        Place(parent, control, node);

        if (!LeafKinds.Contains(node.Kind))
        {
            var content = control is GroupBox box ? box.Controls[0] : control;
            foreach (var child in node.Nodes) AddNode(content, child);
            if (content is TableLayoutPanel { Tag: "v" } stack) TopPack(stack);
        }
        return control;
    }

    /// <summary>Common lookup-name / disabled / tooltip / event registration.
    /// Resolution priority for a name is export &gt; bind &gt; id.</summary>
    private void Register(Control control, ZuiNode node)
    {
        if (node.Attrs.TryGetValue("id", out var id)) control.Name = id;
        foreach (var key in new[]
        {
            node.Attrs.GetValueOrDefault("id", ""),
            node.Attrs.GetValueOrDefault("bind", ""),
            node.Attrs.GetValueOrDefault("export", ""),
        })
            if (key.Length > 0) _exports[key] = control;
        if (node.Attrs.ContainsKey("disabled")) control.Enabled = false;
        if (node.Attrs.TryGetValue("tooltip", out var tip) && tip.Length > 0) Tooltip.SetToolTip(control, tip);
        RegisterCollection(control, node);
        WireInteractions(control, node);
    }

    /// <summary>Registers a bespoke container (grid / scroll / tabs / splitter) whose
    /// own builder has already populated its children.</summary>
    private Control Finish(Control parent, ZuiNode node, Control control)
    {
        Register(control, node);
        Place(parent, control, node);
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

        if (table.Tag as string == "grid") { PlaceInGrid(table, control, node); return; }

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

    private void PlaceInGrid(TableLayoutPanel grid, Control control, ZuiNode node)
    {
        var (row, col) = _gridCursor.GetValueOrDefault(grid, (0, 0));
        int span = Math.Clamp(Int(node, "colspan", 1), 1, grid.ColumnCount);
        if (col + span > grid.ColumnCount) { row++; col = 0; }
        if (row >= grid.RowCount) { grid.RowStyles.Add(new RowStyle(SizeType.AutoSize)); grid.RowCount = row + 1; }
        control.Dock = DockStyle.Fill;
        control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        grid.Controls.Add(control, col, row);
        if (span > 1) grid.SetColumnSpan(control, span);
        col += span;
        if (col >= grid.ColumnCount) { row++; col = 0; }
        _gridCursor[grid] = (row, col);
    }

    private static bool Grows(ZuiNode node, bool horizontal) => horizontal
        ? node.Kind is "input" or "textarea" or "select" or "dropdown" or "slider" or "progress"
            or "table" or "tree" or "list" or "console" or "fill" or "row" or "col" or "workspace"
            or "tabs" or "scroll" or "splitter"
        : node.Kind is "table" or "tree" or "list" or "console" or "textarea" or "fill" or "workspace"
            or "window" or "tabs" or "scroll" or "splitter";

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

    private TextBox MakeConsole(ZuiNode node) => new()
    {
        Multiline = true, ReadOnly = true, WordWrap = false,
        ScrollBars = ScrollBars.Both, BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Consolas", 9F), BackColor = Color.FromArgb(0x0c, 0x0c, 0x0c),
        ForeColor = Color.FromArgb(0xd0, 0xd0, 0xd0), MinimumSize = new Size(0, 140),
        Tag = new ConsoleBuffer(Int(node, "lines", 5000)),
    };

    private sealed class ConsoleBuffer(int cap) { public int Cap { get; } = Math.Max(200, cap); }

    /// <summary>Appends a line to a <c>console</c> control and scrolls to the bottom,
    /// trimming to the capped line buffer.</summary>
    public void Append(string name, string text)
    {
        if (Require(name) is not TextBox box || box.Tag is not ConsoleBuffer buf) return;
        var lines = box.Lines.ToList();
        lines.AddRange(text.Replace("\r\n", "\n").Split('\n'));
        if (lines.Count > buf.Cap) lines.RemoveRange(0, lines.Count - buf.Cap);
        box.Lines = lines.ToArray();
        box.SelectionStart = box.TextLength;
        box.ScrollToCaret();
    }

    private PictureBox MakeImage(ZuiNode node)
    {
        var pic = new PictureBox
        {
            SizeMode = node.Attrs.GetValueOrDefault("fit", "uniform") switch
            {
                "fill" or "uniform-to-fill" => PictureBoxSizeMode.Zoom,
                "none" => PictureBoxSizeMode.CenterImage,
                "stretch" => PictureBoxSizeMode.StretchImage,
                _ => PictureBoxSizeMode.Zoom,
            },
            MinimumSize = new Size(Int(node, "width", 48), Int(node, "height", 48)),
            BackColor = Theme.Raised,
        };
        if (node.Attrs.TryGetValue("src", out var src)) SetImageSource(pic, src);
        return pic;
    }

    private static void SetImageSource(PictureBox pic, object? source)
    {
        pic.Image?.Dispose();
        pic.Image = source switch
        {
            null or "" => null,
            byte[] bytes => SafeImage(() => Image.FromStream(new System.IO.MemoryStream(bytes))),
            string path when System.IO.File.Exists(path) => SafeImage(() => Image.FromFile(path)),
            _ => null,
        };
    }

    private static Image? SafeImage(Func<Image> load) { try { return load(); } catch { return null; } }

    // ---- grid / scroll / tabs / splitter ---------------------------------

    private readonly Dictionary<Control, Dictionary<string, Control>> _tabs = new();
    private readonly Dictionary<TableLayoutPanel, (int row, int col)> _gridCursor = new();

    private TableLayoutPanel BuildGrid(ZuiNode node)
    {
        int cols = Math.Max(1, Int(node, "cols", 2));
        int gap = Int(node, "gap", Gap);
        var g = new TableLayoutPanel
        {
            ColumnCount = cols, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top, Padding = new Padding(gap / 2), Tag = "grid",
            GrowStyle = TableLayoutPanelGrowStyle.AddRows,
        };
        for (int i = 0; i < cols; i++) g.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / cols));
        _gridCursor[g] = (0, 0);
        foreach (var child in node.Nodes) AddNode(g, child);
        return g;
    }

    private Panel BuildScroll(ZuiNode node)
    {
        var host = new Panel { AutoScroll = true, Dock = DockStyle.Fill, MinimumSize = new Size(0, 80) };
        var inner = VStack(fill: false);
        inner.Dock = DockStyle.Top;
        host.Controls.Add(inner);
        foreach (var child in node.Nodes) AddNode(inner, child);
        return host;
    }

    private Control BuildTabs(ZuiNode node)
    {
        var panels = node.Nodes.Where(n => n.Kind == "tabpanel").ToArray();
        bool headless = node.Attrs.ContainsKey("headless") || node.Attrs.ContainsKey("flat");
        string? onTab = node.Attrs.GetValueOrDefault("ontab") ?? node.Attrs.GetValueOrDefault("on");
        var map = new Dictionary<string, Control>(StringComparer.Ordinal);

        if (headless)
        {
            var deck = new Panel { Dock = DockStyle.Fill, MinimumSize = new Size(0, 120) };
            for (int i = 0; i < panels.Length; i++)
            {
                var v = VStack(fill: true);
                v.Visible = i == 0;
                deck.Controls.Add(v);
                foreach (var gc in panels[i].Nodes) AddNode(v, gc);
                TopPack(v);
                map[TabId(panels[i], i)] = v;
            }
            _tabs[deck] = map;
            return deck;
        }

        var tc = new TabControl { Dock = DockStyle.Fill, MinimumSize = new Size(0, 140) };
        for (int i = 0; i < panels.Length; i++)
        {
            var page = new TabPage(panels[i].Text.Length > 0 ? panels[i].Text : $"Tab {i + 1}") { UseVisualStyleBackColor = true };
            var v = VStack(fill: true);
            page.Controls.Add(v);
            tc.TabPages.Add(page);
            foreach (var gc in panels[i].Nodes) AddNode(v, gc);
            TopPack(v);
            map[TabId(panels[i], i)] = page;
        }
        _tabs[tc] = map;
        if (onTab is not null)
            tc.SelectedIndexChanged += (_, _) =>
            {
                var id = map.FirstOrDefault(kv => kv.Value == tc.SelectedTab).Key;
                if (id is not null) Dispatch(onTab, id);
            };
        return tc;
    }

    private static string TabId(ZuiNode panel, int index) =>
        panel.Attrs.GetValueOrDefault("id", panel.Attrs.GetValueOrDefault("value", index.ToString()));

    private void SelectTab(string name, string id)
    {
        var control = Require(name);
        if (!_tabs.TryGetValue(control, out var map) || !map.TryGetValue(id, out var target)) return;
        switch (control)
        {
            case TabControl tc when target is TabPage page: tc.SelectedTab = page; break;
            case Panel deck:
                foreach (Control c in deck.Controls) c.Visible = c == target;
                break;
        }
    }

    private SplitContainer BuildSplitter(ZuiNode node)
    {
        var kids = node.Nodes.Where(n => n.Kind is not ("column" or "option")).Take(2).ToArray();
        var sc = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = node.Attrs.ContainsKey("horizontal") ? Orientation.Horizontal : Orientation.Vertical,
            MinimumSize = new Size(0, 120),
        };
        if (kids.Length > 0) { var v = VStack(fill: true); sc.Panel1.Controls.Add(v); foreach (var c in kids[0].Nodes) AddNode(v, c); TopPack(v); sc.Panel1MinSize = Int(kids[0], "min", 120); }
        if (kids.Length > 1) { var v = VStack(fill: true); sc.Panel2.Controls.Add(v); foreach (var c in kids[1].Nodes) AddNode(v, c); TopPack(v); sc.Panel2MinSize = Int(kids[1], "min", 120); }
        int pos = Int(node, "pos", Int(node, "width", 260));
        try { sc.SplitterDistance = Math.Max(sc.Panel1MinSize, pos); } catch { /* not realized yet */ }
        return sc;
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
        var options = node.Nodes.Where(n => n.Kind is "option" or "item").ToArray();
        box.Items.AddRange(options.Select(n => (object)n.Text).ToArray());
        // Parallel value list: <option value="x">Label</option>. Falls back to the label.
        box.Tag = options.Select(n => n.Attrs.GetValueOrDefault("value", n.Text)).ToArray();
        if (box.Items.Count > 0) box.SelectedIndex = 0;
        return box;
    }

    private static string SelectedOptionValue(ComboBox box) =>
        box.Tag is string[] values && box.SelectedIndex >= 0 && box.SelectedIndex < values.Length
            ? values[box.SelectedIndex]
            : box.SelectedItem?.ToString() ?? "";

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
        var item = new TreeNode(node.Text)
        {
            Name = node.Attrs.GetValueOrDefault("key", node.Attrs.GetValueOrDefault("id", node.Text)),
        };
        foreach (var child in node.Nodes) item.Nodes.Add(TreeItem(child));
        return item;
    }

    private MenuStrip BuildMenuStrip(ZuiNode node)
    {
        var strip = new MenuStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden, Renderer = _menuRenderer };
        foreach (var menu in node.Nodes) strip.Items.Add(BuildMenuItem(menu, ""));
        return strip;
    }

    private ToolStripItem BuildMenuItem(ZuiNode node, string parentPath)
    {
        if (node.Kind == "sep") return new ToolStripSeparator();
        var item = new ToolStripMenuItem(node.Text);
        var path = parentPath.Length == 0 ? node.Text : parentPath + "/" + node.Text;
        _menuItems[path] = item;
        if (node.Attrs.TryGetValue("shortcut", out var text) && TryParseShortcut(text, out var keys))
        {
            item.ShortcutKeys = keys;
            item.ShowShortcutKeys = true;
        }
        if (node.Attrs.ContainsKey("checked")) item.Checked = true;
        if (node.Attrs.TryGetValue("on", out var channel))
            item.Click += (_, _) => Dispatch(channel, "");
        if (node.Attrs.TryGetValue("onmenuopen", out var open))
            item.DropDownOpening += (_, _) => Dispatch(open, path);
        foreach (var child in node.Nodes) item.DropDownItems.Add(BuildMenuItem(child, path));
        return item;
    }

    /// <summary>Enables / disables a menu-bar item addressed by its "Menu/Item" path.</summary>
    public void SetMenuEnabled(string path, bool enabled)
    {
        if (_menuItems.TryGetValue(path, out var item)) item.Enabled = enabled;
    }

    public void SetMenuChecked(string path, bool value)
    {
        if (_menuItems.TryGetValue(path, out var item)) item.Checked = value;
    }

    /// <summary>Pops a context menu at the cursor for the named control and routes
    /// each item's click to its channel/payload (ZU-80).</summary>
    public void PopupMenu(string name, IEnumerable<ZuiMenuItem> items)
    {
        var control = Require(name);
        var menu = new ContextMenuStrip { Renderer = _menuRenderer };
        foreach (var spec in items) menu.Items.Add(BuildSpecItem(spec));
        menu.Show(Cursor.Position);
    }

    private ToolStripItem BuildSpecItem(ZuiMenuItem spec)
    {
        if (spec.Separator) return new ToolStripSeparator();
        var item = new ToolStripMenuItem(spec.Label) { Enabled = spec.Enabled, Checked = spec.Checked };
        if (spec.Channel.Length > 0) item.Click += (_, _) => Dispatch(spec.Channel, spec.Payload);
        if (spec.Submenu is { Count: > 0 })
            foreach (var sub in spec.Submenu) item.DropDownItems.Add(BuildSpecItem(sub));
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

    // ---- Interaction wiring -------------------------------------------------
    //
    // The `on` / `->` channel carries a normalized per-control payload (see
    // core/PROTOCOL.md): button -> "", input/textarea -> current text, check ->
    // "true"/"false", slider/number -> value, select -> selected option value,
    // selectable table/list/tree -> JSON array of selected item keys. Extra
    // channels: onactivate (double-click / Enter -> item key), oncommit
    // (Enter / blur on an editable -> value), ontoggle (toggle button).

    private void WireInteractions(Control control, ZuiNode node)
    {
        string? on = node.Attrs.GetValueOrDefault("on");
        string? activate = node.Attrs.GetValueOrDefault("onactivate");
        string? commit = node.Attrs.GetValueOrDefault("oncommit");

        switch (control)
        {
            case Button button when node.Attrs.GetValueOrDefault("kind") == "toggle":
            {
                string? toggle = node.Attrs.GetValueOrDefault("ontoggle") ?? on;
                button.Click += (_, _) =>
                {
                    bool pressed = !(button.Tag is bool b && b);
                    button.Tag = pressed;
                    StyleToggle(button, pressed);
                    if (toggle is not null) Dispatch(toggle, pressed ? "true" : "false");
                };
                break;
            }
            case Button button:
                if (on is not null) button.Click += (_, _) => Dispatch(on, "");
                break;
            case CheckBox check:
                if (on is not null) check.CheckedChanged += (_, _) => Dispatch(on, check.Checked ? "true" : "false");
                break;
            case TrackBar slider:
                if (on is not null) slider.ValueChanged += (_, _) => Dispatch(on, slider.Value.ToString());
                break;
            case NumericUpDown number:
                if (on is not null) number.ValueChanged += (_, _) => Dispatch(on, ((int)number.Value).ToString());
                if (commit is not null)
                {
                    number.Leave += (_, _) => Dispatch(commit, ((int)number.Value).ToString());
                    number.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) Dispatch(commit, ((int)number.Value).ToString()); };
                }
                break;
            case ComboBox combo:
                if (on is not null) combo.SelectedIndexChanged += (_, _) => Dispatch(on, SelectedOptionValue(combo));
                break;
            case TextBox text:
                if (on is not null) text.TextChanged += (_, _) => Dispatch(on, text.Text);
                if (commit is not null)
                {
                    text.Leave += (_, _) => Dispatch(commit, text.Text);
                    text.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) Dispatch(commit, text.Text); };
                }
                break;
            case DataGridView grid:
                if (on is not null) grid.SelectionChanged += (_, _) => Dispatch(on, SelectionJson(grid));
                if (activate is not null)
                {
                    grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Dispatch(activate, KeyOfRow(grid.Rows[e.RowIndex])); };
                    grid.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter && grid.CurrentRow is { } r) Dispatch(activate, KeyOfRow(r)); };
                }
                break;
            case ListBox list:
                if (on is not null) list.SelectedIndexChanged += (_, _) => Dispatch(on, SelectionJson(list));
                if (activate is not null)
                {
                    list.DoubleClick += (_, _) => { if (list.SelectedItem is RowItem it) Dispatch(activate, it.Key); };
                    list.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter && list.SelectedItem is RowItem it) Dispatch(activate, it.Key); };
                }
                break;
            case TreeView tree:
                if (on is not null) tree.AfterSelect += (_, e) => Dispatch(on, JsonSerializer.Serialize(e.Node?.Name is { Length: > 0 } k ? new[] { k } : Array.Empty<string>()));
                if (activate is not null)
                {
                    tree.NodeMouseDoubleClick += (_, e) => Dispatch(activate, e.Node.Name);
                    tree.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter && tree.SelectedNode is { } n) Dispatch(activate, n.Name); };
                }
                break;
            default:
                // Labels, containers, nav items, etc. — a plain click channel.
                if (on is not null) control.Click += (_, _) => Dispatch(on, "");
                break;
        }

        WireContextAndDrag(control, node);
    }

    // ---- Context menu + drag & drop (ZU-80 / ZU-81) -----------------------

    private void WireContextAndDrag(Control control, ZuiNode node)
    {
        string? name = LookupName(control);

        if (node.Attrs.TryGetValue("oncontext", out var ctx) && name is not null)
        {
            void Request() => Dispatch(ctx, JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["control"] = name,
                ["keys"] = GetSelection(name),
            }));
            control.MouseUp += (_, e) => { if (e.Button == MouseButtons.Right) Request(); };
            control.KeyDown += (_, e) => { if (e.KeyCode == Keys.Apps) Request(); };
        }

        if (node.Attrs.ContainsKey("dragsource") && name is not null)
        {
            control.MouseDown += (_, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                var keys = GetSelection(name);
                if (keys.Count == 0) return;
                var data = new DataObject();
                data.SetData("zui-keys", JsonSerializer.Serialize(keys));
                control.DoDragDrop(data, DragDropEffects.Move | DragDropEffects.Copy);
            };
        }

        if (node.Attrs.TryGetValue("ondrop", out var drop) && name is not null)
        {
            control.AllowDrop = true;
            control.DragEnter += (_, e) => e.Effect =
                e.Data?.GetDataPresent("zui-keys") == true || e.Data?.GetDataPresent(DataFormats.FileDrop) == true
                    ? DragDropEffects.Copy : DragDropEffects.None;
            control.DragDrop += (_, e) =>
            {
                var payload = new Dictionary<string, object> { ["target"] = name };
                if (e.Data?.GetData("zui-keys") is string j)
                {
                    payload["keys"] = JsonSerializer.Deserialize<string[]>(j) ?? [];
                    if (control is DataGridView g && g.HitTest(g.PointToClient(new Point(e.X, e.Y)).X, g.PointToClient(new Point(e.X, e.Y)).Y).RowIndex is >= 0 and var ri)
                        payload["targetKey"] = KeyOfRow(g.Rows[ri]);
                }
                else if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths)
                    payload["paths"] = paths;
                Dispatch(drop, JsonSerializer.Serialize(payload));
            };
        }
    }

    private string? LookupName(Control control) =>
        _exports.FirstOrDefault(kv => ReferenceEquals(kv.Value, control)).Key;

    /// <summary>Repaints a control subtree in the active theme. Every native
    /// control kind zUI emits is covered for both Holo and Clean (ZU-70 / ZU-79).</summary>
    private void ApplyTheme(Control root)
    {
        bool input = root is TextBox or DataGridView or ListBox or TreeView or ComboBox or NumericUpDown;
        root.BackColor = input ? Theme.Raised : Theme.Surface;
        root.ForeColor = Theme.Text;

        switch (root)
        {
            case Button button when button.Tag is bool pressed:
                StyleToggle(button, pressed); break;
            case Button button:
                button.FlatAppearance.BorderColor = Theme.Border;
                button.FlatAppearance.MouseOverBackColor = Theme.Raised == Theme.Surface ? Theme.Border : ZuiTheme.Blend(Theme.Raised, Theme.Accent, 0.18);
                button.BackColor = Theme.Raised; break;
            case GroupBox: root.ForeColor = Theme.Accent; break;
            case Label label when label.Font.Bold: label.ForeColor = Theme.Accent; break;
            case TextBox tb: tb.BorderStyle = BorderStyle.FixedSingle; break;
            case ComboBox combo:
                combo.FlatStyle = FlatStyle.Flat; combo.BackColor = Theme.Raised; combo.ForeColor = Theme.Text; break;
            case NumericUpDown num:
                num.BorderStyle = BorderStyle.FixedSingle; num.BackColor = Theme.Raised; num.ForeColor = Theme.Text; break;
            case TrackBar bar: bar.BackColor = Theme.Surface; break;
            case ProgressBar pbar: pbar.ForeColor = Theme.Accent; pbar.BackColor = Theme.Raised; break;
            case TreeView tree:
                tree.BackColor = Theme.Raised; tree.LineColor = Theme.Border; break;
            case ListBox lb: lb.BorderStyle = BorderStyle.FixedSingle; lb.Invalidate(); break;
            case SplitContainer split:
                split.BackColor = Theme.Border;
                split.Panel1.BackColor = Theme.Surface; split.Panel2.BackColor = Theme.Surface; break;
            case TabControl tabs:
                tabs.BackColor = Theme.Surface;
                foreach (TabPage page in tabs.TabPages) page.BackColor = Theme.Surface;
                break;
            case MenuStrip menu:
                menu.BackColor = Theme.Raised; menu.ForeColor = Theme.Text;
                menu.Renderer = _menuRenderer;
                foreach (ToolStripItem item in menu.Items) ThemeMenuItem(item);
                break;
            case DataGridView grid:
                grid.EnableHeadersVisualStyles = false;
                grid.BackgroundColor = Theme.Surface;
                grid.GridColor = Theme.Border;
                grid.DefaultCellStyle.BackColor = Theme.Surface;
                grid.DefaultCellStyle.ForeColor = Theme.Text;
                grid.DefaultCellStyle.SelectionBackColor = Theme.Accent;
                grid.DefaultCellStyle.SelectionForeColor = Theme.Window;
                grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Raised;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Text;
                break;
        }

        if (_rows.TryGetValue(root, out var store)) RecolorRows(root, store);
        foreach (Control child in root.Controls) ApplyTheme(child);
    }

    private void ThemeMenuItem(ToolStripItem item)
    {
        item.BackColor = Theme.Raised;
        item.ForeColor = Theme.Text;
        if (item is ToolStripMenuItem menuItem)
        {
            menuItem.DropDown.Renderer = _menuRenderer;
            foreach (ToolStripItem sub in menuItem.DropDownItems) ThemeMenuItem(sub);
        }
    }

    private static int Int(ZuiNode node, string key, int fallback) => node.Attrs.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) ? parsed : fallback;

    // ---- Collection binding (source=) -------------------------------------
    //
    // A table / list / tree marked `source=` is filled through the collection
    // API below, never Build(). Records are string maps: a table row is
    // { key, <field>: value, …, state? }; a list/tree item is { key, text, …,
    // state? }. Rows are identified by their `key`; selection is preserved by
    // key across every update. See core/RUNTIME_CONTRACT.md and ZU-67.

    /// <summary>Leading-element / owner-data record for a list item.</summary>
    internal sealed class RowItem(string key, string text)
    {
        public string Key { get; } = key;
        public string Text { get; set; } = text;
        public IReadOnlyDictionary<string, string> Record { get; set; } = new Dictionary<string, string>();
        public override string ToString() => Text;
    }

    private sealed class RowStore
    {
        public string[] Fields = [];                                  // table column field names
        public readonly List<string> Order = [];                      // keys, in display order
        public readonly Dictionary<string, Dictionary<string, string>> Records = new(StringComparer.Ordinal);
    }

    private static ListBox MakeList(ZuiNode node) => new()
    {
        MinimumSize = new Size(0, 120), BorderStyle = BorderStyle.FixedSingle,
        IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 22,
        SelectionMode = node.Attrs.ContainsKey("selectable") ? SelectionMode.MultiExtended : SelectionMode.One,
    };

    private void RegisterCollection(Control control, ZuiNode node)
    {
        if (control is not (DataGridView or ListBox or TreeView)) return;
        var store = new RowStore();
        if (control is DataGridView grid)
            store.Fields = grid.Columns.Cast<DataGridViewColumn>().Select(c => c.Name).ToArray();
        _rows[control] = store;
        if (control is ListBox lb) lb.DrawItem += (_, e) => DrawListItem(lb, e);
    }

    private RowStore Store(string name)
    {
        var c = Require(name);
        return _rows.TryGetValue(c, out var s) ? s
            : throw new InvalidOperationException($"zUI: '{name}' is not a collection control (table/list/tree).");
    }

    private static string Key(IReadOnlyDictionary<string, string> record) =>
        record.TryGetValue("key", out var k) && k.Length > 0 ? k
            : throw new ArgumentException("zUI: every collection record needs a non-empty 'key'.");

    /// <summary>Replaces every row/item, preserving selection by key.</summary>
    public void SetRows(string name, IEnumerable<IReadOnlyDictionary<string, string>> records)
    {
        var store = Store(name);
        var keep = new HashSet<string>(GetSelection(name), StringComparer.Ordinal);
        store.Order.Clear();
        store.Records.Clear();
        foreach (var r in records)
        {
            var key = Key(r);
            store.Order.Add(key);
            store.Records[key] = new Dictionary<string, string>(r, StringComparer.Ordinal);
        }
        RenderRows(name);
        SetSelection(name, keep.Where(store.Records.ContainsKey));
    }

    public void AppendRow(string name, IReadOnlyDictionary<string, string> record) => InsertRow(name, Store(name).Order.Count, record);

    public void InsertRow(string name, int index, IReadOnlyDictionary<string, string> record)
    {
        var store = Store(name);
        var key = Key(record);
        store.Records[key] = new Dictionary<string, string>(record, StringComparer.Ordinal);
        store.Order.Remove(key);
        store.Order.Insert(Math.Clamp(index, 0, store.Order.Count), key);
        var keep = GetSelection(name).ToArray();
        RenderRows(name);
        SetSelection(name, keep);
    }

    public void RemoveRow(string name, string key)
    {
        var store = Store(name);
        if (!store.Records.Remove(key)) return;
        store.Order.Remove(key);
        var keep = GetSelection(name).Where(k => k != key).ToArray();
        RenderRows(name);
        SetSelection(name, keep);
    }

    /// <summary>Merges <paramref name="record"/> into the row and re-renders just that row.</summary>
    public void UpdateRow(string name, string key, IReadOnlyDictionary<string, string> record)
    {
        var store = Store(name);
        if (!store.Records.TryGetValue(key, out var existing)) { AppendRow(name, record); return; }
        foreach (var (k, v) in record) existing[k] = v;
        RefreshRow(name, key);
    }

    public void RefreshRow(string name, string key)
    {
        var store = Store(name);
        if (!store.Records.TryGetValue(key, out var record)) return;
        int i = store.Order.IndexOf(key);
        switch (Require(name))
        {
            case DataGridView grid when i >= 0 && i < grid.Rows.Count:
                FillGridRow(grid.Rows[i], store, record);
                break;
            case ListBox lb when i >= 0 && i < lb.Items.Count:
                ((RowItem)lb.Items[i]).Text = record.GetValueOrDefault("text", key);
                ((RowItem)lb.Items[i]).Record = record;
                lb.Invalidate(lb.GetItemRectangle(i));
                break;
            case TreeView tree when i >= 0 && i < tree.Nodes.Count:
                ApplyNodeState(tree.Nodes[i], record);
                break;
        }
    }

    public void ClearRows(string name)
    {
        var store = Store(name);
        store.Order.Clear();
        store.Records.Clear();
        RenderRows(name);
    }

    public IReadOnlyList<string> GetRowKeys(string name) => Store(name).Order.ToArray();

    private void RenderRows(string name)
    {
        var store = Store(name);
        switch (Require(name))
        {
            case DataGridView grid:
                grid.SuspendLayout();
                grid.Rows.Clear();
                foreach (var key in store.Order)
                {
                    int idx = grid.Rows.Add();
                    FillGridRow(grid.Rows[idx], store, store.Records[key]);
                }
                grid.ClearSelection();
                grid.ResumeLayout();
                break;
            case ListBox lb:
                lb.BeginUpdate();
                lb.Items.Clear();
                foreach (var key in store.Order)
                {
                    var rec = store.Records[key];
                    lb.Items.Add(new RowItem(key, rec.GetValueOrDefault("text", key)) { Record = rec });
                }
                lb.EndUpdate();
                break;
            case TreeView tree:
                tree.BeginUpdate();
                tree.Nodes.Clear();
                foreach (var key in store.Order)
                {
                    var rec = store.Records[key];
                    var tn = new TreeNode(rec.GetValueOrDefault("text", key)) { Name = key };
                    ApplyNodeState(tn, rec);
                    tree.Nodes.Add(tn);
                }
                tree.EndUpdate();
                break;
        }
    }

    private void RecolorRows(Control control, RowStore store)
    {
        switch (control)
        {
            case DataGridView grid:
                foreach (DataGridViewRow row in grid.Rows)
                    if (row.Tag is string k && store.Records.TryGetValue(k, out var rec))
                    {
                        var s = Theme.RowState(rec.GetValueOrDefault("state"));
                        row.DefaultCellStyle.BackColor = s?.back ?? Theme.Surface;
                        row.DefaultCellStyle.ForeColor = s?.fore ?? Theme.Text;
                    }
                break;
            case TreeView tree:
                foreach (TreeNode n in tree.Nodes)
                    if (store.Records.TryGetValue(n.Name, out var rec)) ApplyNodeState(n, rec);
                break;
            case ListBox lb:
                lb.Invalidate();
                break;
        }
    }

    private void FillGridRow(DataGridViewRow row, RowStore store, IReadOnlyDictionary<string, string> record)
    {
        row.Tag = Key(record);
        for (int c = 0; c < store.Fields.Length && c < row.Cells.Count; c++)
            row.Cells[c].Value = record.GetValueOrDefault(store.Fields[c], "");
        var style = Theme.RowState(record.GetValueOrDefault("state"));
        row.DefaultCellStyle.BackColor = style?.back ?? Theme.Surface;
        row.DefaultCellStyle.ForeColor = style?.fore ?? Theme.Text;
    }

    private void ApplyNodeState(TreeNode node, IReadOnlyDictionary<string, string> record)
    {
        node.Text = record.GetValueOrDefault("text", node.Name);
        var style = Theme.RowState(record.GetValueOrDefault("state"));
        node.BackColor = style?.back ?? Color.Empty;
        node.ForeColor = style?.fore ?? Color.Empty;
    }

    private void DrawListItem(ListBox lb, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= lb.Items.Count) return;
        var item = (RowItem)lb.Items[e.Index];
        bool selected = (e.State & DrawItemState.Selected) != 0;
        var state = Theme.RowState(item.Record.GetValueOrDefault("state"));
        var back = selected ? Theme.Accent : state?.back ?? Theme.Surface;
        var fore = selected ? Theme.Window : state?.fore ?? Theme.Text;
        using var b = new SolidBrush(back);
        e.Graphics.FillRectangle(b, e.Bounds);
        var text = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height);
        if (item.Record.GetValueOrDefault("state") == "new")
        {
            using var dot = new SolidBrush(Theme.Accent);
            e.Graphics.FillEllipse(dot, e.Bounds.X + 4, e.Bounds.Y + e.Bounds.Height / 2 - 3, 6, 6);
            text = new Rectangle(e.Bounds.X + 16, e.Bounds.Y, e.Bounds.Width - 18, e.Bounds.Height);
        }
        TextRenderer.DrawText(e.Graphics, item.Text, lb.Font, text, fore,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    // ---- Selection --------------------------------------------------------

    /// <summary>The keys of the currently selected rows/items.</summary>
    public IReadOnlyList<string> GetSelection(string name) => Require(name) switch
    {
        DataGridView grid => grid.Rows.Cast<DataGridViewRow>().Where(r => r.Selected)
            .OrderBy(r => r.Index).Select(KeyOfRow).Where(k => k.Length > 0).ToArray(),
        ListBox list => list.SelectedIndices.Cast<int>().OrderBy(i => i)
            .Select(i => list.Items[i]).OfType<RowItem>().Select(i => i.Key).ToArray(),
        TreeView tree => tree.SelectedNode is { Name.Length: > 0 } n ? [n.Name] : [],
        _ => [],
    };

    public void SetSelection(string name, IEnumerable<string> keys)
    {
        var wanted = new HashSet<string>(keys, StringComparer.Ordinal);
        switch (Require(name))
        {
            case DataGridView grid:
                grid.ClearSelection();
                var hit = grid.Rows.Cast<DataGridViewRow>().Where(r => wanted.Contains(KeyOfRow(r))).ToList();
                if (hit.Count > 0 && hit[0].Cells.Count > 0)
                {
                    grid.CurrentCell = hit[0].Cells[0];   // set the anchor first, then extend
                    foreach (var r in hit) r.Selected = true;
                }
                break;
            case ListBox list:
                list.ClearSelected();
                for (int i = 0; i < list.Items.Count; i++)
                    if (list.Items[i] is RowItem it && wanted.Contains(it.Key)) list.SetSelected(i, true);
                break;
            case TreeView tree:
                tree.SelectedNode = tree.Nodes.Cast<TreeNode>().FirstOrDefault(n => wanted.Contains(n.Name));
                break;
        }
    }

    private static string KeyOfRow(DataGridViewRow row) => row.Tag as string ?? row.Index.ToString();

    private static string SelectionJson(Control control) => JsonSerializer.Serialize(control switch
    {
        DataGridView grid => grid.Rows.Cast<DataGridViewRow>().Where(r => r.Selected).OrderBy(r => r.Index).Select(KeyOfRow).ToArray(),
        ListBox list => list.SelectedIndices.Cast<int>().OrderBy(i => i).Select(i => list.Items[i]).OfType<RowItem>().Select(i => i.Key).ToArray(),
        _ => [],
    });

    private void StyleToggle(Button button, bool pressed)
    {
        button.FlatAppearance.BorderColor = pressed ? Theme.Accent : Theme.Border;
        button.BackColor = pressed ? ZuiTheme.Blend(Theme.Raised, Theme.Accent, 0.30) : Theme.Raised;
        button.ForeColor = Theme.Text;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _handlers.Clear(); _exports.Clear(); _rows.Clear(); _tabs.Clear(); _gridCursor.Clear();
        Tooltip.Dispose();
    }

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
