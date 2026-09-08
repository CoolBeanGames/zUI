#ifndef ZUI_H
#define ZUI_H

#include <functional>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <vector>

namespace zui {

using MessageHandler = std::function<void(const std::string& payload)>;
using Record = std::unordered_map<std::string, std::string>;

class Host;

/// zUI's in-process declarative state model. Values are stored as strings (the
/// wire form the compiler emits and the event channel carries). A change to one
/// property notifies only its bound controls and watchers - there is no global
/// render pass. See core/RUNTIME_CONTRACT.md.
class State {
public:
    void init(const std::string& name, const std::string& value);
    std::string get_string(const std::string& name) const;
    int get_int(const std::string& name) const;
    bool get_bool(const std::string& name) const;
    void set(const std::string& name, const std::string& value);
    void mutate(const std::string& name, const std::string& op);
    void assign(const std::string& name, const std::string& from_name);
    void watch(const std::string& name, MessageHandler handler);
    void flush();

private:
    friend class Host;
    Host* host_ = nullptr;
    std::unordered_map<std::string, std::string> values_;
    std::unordered_map<std::string, std::vector<std::string>> bindings_;
    std::unordered_map<std::string, std::vector<MessageHandler>> watchers_;
    std::unordered_set<std::string> propagating_;
    void add_binding(const std::string& state_name, const std::string& control_name);
    void propagate(const std::string& name);
};

struct Node {
    std::string kind;
    std::string text;
    std::unordered_map<std::string, std::string> attributes;
    std::vector<Node> children;
};

struct Theme {
    unsigned long window = 0x00161310;   // COLORREF: 0x00BBGGRR
    unsigned long surface = 0x00201b17;
    unsigned long raised = 0x002b2520;
    unsigned long text = 0x00f7f4ee;
    unsigned long muted = 0x00b3aa99;
    unsigned long accent = 0x00e5b533;
    unsigned long border = 0x00453d35;
    unsigned long warn = 0x0000b3ff;
    unsigned long error = 0x005252ff;
    unsigned long ok = 0x0000cc99;
    static Theme holo();
    static Theme clean();
};

/// One item in a context menu or menu-bar dropdown (ZU-80 parity).
struct MenuItem {
    std::string label;
    std::string channel;
    std::string payload;
    bool enabled = true;
    bool checked = false;
    bool separator = false;
    std::vector<MenuItem> submenu;
};

/// Builds compiler-emitted nodes directly as Win32 HWND controls.
class Host {
public:
    explicit Host(void* native_parent);
    ~Host();
    Host(const Host&) = delete;
    Host& operator=(const Host&) = delete;

    void build(const Node& root);
    void on(const std::string& channel, MessageHandler handler);
    void send(const std::string& channel, const std::string& payload = "");
    void set_theme(const std::string& name);
    void* find(const std::string& export_name) const;

    // Declarative state (see core/RUNTIME_CONTRACT.md).
    State& state() { return state_; }
    /// Binds a state property to the natural property of a registered control.
    /// One-way for display controls; two-way for editable controls.
    void bind(const std::string& state_name, const std::string& control_name);

    // ----- Incremental native control mutation (see core/RUNTIME_CONTRACT.md) -----
    // Mutates the EXISTING HWND registered under an id/bind/export name. Never
    // recreates a control and never calls build().
    // Generic string API:
    bool set(const std::string& name, const std::string& property, const std::string& value);
    std::string get(const std::string& name, const std::string& property) const;
    // Ergonomic typed helpers:
    bool set_text(const std::string& name, const std::string& text);
    bool set_visible(const std::string& name, bool visible);
    bool set_enabled(const std::string& name, bool enabled);
    bool set_checked(const std::string& name, bool checked);
    bool set_value(const std::string& name, int value);
    bool set_selected(const std::string& name, int index);
    bool set_color(const std::string& name, unsigned long foreground, unsigned long background);
    bool set_size(const std::string& name, int width, int height);
    bool set_focus(const std::string& name);
    std::string get_text(const std::string& name) const;
    bool get_checked(const std::string& name) const;
    int get_value(const std::string& name) const;
    int get_selected(const std::string& name) const;

    // ----- source= collection binding (ZU-67 / ZU-87). Records are string maps
    // keyed by "key"; a table row's other fields match <column field=>, a
    // list/tree item uses "text". Never rebuilds. -----
    void set_rows(const std::string& name, const std::vector<Record>& records);
    void append_row(const std::string& name, const Record& record);
    void remove_row(const std::string& name, const std::string& key);
    void update_row(const std::string& name, const std::string& key, const Record& patch);
    void clear_rows(const std::string& name);
    std::vector<std::string> row_keys(const std::string& name) const;
    std::vector<std::string> get_selection(const std::string& name) const;
    void set_selection(const std::string& name, const std::vector<std::string>& keys);

    /// Appends a line to a `console` control and scrolls to the bottom.
    void append(const std::string& name, const std::string& text);

    // ----- Context menus (ZU-80 parity) -----
    /// Pops a context menu at the cursor for the named control; routes each
    /// item's click to its channel/payload.
    void popup_menu(const std::string& name, const std::vector<MenuItem>& items);
    /// Enables/checks a menu-bar item addressed by its "Menu/Item" path.
    void set_menu_enabled(const std::string& path, bool enabled);
    void set_menu_checked(const std::string& path, bool checked);

    // ----- Media (ZU-82 parity) -----
    /// Sets an `image` control's picture from a file path (BMP/PNG/JPG via GDI+).
    void set_image(const std::string& name, const std::string& path);

    // ----- Canvas overlay (ZU-86 parity) -----
    using PaintFn = std::function<void(void* hdc, int width, int height)>;
    void on_paint(const std::string& name, PaintFn draw);
    void redraw(const std::string& name);

private:
    struct RowStore {
        std::vector<std::string> fields;
        std::vector<std::string> order;
        std::unordered_map<std::string, Record> records;
        bool virtualized = false;
        bool templated = false;
    };
    RowStore* store_for(const std::string& name);
    const RowStore* store_for(const std::string& name) const;
    void render_rows(const std::string& name);
    std::vector<std::string> get_selection_for(void* control) const;
    void* create_node(void* parent, const Node& node, int& x, int& y, int width);
    void dispatch(const std::string& channel, const std::string& payload);
    void* require(const std::string& name) const;
    void apply_bound_value(const std::string& control_name, const std::string& value);
    std::string kind_of(void* control) const;
    friend class State;
    static long long subclass_proc(void*, unsigned, unsigned long long, long long, unsigned long long, unsigned long long);

    struct ControlColor { unsigned long fg = 0xffffffff; unsigned long bg = 0xffffffff; void* brush = nullptr; };

    void* parent_ = nullptr;
    int build_count_ = 0;
    Theme theme_{};
    State state_;
    std::unordered_map<std::string, std::vector<MessageHandler>> handlers_;
    std::unordered_map<void*, std::string> control_channels_;
    std::unordered_map<void*, std::string> control_kinds_;
    std::unordered_map<void*, std::string> control_binds_;
    std::unordered_map<void*, std::string> control_activate_;
    std::unordered_map<void*, std::string> control_commit_;
    std::unordered_map<void*, std::string> control_context_;
    std::unordered_map<void*, std::string> control_drop_;
    std::unordered_set<void*> drag_sources_;
    std::unordered_map<void*, std::vector<std::string>> option_values_;
    std::unordered_map<void*, std::string> control_icons_;
    std::unordered_map<void*, void*> images_;         // HWND -> Gdiplus::Image*
    std::unordered_map<void*, PaintFn> painters_;
    std::unordered_map<void*, ControlColor> control_colors_;
    std::unordered_map<void*, RowStore> rows_;
    std::unordered_map<std::string, void*> exports_;
    std::unordered_map<std::string, std::pair<void*, unsigned>> menu_paths_;  // "File/New" -> {HMENU, id}
    std::unordered_map<unsigned, std::pair<std::string, std::string>> menu_actions_;  // id -> {channel, payload}
    std::unordered_map<void*, std::vector<std::pair<std::string, void*>>> tabs_;  // tab HWND -> [{id, panel HWND}]
    void* menu_bar_ = nullptr;
    unsigned menu_next_id_ = 40000;
    std::vector<std::string> drag_keys_;
    void* drag_from_ = nullptr;
    std::string payload_for(void* control) const;
    void build_menu(void* hmenu, const Node& node, const std::string& path);
    unsigned add_menu_spec(void* hmenu, const MenuItem& item);
    void load_image(void* control, const std::string& path);
    void select_tab(void* tab_control, const std::string& id);
    std::string name_of(void* control) const;
    void custom_draw_list(void* nmlvcd, long long& result);
    void custom_draw_tree(void* nmtvcd, long long& result);
    void draw_owner_list_item(void* drawitemstruct);
    void draw_owner_button(void* drawitemstruct);
};

std::string make_envelope(const std::string& channel, const std::string& payload_json);
bool parse_envelope(const std::string& raw, std::string& channel_out, std::string& payload_out);

}  // namespace zui
#endif
