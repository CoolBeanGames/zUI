#include "zui.h"

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <commctrl.h>
#include <algorithm>
#include <cassert>

namespace zui {
namespace {
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

    if (node.kind == "button") { klass = L"BUTTON"; style |= BS_PUSHBUTTON | WS_TABSTOP; }
    else if (node.kind == "check") { klass = L"BUTTON"; style |= BS_AUTOCHECKBOX | WS_TABSTOP; }
    else if (node.kind == "input" || node.kind == "textarea") {
        klass = L"EDIT"; style |= WS_BORDER | WS_TABSTOP | ES_AUTOHSCROLL;
        if (node.kind == "textarea") { style |= ES_MULTILINE | ES_AUTOVSCROLL | WS_VSCROLL; height = 90; }
    }
    else if (node.kind == "slider") { klass = TRACKBAR_CLASSW; style |= TBS_HORZ | WS_TABSTOP; height = 36; }
    else if (node.kind == "progress" || node.kind == "spinner" || node.kind == "loading") { klass = PROGRESS_CLASSW; height = 16; }
    else if (node.kind == "select" || node.kind == "dropdown") { klass = WC_COMBOBOXW; style |= CBS_DROPDOWNLIST | WS_TABSTOP; height = 180; }
    else if (node.kind == "table") { klass = WC_LISTVIEWW; style |= LVS_REPORT | LVS_SINGLESEL | WS_BORDER | WS_TABSTOP; height = 300; }
    else if (node.kind == "tree") { klass = WC_TREEVIEWW; style |= TVS_HASLINES | TVS_LINESATROOT | WS_BORDER | WS_TABSTOP; height = 300; }
    else if (node.kind == "window" || node.kind == "root" || node.kind == "col" || node.kind == "fill" ||
             node.kind == "workspace" || node.kind == "row" || node.kind == "panel" || node.kind == "sidebar" ||
             node.kind == "statusbar" || node.kind == "nav" || node.kind == "tabs" || node.kind == "menubar") {
        klass = L"STATIC"; style |= SS_LEFT; container = true; height = 4;
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
    if (node.kind == "table") {
        int index = 0;
        for (const auto& col : node.children) if (col.kind == "column") {
            auto heading = wide(col.text); LVCOLUMNW spec{LVCF_TEXT | LVCF_WIDTH, 0, 150, heading.data()};
            ListView_InsertColumn(control, index++, &spec);
        }
    }
    if (node.kind == "select" || node.kind == "dropdown") {
        for (const auto& item : node.children) if (item.kind == "option" || item.kind == "item") {
            auto value = wide(item.text); SendMessageW(control, CB_ADDSTRING, 0, reinterpret_cast<LPARAM>(value.c_str()));
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
    control_kinds_[control] = node.kind;
    // Register every lookup name. Priority is export > bind > id (a more specific
    // name wins), but all three resolve to this control.
    for (const char* key : {"id", "bind", "export"}) {
        auto name = attr(node, key);
        if (!name.empty()) exports_[name] = control;
    }

    int child_x = x + (container ? 8 : 0), child_y = y + height + 4;
    if (container) {
        for (const auto& child : node.children) create_node(parent, child, child_x, child_y, std::max(160, width - 16));
        height = std::max(height, child_y - y);
        SetWindowPos(control, HWND_BOTTOM, x, y, width, height, SWP_NOACTIVATE);
    }
    y += height + 8;
    return control;
}

void Host::on(const std::string& channel, MessageHandler handler) { handlers_[channel].push_back(std::move(handler)); }
void Host::send(const std::string& channel, const std::string& payload) { dispatch(channel, payload); }
void Host::set_theme(const std::string& name) { dispatch("theme-changed", name); InvalidateRect(static_cast<HWND>(parent_), nullptr, TRUE); }
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
        if (kind == "select" || kind == "dropdown") { SendMessageW(h, CB_SETCURSEL, n, 0); return true; }
        if (kind == "table") {
            ListView_SetItemState(h, -1, 0, LVIS_SELECTED | LVIS_FOCUSED);
            if (n >= 0) { ListView_SetItemState(h, n, LVIS_SELECTED | LVIS_FOCUSED, LVIS_SELECTED | LVIS_FOCUSED);
                          ListView_EnsureVisible(h, n, FALSE); }
            return true;
        }
        return false;
    }
    if ((property == "selectedvalue" || property == "selectedtext") &&
        (kind == "select" || kind == "dropdown")) {
        int idx = static_cast<int>(SendMessageW(h, CB_FINDSTRINGEXACT, static_cast<WPARAM>(-1),
                                                reinterpret_cast<LPARAM>(wide(value).c_str())));
        if (idx >= 0) { SendMessageW(h, CB_SETCURSEL, idx, 0); return true; }
        return false;
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
        if (kind == "table")
            return std::to_string(ListView_GetNextItem(h, -1, LVNI_SELECTED));
        return {};
    }
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
    if (msg == WM_COMMAND || msg == WM_HSCROLL || msg == WM_NOTIFY) {
        HWND source = msg == WM_COMMAND ? reinterpret_cast<HWND>(lparam)
                    : msg == WM_HSCROLL ? reinterpret_cast<HWND>(lparam)
                    : reinterpret_cast<NMHDR*>(lparam)->hwndFrom;
        auto it = self->control_channels_.find(source);
        if (it != self->control_channels_.end()) self->dispatch(it->second, "");
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
