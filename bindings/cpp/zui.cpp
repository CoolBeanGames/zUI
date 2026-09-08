#include "zui.h"

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <windowsx.h>
#include <commctrl.h>
#include <shellapi.h>
#include <objidl.h>
#include <gdiplus.h>
#include <algorithm>
#include <cassert>
#include <cmath>

#pragma comment(lib, "gdiplus.lib")
#pragma comment(lib, "shell32.lib")
#pragma comment(lib, "comctl32.lib")

namespace zui {

Theme Theme::holo() { return Theme{}; }
Theme Theme::clean() {
    Theme t;
    t.window = 0x00f5f3f1; t.surface = 0x00ffffff; t.raised = 0x00faf9f8;
    t.text = 0x00282420; t.muted = 0x00736b64; t.accent = 0x00d47800;
    t.border = 0x00dbd7d3; t.warn = 0x00006a9a; t.error = 0x002b39c0; t.ok = 0x00327d2e;
    return t;
}

namespace {

ULONG_PTR g_gdiplus_token = 0;
void ensure_gdiplus() {
    if (g_gdiplus_token) return;
    Gdiplus::GdiplusStartupInput input;
    Gdiplus::GdiplusStartup(&g_gdiplus_token, &input, nullptr);
}

COLORREF blend(COLORREF a, COLORREF b, double t) {
    auto mix = [&](int shift) {
        int av = (a >> shift) & 0xff, bv = (b >> shift) & 0xff;
        return static_cast<int>(av + (bv - av) * t) & 0xff;
    };
    return RGB(mix(0), mix(8), mix(16));
}

// Background/foreground for a non-default row state. Returns false for "normal".
bool row_state_colors(const Theme& th, const std::string& state, COLORREF& bg, COLORREF& fg) {
    if (state == "warn")   { bg = blend(th.surface, th.warn, 0.22);  fg = th.text; return true; }
    if (state == "error")  { bg = blend(th.surface, th.error, 0.24); fg = th.text; return true; }
    if (state == "active") { bg = blend(th.surface, th.accent, 0.28); fg = th.text; return true; }
    return false;
}

// Monochrome line-icon set, mirroring the C# ZuiIcons (ZU-84 parity).
void draw_icon(HDC dc, const std::string& name, RECT r, COLORREF color) {
    int w = r.right - r.left, h = r.bottom - r.top;
    float x = static_cast<float>(r.left), y = static_cast<float>(r.top);
    float cx = x + w / 2.f, cy = y + h / 2.f, pad = w * 0.24f;
    ensure_gdiplus();
    Gdiplus::Graphics g(dc);
    g.SetSmoothingMode(Gdiplus::SmoothingModeAntiAlias);
    Gdiplus::Color c(GetRValue(color), GetGValue(color), GetBValue(color));
    Gdiplus::Pen pen(c, std::max(1.4f, w / 12.f));
    pen.SetStartCap(Gdiplus::LineCapRound); pen.SetEndCap(Gdiplus::LineCapRound);
    Gdiplus::SolidBrush br(c);
    if (name == "play") {
        Gdiplus::PointF pts[] = {{x + pad, y + pad}, {x + w - pad, cy}, {x + pad, y + h - pad}};
        g.FillPolygon(&br, pts, 3);
    } else if (name == "pause") {
        g.FillRectangle(&br, x + pad, y + pad, w * 0.18f, h - 2 * pad);
        g.FillRectangle(&br, x + w - pad - w * 0.18f, y + pad, w * 0.18f, h - 2 * pad);
    } else if (name == "stop") {
        g.FillRectangle(&br, x + pad, y + pad, w - 2 * pad, h - 2 * pad);
    } else if (name == "prev" || name == "next") {
        float dir = name == "prev" ? -1.f : 1.f;
        Gdiplus::PointF pts[] = {{cx + dir * (w * 0.18f), cy}, {cx - dir * (w * 0.18f), y + pad}, {cx - dir * (w * 0.18f), y + h - pad}};
        g.FillPolygon(&br, pts, 3);
        g.FillRectangle(&br, cx + dir * (w * 0.18f) - (dir < 0 ? w * 0.14f : 0), y + pad, w * 0.14f, h - 2 * pad);
    } else if (name == "add") {
        g.DrawLine(&pen, cx, y + pad, cx, y + h - pad);
        g.DrawLine(&pen, x + pad, cy, x + w - pad, cy);
    } else if (name == "remove") {
        g.DrawLine(&pen, x + pad, cy, x + w - pad, cy);
    } else if (name == "close") {
        g.DrawLine(&pen, x + pad, y + pad, x + w - pad, y + h - pad);
        g.DrawLine(&pen, x + w - pad, y + pad, x + pad, y + h - pad);
    } else if (name == "check") {
        Gdiplus::PointF pts[] = {{x + pad, cy}, {cx - w * 0.05f, y + h - pad}, {x + w - pad, y + pad}};
        g.DrawLines(&pen, pts, 3);
    } else if (name == "chevron-right" || name == "chevron-down") {
        bool down = name == "chevron-down";
        Gdiplus::PointF pts[3];
        if (down) { pts[0] = {x + pad, cy - h * 0.1f}; pts[1] = {cx, cy + h * 0.15f}; pts[2] = {x + w - pad, cy - h * 0.1f}; }
        else { pts[0] = {cx - w * 0.1f, y + pad}; pts[1] = {cx + w * 0.15f, cy}; pts[2] = {cx - w * 0.1f, y + h - pad}; }
        g.DrawLines(&pen, pts, 3);
    } else if (name == "dot") {
        g.FillEllipse(&br, cx - w * 0.16f, cy - w * 0.16f, w * 0.32f, w * 0.32f);
    } else if (name == "search") {
        g.DrawEllipse(&pen, x + pad, y + pad, w * 0.45f, h * 0.45f);
        g.DrawLine(&pen, x + pad + w * 0.42f, y + pad + h * 0.42f, x + w - pad, y + h - pad);
    } else if (name == "gear") {
        g.DrawEllipse(&pen, cx - w * 0.22f, cy - w * 0.22f, w * 0.44f, w * 0.44f);
        for (int i = 0; i < 8; ++i) {
            double a = i * 3.14159265 / 4;
            g.DrawLine(&pen, cx + (float)std::cos(a) * w * 0.24f, cy + (float)std::sin(a) * w * 0.24f,
                       cx + (float)std::cos(a) * w * 0.36f, cy + (float)std::sin(a) * w * 0.36f);
        }
    } else if (name == "download") {
        g.DrawLine(&pen, cx, y + pad, cx, y + h * 0.62f);
        Gdiplus::PointF pts[] = {{cx - w * 0.15f, y + h * 0.45f}, {cx, y + h * 0.66f}, {cx + w * 0.15f, y + h * 0.45f}};
        g.DrawLines(&pen, pts, 3);
        g.DrawLine(&pen, x + pad, y + h - pad, x + w - pad, y + h - pad);
    } else if (name == "refresh") {
        g.DrawArc(&pen, x + pad, y + pad, w - 2 * pad, h - 2 * pad, 40.f, 280.f);
    } else {
        g.DrawRectangle(&pen, x + pad, y + pad, w - 2 * pad, h - 2 * pad);
    }
}

std::wstring wide(const std::string& value) {
    if (value.empty()) return {};
    int size = MultiByteToWideChar(CP_UTF8, 0, value.data(), static_cast<int>(value.size()), nullptr, 0);
    std::wstring result(size, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, value.data(), static_cast<int>(value.size()), result.data(), size);
    return result;
}

std::string narrow(const std::wstring& value) {
    if (value.empty()) return {};
    int size = WideCharToMultiByte(CP_UTF8, 0, value.data(), static_cast<int>(value.size()), nullptr, 0, nullptr, nullptr);
    std::string result(size, '\0');
    WideCharToMultiByte(CP_UTF8, 0, value.data(), static_cast<int>(value.size()), result.data(), size, nullptr, nullptr);
    return result;
}

bool truthy(const std::string& v) { return v == "true" || v == "1" || v == "on" || v == "yes"; }

std::string json_array(const std::vector<std::string>& items) {
    std::string out = "[";
    for (size_t i = 0; i < items.size(); ++i) {
        if (i) out += ',';
        out += '"';
        for (char c : items[i]) { if (c == '"' || c == '\\') out += '\\'; out += c; }
        out += '"';
    }
    return out + "]";
}

std::string record_key(const Record& r) {
    auto it = r.find("key");
    return it == r.end() ? std::string{} : it->second;
}

std::string attr(const Node& n, const char* key, const char* fallback = "") {
    auto it = n.attributes.find(key);
    return it == n.attributes.end() ? fallback : it->second;
}

int number(const Node& n, const char* key, int fallback) {
    auto value = attr(n, key);
    if (value.empty()) return fallback;
    try { return std::stoi(value); } catch (...) { return fallback; }
}
}

Host::Host(void* native_parent) : parent_(native_parent) {
    state_.host_ = this;
    INITCOMMONCONTROLSEX init{sizeof(init), ICC_STANDARD_CLASSES | ICC_BAR_CLASSES | ICC_LISTVIEW_CLASSES};
    InitCommonControlsEx(&init);
    SetWindowSubclass(static_cast<HWND>(parent_), reinterpret_cast<SUBCLASSPROC>(&Host::subclass_proc), 1,
                      reinterpret_cast<DWORD_PTR>(this));
}

Host::~Host() {
    if (parent_) RemoveWindowSubclass(static_cast<HWND>(parent_), reinterpret_cast<SUBCLASSPROC>(&Host::subclass_proc), 1);
    for (auto& [hwnd, color] : control_colors_)
        if (color.brush) DeleteObject(static_cast<HGDIOBJ>(color.brush));
    for (auto& [hwnd, img] : images_) if (img) delete static_cast<Gdiplus::Image*>(img);
    if (menu_bar_) DestroyMenu(static_cast<HMENU>(menu_bar_));
}

// build() constructs the native control tree from compiler-emitted nodes. It is a
// construction operation only: call it once per screen. It is NOT a render,
// refresh, or update pass, and it destroys any controls, selection, and focus
// from a previous call. To change text, values, visibility, selection, or
// collection contents after construction, mutate the existing controls via
// find() / the incremental mutation API. See core/RUNTIME_CONTRACT.md.
void Host::build(const Node& root) {
    if (++build_count_ > 1) {
        OutputDebugStringW(L"[zUI] Host::build() called more than once on the same host. "
                           L"build() is construction-only and destroys existing controls, "
                           L"selection, and focus. Mutate existing controls via find()/"
                           L"set_text()/set_value()/collection updates instead. "
                           L"See core/RUNTIME_CONTRACT.md.\n");
        assert(build_count_ == 1 && "zUI: Host::build() is construction-only; see core/RUNTIME_CONTRACT.md");
    }
    HWND parent = static_cast<HWND>(parent_);
    for (HWND child = GetWindow(parent, GW_CHILD); child;) {
        HWND next = GetWindow(child, GW_HWNDNEXT);
        DestroyWindow(child);
        child = next;
    }
    control_channels_.clear();
    control_kinds_.clear();
    control_binds_.clear();
    control_activate_.clear();
    control_commit_.clear();
    control_context_.clear();
    control_drop_.clear();
    control_icons_.clear();
    drag_sources_.clear();
    option_values_.clear();
    for (auto& [hwnd, img] : images_) if (img) delete static_cast<Gdiplus::Image*>(img);
    images_.clear();
    painters_.clear();
    tabs_.clear();
    menu_paths_.clear();
    menu_actions_.clear();
    if (menu_bar_) { HWND top = GetAncestor(parent, GA_ROOT); if (top) SetMenu(top, nullptr); DestroyMenu(static_cast<HMENU>(menu_bar_)); menu_bar_ = nullptr; }
    rows_.clear();
    for (auto& [hwnd, color] : control_colors_)
        if (color.brush) DeleteObject(static_cast<HGDIOBJ>(color.brush));
    control_colors_.clear();
    exports_.clear();
    RECT client{}; GetClientRect(parent, &client);
    int x = 12, y = 12;
    for (const auto& child : root.children) create_node(parent, child, x, y, std::max(200L, client.right - 24));
}

void* Host::create_node(void* raw_parent, const Node& node, int& x, int& y, int width) {
    HWND parent = static_cast<HWND>(raw_parent);
    auto text = wide(node.text);
    const wchar_t* klass = L"STATIC";
    DWORD style = WS_CHILD | WS_VISIBLE;
    int height = 28;
    bool container = false;

    bool icon_button = node.kind == "button" && (node.attributes.count("icon") || attr(node, "kind") == "icon");
    if (node.kind == "button") {
        klass = L"BUTTON";
        style |= WS_TABSTOP | (icon_button ? BS_OWNERDRAW : BS_PUSHBUTTON);
    }
    else if (node.kind == "check") { klass = L"BUTTON"; style |= BS_AUTOCHECKBOX | WS_TABSTOP; }
    else if (node.kind == "image" || node.kind == "canvas" || node.kind == "overlay") {
        klass = L"STATIC"; style |= SS_OWNERDRAW;
        height = number(node, "height", node.kind == "image" ? 60 : 48);
    }
    else if (node.kind == "number") { klass = L"EDIT"; style |= WS_BORDER | WS_TABSTOP | ES_NUMBER | ES_RIGHT; }
    else if (node.kind == "console") {
        klass = L"EDIT";
        style |= WS_BORDER | ES_MULTILINE | ES_AUTOVSCROLL | ES_READONLY | WS_VSCROLL | WS_HSCROLL;
        height = 140;
    }
    else if (node.kind == "input" || node.kind == "textarea") {
        klass = L"EDIT"; style |= WS_BORDER | WS_TABSTOP | ES_AUTOHSCROLL;
        if (node.kind == "textarea") { style |= ES_MULTILINE | ES_AUTOVSCROLL | WS_VSCROLL; height = 90; }
    }
    else if (node.kind == "list") {
        klass = WC_LISTVIEWW; style |= LVS_REPORT | LVS_NOCOLUMNHEADER | WS_BORDER | WS_TABSTOP; height = 240;
        if (node.attributes.count("selectable") == 0) style |= LVS_SINGLESEL;
        if (node.attributes.count("template")) style |= LVS_OWNERDRAWFIXED;
        if (node.attributes.count("virtual")) style |= LVS_OWNERDATA;
    }
    else if (node.kind == "slider") { klass = TRACKBAR_CLASSW; style |= TBS_HORZ | WS_TABSTOP; height = 36; }
    else if (node.kind == "progress" || node.kind == "spinner" || node.kind == "loading") { klass = PROGRESS_CLASSW; height = 16; }
    else if (node.kind == "select" || node.kind == "dropdown") { klass = WC_COMBOBOXW; style |= CBS_DROPDOWNLIST | WS_TABSTOP; height = 180; }
    else if (node.kind == "table") {
        klass = WC_LISTVIEWW; style |= LVS_REPORT | WS_BORDER | WS_TABSTOP; height = 300;
        if (node.attributes.count("selectable") == 0) style |= LVS_SINGLESEL;
        if (node.attributes.count("virtual")) style |= LVS_OWNERDATA;
    }
    else if (node.kind == "tree") { klass = WC_TREEVIEWW; style |= TVS_HASLINES | TVS_LINESATROOT | WS_BORDER | WS_TABSTOP; height = 300; }
    else if (node.kind == "tabs") { klass = WC_TABCONTROLW; style |= WS_TABSTOP; container = true; height = 260; }
    else if (node.kind == "window" || node.kind == "root" || node.kind == "col" || node.kind == "fill" ||
             node.kind == "workspace" || node.kind == "row" || node.kind == "panel" || node.kind == "sidebar" ||
             node.kind == "statusbar" || node.kind == "contextbar" || node.kind == "nav" ||
             node.kind == "grid" || node.kind == "scroll" || node.kind == "splitter" ||
             node.kind == "tabpanel" || node.kind == "menubar") {
        klass = L"STATIC"; style |= SS_LEFT; container = true; height = 4;
        if (node.kind == "scroll") style |= WS_VSCROLL;
        if (node.kind == "window" && !text.empty()) SetWindowTextW(static_cast<HWND>(parent_), text.c_str());
    }

    HWND control = CreateWindowExW(0, klass, text.c_str(), style, x, y, width, height,
                                   parent, nullptr, GetModuleHandleW(nullptr), nullptr);
    if (!control) return nullptr;
    SendMessageW(control, WM_SETFONT, reinterpret_cast<WPARAM>(GetStockObject(DEFAULT_GUI_FONT)), TRUE);
    if (node.kind == "slider") {
        SendMessageW(control, TBM_SETRANGE, TRUE, MAKELONG(number(node, "min", 0), number(node, "max", 100)));
        SendMessageW(control, TBM_SETPOS, TRUE, number(node, "value", 0));
    }
    if (node.kind == "progress") SendMessageW(control, PBM_SETPOS, number(node, "value", 0), 0);
    if (node.kind == "number") SetWindowTextW(control, std::to_wstring(number(node, "value", number(node, "min", 0))).c_str());
    if (node.kind == "console" || node.kind == "input" || node.kind == "textarea" || node.kind == "list" || node.kind == "table" || node.kind == "tree") {
        if (node.kind == "console")
            SendMessageW(control, WM_SETFONT, reinterpret_cast<WPARAM>(GetStockObject(ANSI_FIXED_FONT)), TRUE);
    }
    if (node.kind == "table" || node.kind == "list") {
        ListView_SetExtendedListViewStyle(control, LVS_EX_FULLROWSELECT | LVS_EX_DOUBLEBUFFER);
        auto& store = rows_[control];
        store.virtualized = node.attributes.count("virtual") > 0;
        store.templated = node.attributes.count("template") > 0;
        int index = 0;
        for (const auto& col : node.children) if (col.kind == "column") {
            auto heading = wide(col.text); LVCOLUMNW spec{LVCF_TEXT | LVCF_WIDTH, 0, 150, heading.data()};
            ListView_InsertColumn(control, index++, &spec);
            store.fields.push_back(attr(col, "field", col.text.c_str()));
        }
        if (node.kind == "list" && store.fields.empty()) {
            LVCOLUMNW spec{LVCF_WIDTH, 0, 240, nullptr};
            ListView_InsertColumn(control, 0, &spec);
            store.fields.push_back("text");
        }
    }
    if (node.kind == "tree") rows_[control];
    if (node.kind == "tabs") {
        int ti = 0;
        for (const auto& tp : node.children) if (tp.kind == "tabpanel") {
            auto label = wide(tp.text.empty() ? ("Tab " + std::to_string(ti + 1)) : tp.text);
            TCITEMW item{TCIF_TEXT, 0, 0, label.data()}; SendMessageW(control, TCM_INSERTITEMW, ti++, reinterpret_cast<LPARAM>(&item));
        }
    }
    if (node.kind == "image") {
        if (auto s = node.attributes.find("src"); s != node.attributes.end() && !s->second.empty()) load_image(control, s->second);
        else if (auto p = node.attributes.find("placeholder"); p != node.attributes.end()) load_image(control, p->second);
    }
    if (auto ic = node.attributes.find("icon"); ic != node.attributes.end()) control_icons_[control] = ic->second;
    if (node.kind == "select" || node.kind == "dropdown") {
        auto& values = option_values_[control];
        for (const auto& item : node.children) if (item.kind == "option" || item.kind == "item") {
            auto value = wide(item.text); SendMessageW(control, CB_ADDSTRING, 0, reinterpret_cast<LPARAM>(value.c_str()));
            values.push_back(attr(item, "value", item.text.c_str()));
        }
        SendMessageW(control, CB_SETCURSEL, 0, 0);
    }
    if (node.kind == "tree") {
        for (const auto& item : node.children) {
            auto value = wide(item.text); TVINSERTSTRUCTW insert{}; insert.hParent = TVI_ROOT; insert.hInsertAfter = TVI_LAST;
            insert.item.mask = TVIF_TEXT; insert.item.pszText = value.data(); TreeView_InsertItem(control, &insert);
        }
    }

    auto event = node.attributes.find("on");
    if (event != node.attributes.end()) control_channels_[control] = event->second;
    if (auto a = node.attributes.find("onactivate"); a != node.attributes.end()) control_activate_[control] = a->second;
    if (auto c = node.attributes.find("oncommit"); c != node.attributes.end()) control_commit_[control] = c->second;
    if (auto x = node.attributes.find("oncontext"); x != node.attributes.end()) control_context_[control] = x->second;
    if (auto d = node.attributes.find("ondrop"); d != node.attributes.end()) { control_drop_[control] = d->second; DragAcceptFiles(control, TRUE); }
    if (node.attributes.count("dragsource")) drag_sources_.insert(control);
    control_kinds_[control] = node.kind;
    // Register every lookup name. Priority is export > bind > id (a more specific
    // name wins), but all three resolve to this control.
    for (const char* key : {"id", "bind", "export"}) {
        auto name = attr(node, key);
        if (!name.empty()) exports_[name] = control;
    }

    if (node.kind == "menubar") {
        HMENU bar = CreateMenu();
        for (const auto& m : node.children) build_menu(bar, m, "");
        HWND top = GetAncestor(parent, GA_ROOT);
        if (top && (GetWindowLongPtrW(top, GWL_STYLE) & WS_CHILD) == 0) {
            SetMenu(top, bar); DrawMenuBar(top); menu_bar_ = bar;
        } else {
            DestroyMenu(bar);  // embedded panel: no menu bar, matches the C# host
        }
        y += 4;
        return control;
    }

    int child_x = x + (container ? 8 : 0), child_y = y + height + 4;
    if (node.kind == "tabs") {
        RECT disp{0, 0, width, height};
        SendMessageW(control, TCM_ADJUSTRECT, FALSE, reinterpret_cast<LPARAM>(&disp));
        auto& panels = tabs_[control];
        int pi = 0;
        for (const auto& tp : node.children) {
            if (tp.kind != "tabpanel") continue;
            HWND panel = CreateWindowExW(0, L"STATIC", L"", WS_CHILD | (pi == 0 ? WS_VISIBLE : 0) | SS_LEFT,
                                         x + disp.left, y + disp.top, disp.right - disp.left, disp.bottom - disp.top,
                                         parent, nullptr, GetModuleHandleW(nullptr), nullptr);
            std::string id = attr(tp, "id");
            if (id.empty()) id = attr(tp, "value", std::to_string(pi).c_str());
            panels.push_back({id, panel});
            int px = 8, py = 8;
            for (const auto& c : tp.children) create_node(panel, c, px, py, disp.right - disp.left - 20);
            ++pi;
        }
        SetWindowPos(control, HWND_BOTTOM, x, y, width, height, SWP_NOACTIVATE);
        y += height + 8;
        return control;
    }
    if (node.kind == "grid") {
        int cols = std::max(1, number(node, "cols", 2));
        int gap = number(node, "gap", 8);
        int cell_w = (width - gap * (cols - 1)) / cols;
        int col = 0, row_y = child_y, row_h = 0;
        for (const auto& child : node.children) {
            int cx = child_x + col * (cell_w + gap), cy = row_y, dummy_y = cy;
            create_node(parent, child, cx, dummy_y, cell_w);
            row_h = std::max(row_h, dummy_y - cy);
            if (++col >= cols) { col = 0; row_y += row_h + gap; row_h = 0; }
        }
        height = std::max(height, (col ? row_y + row_h : row_y) - y);
        SetWindowPos(control, HWND_BOTTOM, x, y, width, height, SWP_NOACTIVATE);
        y += height + 8;
        return control;
    }
    if (node.kind == "splitter") {
        int pos = number(node, "pos", number(node, "width", width / 2));
        int panes = 0; std::vector<const Node*> kids;
        for (const auto& c : node.children) if (c.kind != "column" && c.kind != "option") kids.push_back(&c);
        int split_h = number(node, "height", 260);
        for (size_t k = 0; k < kids.size() && k < 2; ++k) {
            int px = (k == 0) ? child_x : child_x + pos + 6;
            int pw = (k == 0) ? pos : width - pos - 6;
            int cx = px, cy = y + 4;
            for (const auto& c : kids[k]->children) create_node(parent, c, cx, cy, pw - 8);
            split_h = std::max(split_h, cy - (y + 4));
        }
        height = split_h + 8;
        SetWindowPos(control, HWND_BOTTOM, x, y, width, height, SWP_NOACTIVATE);
        y += height + 8;
        return control;
    }
    if (container) {
        void* host_parent = (node.kind == "scroll") ? static_cast<void*>(control) : raw_parent;
        for (const auto& child : node.children) create_node(host_parent, child, child_x, child_y, std::max(160, width - 16));
        height = std::max(height, child_y - y);
        SetWindowPos(control, HWND_BOTTOM, x, y, width, height, SWP_NOACTIVATE);
    }
    y += height + 8;
    return control;
}

void Host::on(const std::string& channel, MessageHandler handler) { handlers_[channel].push_back(std::move(handler)); }
void Host::send(const std::string& channel, const std::string& payload) { dispatch(channel, payload); }
void Host::set_theme(const std::string& name) {
    theme_ = (name == "clean") ? Theme::clean() : Theme::holo();
    for (auto& [raw, store] : rows_) {
        HWND hwnd = static_cast<HWND>(raw);
        std::string k = kind_of(raw);
        if (k == "table" || k == "list") {
            ListView_SetBkColor(hwnd, theme_.surface);
            ListView_SetTextColor(hwnd, theme_.text);
            ListView_SetTextBkColor(hwnd, theme_.surface);
        } else if (k == "tree") {
            TreeView_SetBkColor(hwnd, theme_.raised);
            TreeView_SetTextColor(hwnd, theme_.text);
        }
        InvalidateRect(hwnd, nullptr, TRUE);
    }
    dispatch("theme-changed", name);
    InvalidateRect(static_cast<HWND>(parent_), nullptr, TRUE);
}
void* Host::find(const std::string& name) const { auto it = exports_.find(name); return it == exports_.end() ? nullptr : it->second; }

void* Host::require(const std::string& name) const {
    auto it = exports_.find(name);
    if (it == exports_.end() || !it->second) {
        OutputDebugStringW((L"[zUI] no control registered as '" + wide(name) + L"'\n").c_str());
        return nullptr;
    }
    return it->second;
}

bool Host::set(const std::string& name, const std::string& property, const std::string& value) {
    HWND h = static_cast<HWND>(require(name));
    if (!h) return false;
    std::string kind;
    if (auto k = control_kinds_.find(h); k != control_kinds_.end()) kind = k->second;
    int n = 0; try { n = std::stoi(value); } catch (...) { n = 0; }

    if (property == "text") { SetWindowTextW(h, wide(value).c_str()); return true; }
    if (property == "visible") { ShowWindow(h, truthy(value) ? SW_SHOW : SW_HIDE); return true; }
    if (property == "enabled") { EnableWindow(h, truthy(value) ? TRUE : FALSE); return true; }
    if (property == "focus") { SetFocus(h); return true; }
    if (property == "width" || property == "height") {
        RECT r{}; GetWindowRect(h, &r);
        int w = r.right - r.left, ht = r.bottom - r.top;
        if (property == "width") w = n; else ht = n;
        SetWindowPos(h, nullptr, 0, 0, w, ht, SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE);
        return true;
    }
    if (property == "checked") { SendMessageW(h, BM_SETCHECK, truthy(value) ? BST_CHECKED : BST_UNCHECKED, 0); return true; }
    if (property == "value") {
        if (kind == "slider") { SendMessageW(h, TBM_SETPOS, TRUE, n); return true; }
        if (kind == "progress" || kind == "spinner" || kind == "loading") { SendMessageW(h, PBM_SETPOS, n, 0); return true; }
        if (kind == "input" || kind == "textarea") { SetWindowTextW(h, wide(value).c_str()); return true; }
        return false;
    }
    if (property == "selected" || property == "selectedindex") {
        if (kind == "tabs") { select_tab(h, value); return true; }
        if (kind == "select" || kind == "dropdown") { SendMessageW(h, CB_SETCURSEL, n, 0); return true; }
        if (kind == "table" || kind == "list") {
            ListView_SetItemState(h, -1, 0, LVIS_SELECTED | LVIS_FOCUSED);
            if (n >= 0) { ListView_SetItemState(h, n, LVIS_SELECTED | LVIS_FOCUSED, LVIS_SELECTED | LVIS_FOCUSED);
                          ListView_EnsureVisible(h, n, FALSE); }
            return true;
        }
        return false;
    }
    if ((property == "selectedvalue" || property == "selectedtext") &&
        (kind == "select" || kind == "dropdown")) {
        if (auto it = option_values_.find(h); it != option_values_.end()) {
            for (int i = 0; i < static_cast<int>(it->second.size()); ++i)
                if (it->second[i] == value) { SendMessageW(h, CB_SETCURSEL, i, 0); return true; }
        }
        int idx = static_cast<int>(SendMessageW(h, CB_FINDSTRINGEXACT, static_cast<WPARAM>(-1),
                                                reinterpret_cast<LPARAM>(wide(value).c_str())));
        if (idx >= 0) { SendMessageW(h, CB_SETCURSEL, idx, 0); return true; }
        return false;
    }
    if (property == "selection") return false;  // use set_selection(name, keys)
    if (property == "selected" && kind == "tabs") { select_tab(h, value); return true; }
    if (property == "source" && kind == "image") { load_image(h, value); return true; }
    if (property == "attention") {
        control_colors_[h].fg = truthy(value) ? theme_.accent : 0xffffffff;
        InvalidateRect(h, nullptr, TRUE); return true;
    }
    if (property == "fg" || property == "foreground") {
        control_colors_[h].fg = static_cast<unsigned long>(std::stoul(value, nullptr, 0));
        InvalidateRect(h, nullptr, TRUE); return true;
    }
    if (property == "bg" || property == "background") {
        auto& cc = control_colors_[h];
        cc.bg = static_cast<unsigned long>(std::stoul(value, nullptr, 0));
        if (cc.brush) DeleteObject(static_cast<HGDIOBJ>(cc.brush));
        cc.brush = CreateSolidBrush(cc.bg);
        InvalidateRect(h, nullptr, TRUE); return true;
    }
    return false;
}

std::string Host::get(const std::string& name, const std::string& property) const {
    HWND h = static_cast<HWND>(const_cast<Host*>(this)->require(name));
    if (!h) return {};
    std::string kind;
    if (auto k = control_kinds_.find(h); k != control_kinds_.end()) kind = k->second;
    if (property == "text" || (property == "value" && (kind == "input" || kind == "textarea"))) {
        int len = GetWindowTextLengthW(h);
        std::wstring buf(len + 1, L'\0');
        GetWindowTextW(h, buf.data(), len + 1);
        buf.resize(len);
        return narrow(buf);
    }
    if (property == "visible") return IsWindowVisible(h) ? "true" : "false";
    if (property == "enabled") return IsWindowEnabled(h) ? "true" : "false";
    if (property == "checked") return SendMessageW(h, BM_GETCHECK, 0, 0) == BST_CHECKED ? "true" : "false";
    if (property == "value") {
        if (kind == "slider") return std::to_string(static_cast<int>(SendMessageW(h, TBM_GETPOS, 0, 0)));
        if (kind == "progress" || kind == "spinner" || kind == "loading")
            return std::to_string(static_cast<int>(SendMessageW(h, PBM_GETPOS, 0, 0)));
        return {};
    }
    if (property == "selected" || property == "selectedindex") {
        if (kind == "select" || kind == "dropdown")
            return std::to_string(static_cast<int>(SendMessageW(h, CB_GETCURSEL, 0, 0)));
        if (kind == "table" || kind == "list")
            return std::to_string(ListView_GetNextItem(h, -1, LVNI_SELECTED));
        return {};
    }
    if (property == "selectedvalue" || property == "selectedtext") {
        if (kind == "select" || kind == "dropdown") {
            int i = static_cast<int>(SendMessageW(h, CB_GETCURSEL, 0, 0));
            if (auto it = option_values_.find(h); it != option_values_.end() && i >= 0 && i < static_cast<int>(it->second.size()))
                return it->second[i];
        }
        return {};
    }
    if (property == "selection") return json_array(get_selection_for(h));
    return {};
}

bool Host::set_text(const std::string& name, const std::string& text) { return set(name, "text", text); }
bool Host::set_visible(const std::string& name, bool v) { return set(name, "visible", v ? "true" : "false"); }
bool Host::set_enabled(const std::string& name, bool v) { return set(name, "enabled", v ? "true" : "false"); }
bool Host::set_checked(const std::string& name, bool v) { return set(name, "checked", v ? "true" : "false"); }
bool Host::set_value(const std::string& name, int v) { return set(name, "value", std::to_string(v)); }
bool Host::set_selected(const std::string& name, int i) { return set(name, "selected", std::to_string(i)); }
bool Host::set_color(const std::string& name, unsigned long fg, unsigned long bg) {
    bool ok = set(name, "fg", std::to_string(fg));
    return set(name, "bg", std::to_string(bg)) || ok;
}
bool Host::set_size(const std::string& name, int w, int h) {
    bool ok = set(name, "width", std::to_string(w));
    return set(name, "height", std::to_string(h)) && ok;
}
bool Host::set_focus(const std::string& name) { return set(name, "focus", "true"); }
std::string Host::get_text(const std::string& name) const { return get(name, "text"); }
bool Host::get_checked(const std::string& name) const { return get(name, "checked") == "true"; }
int Host::get_value(const std::string& name) const { auto v = get(name, "value"); try { return std::stoi(v); } catch (...) { return 0; } }
int Host::get_selected(const std::string& name) const { auto v = get(name, "selected"); try { return std::stoi(v); } catch (...) { return -1; } }

std::string Host::kind_of(void* control) const {
    auto it = control_kinds_.find(control);
    return it == control_kinds_.end() ? std::string{} : it->second;
}

std::string Host::name_of(void* control) const {
    for (const auto& [name, hwnd] : exports_) if (hwnd == control) return name;
    return {};
}

// Normalized `on` payload for a control (see core/PROTOCOL.md).
std::string Host::payload_for(void* raw) const {
    HWND h = static_cast<HWND>(raw);
    std::string kind = kind_of(raw);
    if (kind == "input" || kind == "textarea" || kind == "number") {
        int len = GetWindowTextLengthW(h);
        std::wstring buf(len + 1, L'\0');
        GetWindowTextW(h, buf.data(), len + 1); buf.resize(len);
        return narrow(buf);
    }
    if (kind == "check") return SendMessageW(h, BM_GETCHECK, 0, 0) == BST_CHECKED ? "true" : "false";
    if (kind == "slider") return std::to_string(static_cast<int>(SendMessageW(h, TBM_GETPOS, 0, 0)));
    if (kind == "select" || kind == "dropdown") {
        int i = static_cast<int>(SendMessageW(h, CB_GETCURSEL, 0, 0));
        auto it = option_values_.find(raw);
        if (it != option_values_.end() && i >= 0 && i < static_cast<int>(it->second.size())) return it->second[i];
        return std::to_string(i);
    }
    if (kind == "table" || kind == "list" || kind == "tree") {
        auto keys = get_selection_for(raw);
        return json_array(keys);
    }
    return "";
}

std::vector<std::string> Host::get_selection_for(void* raw) const {
    std::vector<std::string> keys;
    HWND h = static_cast<HWND>(raw);
    std::string kind = kind_of(raw);
    auto it = rows_.find(raw);
    if (it == rows_.end()) return keys;
    if (kind == "tree") {
        HTREEITEM sel = TreeView_GetSelection(h);
        for (int i = 0; i < static_cast<int>(it->second.order.size()); ++i) {
            // treeitem lParam holds the row index
        }
        if (sel) {
            TVITEMW tv{}; tv.mask = TVIF_PARAM; tv.hItem = sel;
            if (TreeView_GetItem(h, &tv) && tv.lParam >= 0 && tv.lParam < static_cast<LONG_PTR>(it->second.order.size()))
                keys.push_back(it->second.order[tv.lParam]);
        }
        return keys;
    }
    for (int i = ListView_GetNextItem(h, -1, LVNI_SELECTED); i >= 0; i = ListView_GetNextItem(h, i, LVNI_SELECTED))
        if (i < static_cast<int>(it->second.order.size())) keys.push_back(it->second.order[i]);
    return keys;
}

Host::RowStore* Host::store_for(const std::string& name) {
    void* c = find(name);
    auto it = c ? rows_.find(c) : rows_.end();
    return it == rows_.end() ? nullptr : &it->second;
}
const Host::RowStore* Host::store_for(const std::string& name) const {
    void* c = find(name);
    auto it = c ? rows_.find(c) : rows_.end();
    return it == rows_.end() ? nullptr : &it->second;
}

void Host::set_rows(const std::string& name, const std::vector<Record>& records) {
    RowStore* s = store_for(name);
    if (!s) return;
    auto keep = get_selection(name);
    s->order.clear(); s->records.clear();
    for (const auto& r : records) {
        auto key = record_key(r);
        if (key.empty()) continue;
        s->order.push_back(key);
        s->records[key] = r;
    }
    render_rows(name);
    std::vector<std::string> still;
    for (auto& k : keep) if (s->records.count(k)) still.push_back(k);
    set_selection(name, still);
}

void Host::append_row(const std::string& name, const Record& record) {
    RowStore* s = store_for(name);
    if (!s) return;
    auto key = record_key(record);
    if (key.empty()) return;
    auto keep = get_selection(name);
    if (!s->records.count(key)) s->order.push_back(key);
    s->records[key] = record;
    render_rows(name);
    set_selection(name, keep);
}

void Host::update_row(const std::string& name, const std::string& key, const Record& patch) {
    RowStore* s = store_for(name);
    if (!s || !s->records.count(key)) { if (s) append_row(name, patch); return; }
    auto keep = get_selection(name);
    for (auto& [k, v] : patch) s->records[key][k] = v;
    render_rows(name);
    set_selection(name, keep);
}

void Host::remove_row(const std::string& name, const std::string& key) {
    RowStore* s = store_for(name);
    if (!s || !s->records.count(key)) return;
    auto keep = get_selection(name);
    keep.erase(std::remove(keep.begin(), keep.end(), key), keep.end());
    s->records.erase(key);
    s->order.erase(std::remove(s->order.begin(), s->order.end(), key), s->order.end());
    render_rows(name);
    set_selection(name, keep);
}

void Host::clear_rows(const std::string& name) {
    RowStore* s = store_for(name);
    if (!s) return;
    s->order.clear(); s->records.clear();
    render_rows(name);
}

std::vector<std::string> Host::row_keys(const std::string& name) const {
    const RowStore* s = store_for(name);
    return s ? s->order : std::vector<std::string>{};
}

std::vector<std::string> Host::get_selection(const std::string& name) const {
    void* c = find(name);
    return c ? get_selection_for(c) : std::vector<std::string>{};
}

void Host::set_selection(const std::string& name, const std::vector<std::string>& keys) {
    RowStore* s = store_for(name);
    void* c = find(name);
    if (!s || !c) return;
    HWND h = static_cast<HWND>(c);
    std::string kind = kind_of(c);
    std::unordered_set<std::string> want(keys.begin(), keys.end());
    if (kind == "tree") {
        HTREEITEM item = TreeView_GetRoot(h);
        while (item) {
            TVITEMW tv{}; tv.mask = TVIF_PARAM; tv.hItem = item;
            if (TreeView_GetItem(h, &tv) && tv.lParam >= 0 && tv.lParam < static_cast<LONG_PTR>(s->order.size())
                && want.count(s->order[tv.lParam])) { TreeView_SelectItem(h, item); return; }
            item = TreeView_GetNextSibling(h, item);
        }
        return;
    }
    // Deselect only what is currently selected (O(selected), not O(rows)).
    for (int i = ListView_GetNextItem(h, -1, LVNI_SELECTED); i >= 0; i = ListView_GetNextItem(h, -1, LVNI_SELECTED))
        ListView_SetItemState(h, i, 0, LVIS_SELECTED | LVIS_FOCUSED);
    std::unordered_map<std::string, int> index;
    for (int i = 0; i < static_cast<int>(s->order.size()); ++i) index[s->order[i]] = i;
    for (const auto& k : keys)
        if (auto it = index.find(k); it != index.end())
            ListView_SetItemState(h, it->second, LVIS_SELECTED | LVIS_FOCUSED, LVIS_SELECTED | LVIS_FOCUSED);
}

void Host::render_rows(const std::string& name) {
    RowStore* s = store_for(name);
    void* c = find(name);
    if (!s || !c) return;
    HWND h = static_cast<HWND>(c);
    std::string kind = kind_of(c);
    if (kind == "tree") {
        TreeView_DeleteAllItems(h);
        for (int i = 0; i < static_cast<int>(s->order.size()); ++i) {
            auto& rec = s->records[s->order[i]];
            auto text = wide(rec.count("text") ? rec["text"] : s->order[i]);
            TVINSERTSTRUCTW ins{}; ins.hParent = TVI_ROOT; ins.hInsertAfter = TVI_LAST;
            ins.item.mask = TVIF_TEXT | TVIF_PARAM; ins.item.pszText = text.data(); ins.item.lParam = i;
            TreeView_InsertItem(h, &ins);
        }
        return;
    }
    if (s->virtualized) {
        ListView_SetItemCountEx(h, static_cast<int>(s->order.size()), LVSICF_NOSCROLL);
        InvalidateRect(h, nullptr, TRUE);
        return;
    }
    SendMessageW(h, WM_SETREDRAW, FALSE, 0);
    SendMessageW(h, LVM_DELETEALLITEMS, 0, 0);
    if (s->templated) {
        for (int i = 0; i < static_cast<int>(s->order.size()); ++i) {
            LVITEMW item{}; item.mask = LVIF_TEXT; item.iItem = i;
            std::wstring t = wide(s->records[s->order[i]].count("text") ? s->records[s->order[i]]["text"] : std::string{});
            item.pszText = t.data();
            SendMessageW(h, LVM_INSERTITEMW, 0, reinterpret_cast<LPARAM>(&item));
        }
        SendMessageW(h, WM_SETREDRAW, TRUE, 0);
        InvalidateRect(h, nullptr, TRUE);
        return;
    }
    for (int i = 0; i < static_cast<int>(s->order.size()); ++i) {
        auto& rec = s->records[s->order[i]];
        const std::string firstField = s->fields.empty() ? "text" : s->fields[0];
        std::wstring first = wide(rec.count(firstField) ? rec[firstField] : std::string{});
        LVITEMW item{}; item.mask = LVIF_TEXT; item.iItem = i; item.pszText = first.data();
        SendMessageW(h, LVM_INSERTITEMW, 0, reinterpret_cast<LPARAM>(&item));
        for (int col = 1; col < static_cast<int>(s->fields.size()); ++col) {
            auto cell = wide(rec.count(s->fields[col]) ? rec[s->fields[col]] : std::string{});
            LVITEMW sub{}; sub.iSubItem = col; sub.pszText = cell.data();
            SendMessageW(h, LVM_SETITEMTEXTW, i, reinterpret_cast<LPARAM>(&sub));
        }
    }
    SendMessageW(h, WM_SETREDRAW, TRUE, 0);
    InvalidateRect(h, nullptr, TRUE);
}

void Host::append(const std::string& name, const std::string& text) {
    HWND h = static_cast<HWND>(require(name));
    if (!h || kind_of(h) != "console") return;
    int len = GetWindowTextLengthW(h);
    SendMessageW(h, EM_SETSEL, len, len);
    auto line = wide((len ? "\r\n" : "") + text);
    SendMessageW(h, EM_REPLACESEL, FALSE, reinterpret_cast<LPARAM>(line.c_str()));
    SendMessageW(h, WM_VSCROLL, SB_BOTTOM, 0);
}

void Host::bind(const std::string& state_name, const std::string& control_name) {
    state_.add_binding(state_name, control_name);
    if (void* c = find(control_name)) control_binds_[c] = state_name;
}

void Host::apply_bound_value(const std::string& control_name, const std::string& value) {
    void* c = find(control_name);
    if (!c) return;
    std::string kind = kind_of(c);
    if (kind == "check") set(control_name, "checked", value);
    else if (kind == "slider" || kind == "progress" || kind == "spinner" || kind == "loading")
        set(control_name, "value", value);
    else if (kind == "select" || kind == "dropdown") {
        bool numeric = !value.empty() && (value[0] == '-' || (value[0] >= '0' && value[0] <= '9'));
        set(control_name, numeric ? "selected" : "selectedvalue", value);
    }
    else set(control_name, "text", value);
}

// ----- Context menus (ZU-80 parity) -----
void Host::build_menu(void* raw, const Node& node, const std::string& parent_path) {
    HMENU hmenu = static_cast<HMENU>(raw);
    if (node.kind == "sep") { AppendMenuW(hmenu, MF_SEPARATOR, 0, nullptr); return; }
    std::string path = parent_path.empty() ? node.text : parent_path + "/" + node.text;
    bool has_children = std::any_of(node.children.begin(), node.children.end(),
                                    [](const Node& c) { return c.kind == "menu" || c.kind == "item" || c.kind == "sep"; });
    if (has_children) {
        HMENU sub = CreatePopupMenu();
        for (const auto& c : node.children) build_menu(sub, c, path);
        AppendMenuW(hmenu, MF_POPUP | (node.attributes.count("disabled") ? MF_GRAYED : 0),
                    reinterpret_cast<UINT_PTR>(sub), wide(node.text).c_str());
        menu_paths_[path] = {sub, 0};
    } else {
        unsigned id = menu_next_id_++;
        UINT flags = MF_STRING | (node.attributes.count("disabled") ? MF_GRAYED : 0)
                   | (node.attributes.count("checked") ? MF_CHECKED : 0);
        AppendMenuW(hmenu, flags, id, wide(node.text).c_str());
        menu_paths_[path] = {hmenu, id};
        if (auto ch = node.attributes.find("on"); ch != node.attributes.end())
            menu_actions_[id] = {ch->second, ""};
    }
}

unsigned Host::add_menu_spec(void* raw, const MenuItem& item) {
    HMENU hmenu = static_cast<HMENU>(raw);
    if (item.separator) { AppendMenuW(hmenu, MF_SEPARATOR, 0, nullptr); return 0; }
    if (!item.submenu.empty()) {
        HMENU sub = CreatePopupMenu();
        for (const auto& s : item.submenu) add_menu_spec(sub, s);
        AppendMenuW(hmenu, MF_POPUP | (item.enabled ? 0 : MF_GRAYED), reinterpret_cast<UINT_PTR>(sub), wide(item.label).c_str());
        return 0;
    }
    unsigned id = menu_next_id_++;
    AppendMenuW(hmenu, MF_STRING | (item.enabled ? 0 : MF_GRAYED) | (item.checked ? MF_CHECKED : 0),
                id, wide(item.label).c_str());
    if (!item.channel.empty()) menu_actions_[id] = {item.channel, item.payload};
    return id;
}

void Host::popup_menu(const std::string& name, const std::vector<MenuItem>& items) {
    void* c = find(name);
    if (!c) return;
    HMENU menu = CreatePopupMenu();
    for (const auto& item : items) add_menu_spec(menu, item);
    POINT p; GetCursorPos(&p);
    int chosen = TrackPopupMenu(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, p.x, p.y, 0,
                                static_cast<HWND>(parent_), nullptr);
    if (chosen > 0) {
        auto it = menu_actions_.find(static_cast<unsigned>(chosen));
        if (it != menu_actions_.end()) dispatch(it->second.first, it->second.second);
    }
    DestroyMenu(menu);
}

void Host::set_menu_enabled(const std::string& path, bool enabled) {
    auto it = menu_paths_.find(path);
    if (it == menu_paths_.end()) return;
    EnableMenuItem(static_cast<HMENU>(it->second.first), it->second.second,
                   MF_BYCOMMAND | (enabled ? MF_ENABLED : MF_GRAYED));
    if (menu_bar_) DrawMenuBar(GetAncestor(static_cast<HWND>(parent_), GA_ROOT));
}

void Host::set_menu_checked(const std::string& path, bool checked) {
    auto it = menu_paths_.find(path);
    if (it == menu_paths_.end()) return;
    CheckMenuItem(static_cast<HMENU>(it->second.first), it->second.second,
                  MF_BYCOMMAND | (checked ? MF_CHECKED : MF_UNCHECKED));
}

// ----- Media / canvas (ZU-82 / ZU-86 parity) -----
void Host::load_image(void* control, const std::string& path) {
    ensure_gdiplus();
    if (auto it = images_.find(control); it != images_.end() && it->second)
        delete static_cast<Gdiplus::Image*>(it->second);
    auto* img = Gdiplus::Image::FromFile(wide(path).c_str());
    if (img && img->GetLastStatus() != Gdiplus::Ok) { delete img; img = nullptr; }
    images_[control] = img;
    InvalidateRect(static_cast<HWND>(control), nullptr, TRUE);
}

void Host::set_image(const std::string& name, const std::string& path) {
    if (void* c = find(name)) load_image(c, path);
}

void Host::on_paint(const std::string& name, PaintFn draw) {
    if (void* c = find(name)) { painters_[c] = std::move(draw); InvalidateRect(static_cast<HWND>(c), nullptr, TRUE); }
}

void Host::redraw(const std::string& name) {
    if (void* c = find(name)) InvalidateRect(static_cast<HWND>(c), nullptr, TRUE);
}

void Host::select_tab(void* tab, const std::string& id) {
    auto it = tabs_.find(tab);
    if (it == tabs_.end()) return;
    for (size_t i = 0; i < it->second.size(); ++i) {
        bool on = it->second[i].first == id;
        ShowWindow(static_cast<HWND>(it->second[i].second), on ? SW_SHOW : SW_HIDE);
        if (on) TabCtrl_SetCurSel(static_cast<HWND>(tab), static_cast<int>(i));
    }
}

// ----- Owner draw + custom draw (ZU-79 / ZU-83 / ZU-84 parity) -----
void Host::custom_draw_list(void* raw, long long& result) {
    auto* cd = static_cast<NMLVCUSTOMDRAW*>(raw);
    auto it = rows_.find(cd->nmcd.hdr.hwndFrom);
    if (it == rows_.end()) { result = CDRF_DODEFAULT; return; }
    if (cd->nmcd.dwDrawStage == CDDS_PREPAINT) { result = CDRF_NOTIFYITEMDRAW; return; }
    if (cd->nmcd.dwDrawStage == CDDS_ITEMPREPAINT) {
        int i = static_cast<int>(cd->nmcd.dwItemSpec);
        if (i >= 0 && i < static_cast<int>(it->second.order.size())) {
            const Record& rec = it->second.records[it->second.order[i]];
            auto s = rec.find("state");
            COLORREF bg, fg;
            if (s != rec.end() && row_state_colors(theme_, s->second, bg, fg)) {
                cd->clrTextBk = bg; cd->clrText = fg;
            } else {
                cd->clrTextBk = theme_.surface; cd->clrText = theme_.text;
            }
        }
        result = CDRF_NEWFONT; return;
    }
    result = CDRF_DODEFAULT;
}

void Host::custom_draw_tree(void* raw, long long& result) {
    auto* cd = static_cast<NMTVCUSTOMDRAW*>(raw);
    auto it = rows_.find(cd->nmcd.hdr.hwndFrom);
    if (it == rows_.end()) { result = CDRF_DODEFAULT; return; }
    if (cd->nmcd.dwDrawStage == CDDS_PREPAINT) { result = CDRF_NOTIFYITEMDRAW; return; }
    if (cd->nmcd.dwDrawStage == CDDS_ITEMPREPAINT) {
        LONG_PTR i = cd->nmcd.lItemlParam;
        if (i >= 0 && i < static_cast<LONG_PTR>(it->second.order.size())) {
            const Record& rec = it->second.records[it->second.order[i]];
            auto s = rec.find("state");
            COLORREF bg, fg;
            if (s != rec.end() && row_state_colors(theme_, s->second, bg, fg)) { cd->clrTextBk = bg; cd->clrText = fg; }
        }
        result = CDRF_NEWFONT; return;
    }
    result = CDRF_DODEFAULT;
}

void Host::draw_owner_button(void* raw) {
    auto* dis = static_cast<DRAWITEMSTRUCT*>(raw);
    HDC dc = dis->hDC;
    RECT r = dis->rcItem;
    bool pressed = (dis->itemState & ODS_SELECTED) != 0;
    HBRUSH bg = CreateSolidBrush(pressed ? blend(theme_.raised, theme_.accent, 0.30) : theme_.raised);
    FillRect(dc, &r, bg); DeleteObject(bg);
    FrameRect(dc, &r, reinterpret_cast<HBRUSH>(GetStockObject(DC_BRUSH)));
    auto ic = control_icons_.find(dis->hwndItem);
    wchar_t caption[128]{}; GetWindowTextW(dis->hwndItem, caption, 128);
    bool icon_only = caption[0] == 0;
    if (ic != control_icons_.end()) {
        int s = icon_only ? 16 : 14;
        RECT box{r.left + (icon_only ? (r.right - r.left - s) / 2 : 8), (r.top + r.bottom - s) / 2,
                 0, 0};
        box.right = box.left + s; box.bottom = box.top + s;
        draw_icon(dc, ic->second, box, theme_.text);
        r.left = box.right + 6;
    }
    if (!icon_only) {
        SetBkMode(dc, TRANSPARENT); SetTextColor(dc, theme_.text);
        DrawTextW(dc, caption, -1, &r, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
    }
}

void Host::draw_owner_list_item(void* raw) {
    auto* dis = static_cast<DRAWITEMSTRUCT*>(raw);
    auto it = rows_.find(dis->hwndItem);
    if (it == rows_.end()) return;
    HDC dc = dis->hDC;
    RECT r = dis->rcItem;
    int i = dis->itemID;
    bool selected = (dis->itemState & ODS_SELECTED) != 0;
    const Record* rec = (i >= 0 && i < static_cast<int>(it->second.order.size()))
        ? &it->second.records[it->second.order[i]] : nullptr;
    COLORREF bg = selected ? theme_.accent : theme_.surface, fg = selected ? theme_.window : theme_.text;
    if (!selected && rec) { auto s = rec->find("state"); COLORREF b, f; if (s != rec->end() && row_state_colors(theme_, s->second, b, f)) { bg = b; fg = f; } }
    HBRUSH br = CreateSolidBrush(bg); FillRect(dc, &r, br); DeleteObject(br);
    int x = r.left + 8, mid = (r.top + r.bottom) / 2;
    auto field = [&](const char* k) -> std::string { if (!rec) return ""; auto f = rec->find(k); return f == rec->end() ? "" : f->second; };
    std::string state = field("state");
    if (state == "new" || state == "active") {
        HBRUSH dot = CreateSolidBrush(selected ? theme_.window : theme_.accent);
        RECT d{x, mid - 3, x + 6, mid + 3}; FillRect(dc, &d, dot); DeleteObject(dot);
        x += 12;
    }
    std::string icon = field("icon");
    if (!icon.empty()) { RECT ib{x, mid - 8, x + 16, mid + 8}; draw_icon(dc, icon, ib, fg); x += 22; }
    SetBkMode(dc, TRANSPARENT);
    std::string title = field(it->second.fields.empty() ? "text" : it->second.fields[0].c_str());
    if (title.empty()) title = field("text");
    std::string subtitle = field("subtitle"), badge = field("badge");
    RECT tr{x, r.top, r.right - 8, r.bottom};
    if (!badge.empty()) {
        auto wb = wide(badge); SIZE sz; GetTextExtentPoint32W(dc, wb.c_str(), (int)wb.size(), &sz);
        RECT br2{r.right - sz.cx - 14, mid - sz.cy / 2 - 1, r.right - 6, mid + sz.cy / 2 + 1};
        HBRUSH bb = CreateSolidBrush(blend(bg, fg, 0.15)); FillRect(dc, &br2, bb); DeleteObject(bb);
        SetTextColor(dc, fg); TextOutW(dc, br2.left + 4, mid - sz.cy / 2, wb.c_str(), (int)wb.size());
        tr.right = br2.left - 6;
    }
    SetTextColor(dc, fg);
    auto wt = wide(title);
    if (!subtitle.empty()) {
        RECT t1{tr.left, r.top + 3, tr.right, mid};
        RECT t2{tr.left, mid, tr.right, r.bottom - 3};
        HFONT bold = CreateFontW(-13, 0, 0, 0, FW_BOLD, 0, 0, 0, 0, 0, 0, 0, 0, L"Segoe UI");
        HGDIOBJ old = SelectObject(dc, bold);
        DrawTextW(dc, wt.c_str(), -1, &t1, DT_LEFT | DT_SINGLELINE | DT_END_ELLIPSIS);
        SelectObject(dc, old); DeleteObject(bold);
        SetTextColor(dc, selected ? theme_.window : theme_.muted);
        auto ws = wide(subtitle);
        DrawTextW(dc, ws.c_str(), -1, &t2, DT_LEFT | DT_SINGLELINE | DT_END_ELLIPSIS);
    } else {
        DrawTextW(dc, wt.c_str(), -1, &tr, DT_LEFT | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);
    }
}

// ----- State -----
void State::init(const std::string& name, const std::string& value) { values_[name] = value; }
std::string State::get_string(const std::string& name) const {
    auto it = values_.find(name); return it == values_.end() ? std::string{} : it->second;
}
int State::get_int(const std::string& name) const {
    try { return std::stoi(get_string(name)); } catch (...) { return 0; }
}
bool State::get_bool(const std::string& name) const {
    auto v = get_string(name); return v == "true" || v == "1" || v == "on";
}
void State::set(const std::string& name, const std::string& value) {
    auto it = values_.find(name);
    if (it != values_.end() && it->second == value) return;
    values_[name] = value;
    propagate(name);
}
void State::mutate(const std::string& name, const std::string& op) {
    if (op == "plus1") set(name, std::to_string(get_int(name) + 1));
    else if (op == "minus1") set(name, std::to_string(get_int(name) - 1));
    else if (op == "toggle" || op == "not") set(name, get_bool(name) ? "false" : "true");
    else set(name, get_string(op));
}
void State::assign(const std::string& name, const std::string& from_name) { set(name, get_string(from_name)); }
void State::watch(const std::string& name, MessageHandler handler) { watchers_[name].push_back(std::move(handler)); }
void State::flush() {
    std::vector<std::string> names;
    names.reserve(values_.size());
    for (auto& [k, v] : values_) names.push_back(k);
    for (auto& n : names) propagate(n);
}
void State::add_binding(const std::string& state_name, const std::string& control_name) {
    auto& list = bindings_[state_name];
    for (auto& c : list) if (c == control_name) return;
    list.push_back(control_name);
}
void State::propagate(const std::string& name) {
    if (!propagating_.insert(name).second) return; // reentrancy guard (two-way)
    auto value = get_string(name);
    if (auto b = bindings_.find(name); b != bindings_.end() && host_)
        for (auto& control : b->second) host_->apply_bound_value(control, value);
    if (auto w = watchers_.find(name); w != watchers_.end())
        for (auto& handler : w->second) handler(value);
    propagating_.erase(name);
}

void Host::dispatch(const std::string& channel, const std::string& payload) {
    auto it = handlers_.find(channel); if (it == handlers_.end()) return;
    for (auto& handler : it->second) handler(payload);
}

long long Host::subclass_proc(void* hwnd, unsigned msg, unsigned long long wparam, long long lparam,
                              unsigned long long id, unsigned long long data) {
    auto* self = reinterpret_cast<Host*>(data);
    if (msg == WM_CTLCOLORSTATIC || msg == WM_CTLCOLOREDIT || msg == WM_CTLCOLORBTN ||
        msg == WM_CTLCOLORLISTBOX) {
        auto it = self->control_colors_.find(reinterpret_cast<HWND>(lparam));
        if (it != self->control_colors_.end()) {
            HDC dc = reinterpret_cast<HDC>(wparam);
            if (it->second.fg != 0xffffffff) SetTextColor(dc, it->second.fg);
            if (it->second.bg != 0xffffffff) {
                SetBkColor(dc, it->second.bg);
                if (it->second.brush) return reinterpret_cast<long long>(it->second.brush);
            }
        }
    }
    if (msg == WM_DRAWITEM) {
        auto* dis = reinterpret_cast<DRAWITEMSTRUCT*>(lparam);
        std::string k = self->kind_of(dis->hwndItem);
        if (dis->CtlType == ODT_BUTTON) { self->draw_owner_button(dis); return TRUE; }
        if (k == "image" || k == "canvas" || k == "overlay") {
            HDC dc = dis->hDC; RECT r = dis->rcItem;
            HBRUSH bg = CreateSolidBrush(self->theme_.raised); FillRect(dc, &r, bg); DeleteObject(bg);
            if (auto it = self->painters_.find(dis->hwndItem); it != self->painters_.end() && it->second)
                it->second(dc, r.right - r.left, r.bottom - r.top);
            else if (auto im = self->images_.find(dis->hwndItem); im != self->images_.end() && im->second) {
                ensure_gdiplus();
                Gdiplus::Graphics g(dc);
                auto* img = static_cast<Gdiplus::Image*>(im->second);
                float iw = (float)img->GetWidth(), ih = (float)img->GetHeight();
                float rw = (float)(r.right - r.left), rh = (float)(r.bottom - r.top);
                float scale = std::min(rw / iw, rh / ih);
                float dw = iw * scale, dh = ih * scale;
                g.DrawImage(img, (rw - dw) / 2, (rh - dh) / 2, dw, dh);
            }
            return TRUE;
        }
        if (k == "list") { self->draw_owner_list_item(dis); return TRUE; }
    }
    if (msg == WM_MEASUREITEM) {
        auto* mis = reinterpret_cast<MEASUREITEMSTRUCT*>(lparam);
        if (mis->CtlType == ODT_LISTVIEW) { mis->itemHeight = 44; return TRUE; }
    }
    if (msg == WM_DROPFILES) {
        HDROP drop = reinterpret_cast<HDROP>(wparam);
        unsigned count = DragQueryFileW(drop, 0xFFFFFFFF, nullptr, 0);
        POINT pt; DragQueryPoint(drop, &pt);
        HWND target = ChildWindowFromPoint(reinterpret_cast<HWND>(hwnd), pt);
        auto d = self->control_drop_.find(target);
        if (d != self->control_drop_.end()) {
            std::string json = "{\"target\":\"" + self->name_of(target) + "\",\"paths\":[";
            for (unsigned i = 0; i < count; ++i) {
                wchar_t path[MAX_PATH]{}; DragQueryFileW(drop, i, path, MAX_PATH);
                if (i) json += ',';
                json += '"';
                for (char c : narrow(path)) { if (c == '"' || c == '\\') json += '\\'; json += c; }
                json += '"';
            }
            json += "]}";
            self->dispatch(d->second, json);
        }
        DragFinish(drop);
        return 0;
    }
    if (msg == WM_CONTEXTMENU) {
        HWND from = reinterpret_cast<HWND>(wparam);
        auto x = self->control_context_.find(from);
        if (x != self->control_context_.end()) {
            std::string json = "{\"control\":\"" + self->name_of(from) + "\",\"keys\":"
                             + json_array(self->get_selection_for(from)) + "}";
            self->dispatch(x->second, json);
            return 0;
        }
    }
    if (msg == WM_COMMAND && lparam == 0 && HIWORD(wparam) == 0) {
        // Menu-bar item click.
        auto it = self->menu_actions_.find(LOWORD(wparam));
        if (it != self->menu_actions_.end()) { self->dispatch(it->second.first, it->second.second); return 0; }
    }
    if (msg == WM_NOTIFY) {
        auto* nm = reinterpret_cast<NMHDR*>(lparam);
        HWND source = nm->hwndFrom;
        if (nm->code == NM_CUSTOMDRAW) {
            std::string k = self->kind_of(source);
            long long r = CDRF_DODEFAULT;
            if (k == "table" || k == "list") self->custom_draw_list(lparam ? reinterpret_cast<void*>(lparam) : nullptr, r);
            else if (k == "tree") self->custom_draw_tree(reinterpret_cast<void*>(lparam), r);
            else return DefSubclassProc(static_cast<HWND>(hwnd), msg, wparam, lparam);
            return r;
        }
        if (nm->code == LVN_GETDISPINFOW) {
            auto* di = reinterpret_cast<NMLVDISPINFOW*>(lparam);
            auto st = self->rows_.find(source);
            if (st != self->rows_.end() && (di->item.mask & LVIF_TEXT)) {
                int i = di->item.iItem, col = di->item.iSubItem;
                static std::wstring buf;
                if (i >= 0 && i < (int)st->second.order.size() && col < (int)st->second.fields.size()) {
                    auto& rec = st->second.records[st->second.order[i]];
                    auto f = rec.find(st->second.fields[col]);
                    buf = wide(f == rec.end() ? std::string{} : f->second);
                } else buf.clear();
                di->item.pszText = buf.data();
            }
            return 0;
        }
        if (nm->code == NM_RCLICK || nm->code == NM_RETURN) {
            if (auto x = self->control_context_.find(source); x != self->control_context_.end() && nm->code == NM_RCLICK) {
                std::string json = "{\"control\":\"" + self->name_of(source) + "\",\"keys\":"
                                 + json_array(self->get_selection_for(source)) + "}";
                self->dispatch(x->second, json);
            }
        }
        if (nm->code == TCN_SELCHANGE) {
            auto t = self->tabs_.find(source);
            if (t != self->tabs_.end()) {
                int sel = TabCtrl_GetCurSel(source);
                for (size_t i = 0; i < t->second.size(); ++i)
                    ShowWindow(static_cast<HWND>(t->second[i].second), (int)i == sel ? SW_SHOW : SW_HIDE);
                if (sel >= 0 && sel < (int)t->second.size()) {
                    auto ch = self->control_channels_.find(source);
                    if (ch != self->control_channels_.end()) self->dispatch(ch->second, t->second[sel].first);
                }
            }
        }
        // Row activation (double-click / Enter) -> onactivate with the item key.
        if (auto a = self->control_activate_.find(source); a != self->control_activate_.end()) {
            if (nm->code == LVN_ITEMACTIVATE ||
                (nm->code == NM_DBLCLK && self->kind_of(source) == "tree")) {
                auto keys = self->get_selection_for(source);
                if (!keys.empty()) self->dispatch(a->second, keys.front());
            }
        }
        if (nm->code == LVN_BEGINDRAG || nm->code == TVN_BEGINDRAGW) {
            if (self->drag_sources_.count(source)) {
                self->drag_keys_ = self->get_selection_for(source);
                self->drag_from_ = source;
                SetCapture(static_cast<HWND>(hwnd));
            }
        }
        // Selection change -> on channel with the JSON key array.
        if ((nm->code == LVN_ITEMCHANGED || nm->code == TVN_SELCHANGEDW)) {
            if (auto it = self->control_channels_.find(source); it != self->control_channels_.end())
                self->dispatch(it->second, self->payload_for(source));
        }
    }
    if (msg == WM_LBUTTONUP && self->drag_from_ && !self->drag_keys_.empty()) {
        ReleaseCapture();
        POINT pt{GET_X_LPARAM(lparam), GET_Y_LPARAM(lparam)};
        HWND target = ChildWindowFromPoint(reinterpret_cast<HWND>(hwnd), pt);
        auto d = self->control_drop_.find(target);
        if (d != self->control_drop_.end() && target != self->drag_from_) {
            std::string tk;
            if (self->kind_of(target) == "table" || self->kind_of(target) == "list") {
                POINT lp = pt; ScreenToClient(target, &lp);
                LVHITTESTINFO ht{}; ht.pt = lp;
                int row = ListView_HitTest(target, &ht);
                auto st = self->rows_.find(target);
                if (row >= 0 && st != self->rows_.end() && row < (int)st->second.order.size()) tk = st->second.order[row];
            }
            std::string json = "{\"target\":\"" + self->name_of(target) + "\",\"targetKey\":\"" + tk
                             + "\",\"keys\":" + json_array(self->drag_keys_) + "}";
            self->dispatch(d->second, json);
        }
        self->drag_from_ = nullptr; self->drag_keys_.clear();
    }
    if (msg == WM_COMMAND || msg == WM_HSCROLL) {
        HWND source = reinterpret_cast<HWND>(lparam);
        unsigned code = HIWORD(wparam);
        auto it = self->control_channels_.find(source);
        bool editish = self->kind_of(source) == "input" || self->kind_of(source) == "textarea" || self->kind_of(source) == "number";
        if (it != self->control_channels_.end()) {
            // Coalesce: for an edit, `on` fires on EN_CHANGE; for others, on any command.
            if (!editish || msg == WM_HSCROLL || code == EN_CHANGE)
                self->dispatch(it->second, self->payload_for(source));
        }
        if (auto c = self->control_commit_.find(source); c != self->control_commit_.end() && code == EN_KILLFOCUS)
            self->dispatch(c->second, self->payload_for(source));
        // Two-way binding write-back: mirror the control's current value into state.
        auto bound = self->control_binds_.find(source);
        if (bound != self->control_binds_.end()) {
            std::string kind = self->kind_of(source);
            std::string value;
            if (kind == "input" || kind == "textarea") {
                int len = GetWindowTextLengthW(source);
                std::wstring buf(len + 1, L'\0');
                GetWindowTextW(source, buf.data(), len + 1); buf.resize(len);
                value = narrow(buf);
            } else if (kind == "check") {
                value = SendMessageW(source, BM_GETCHECK, 0, 0) == BST_CHECKED ? "true" : "false";
            } else if (kind == "slider") {
                value = std::to_string(static_cast<int>(SendMessageW(source, TBM_GETPOS, 0, 0)));
            } else if (kind == "select" || kind == "dropdown") {
                value = std::to_string(static_cast<int>(SendMessageW(source, CB_GETCURSEL, 0, 0)));
            }
            self->state_.set(bound->second, value);
        }
    }
    return DefSubclassProc(static_cast<HWND>(hwnd), msg, static_cast<WPARAM>(wparam), static_cast<LPARAM>(lparam));
}

}  // namespace zui
