#include "zui.h"

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <commctrl.h>
#include <algorithm>

namespace zui {
namespace {
std::wstring wide(const std::string& value) {
    if (value.empty()) return {};
    int size = MultiByteToWideChar(CP_UTF8, 0, value.data(), static_cast<int>(value.size()), nullptr, 0);
    std::wstring result(size, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, value.data(), static_cast<int>(value.size()), result.data(), size);
    return result;
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
    INITCOMMONCONTROLSEX init{sizeof(init), ICC_STANDARD_CLASSES | ICC_BAR_CLASSES | ICC_LISTVIEW_CLASSES};
    InitCommonControlsEx(&init);
    SetWindowSubclass(static_cast<HWND>(parent_), reinterpret_cast<SUBCLASSPROC>(&Host::subclass_proc), 1,
                      reinterpret_cast<DWORD_PTR>(this));
}

Host::~Host() {
    if (parent_) RemoveWindowSubclass(static_cast<HWND>(parent_), reinterpret_cast<SUBCLASSPROC>(&Host::subclass_proc), 1);
}

void Host::build(const Node& root) {
    HWND parent = static_cast<HWND>(parent_);
    for (HWND child = GetWindow(parent, GW_CHILD); child;) {
        HWND next = GetWindow(child, GW_HWNDNEXT);
        DestroyWindow(child);
        child = next;
    }
    control_channels_.clear();
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
    auto bind_name = attr(node, "bind", attr(node, "id").c_str());
    auto export_name = attr(node, "export", bind_name.c_str());
    if (!export_name.empty()) exports_[export_name] = control;

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

void Host::dispatch(const std::string& channel, const std::string& payload) {
    auto it = handlers_.find(channel); if (it == handlers_.end()) return;
    for (auto& handler : it->second) handler(payload);
}

long long Host::subclass_proc(void* hwnd, unsigned msg, unsigned long long wparam, long long lparam,
                              unsigned long long id, unsigned long long data) {
    auto* self = reinterpret_cast<Host*>(data);
    if (msg == WM_COMMAND || msg == WM_HSCROLL || msg == WM_NOTIFY) {
        HWND source = msg == WM_COMMAND ? reinterpret_cast<HWND>(lparam)
                    : msg == WM_HSCROLL ? reinterpret_cast<HWND>(lparam)
                    : reinterpret_cast<NMHDR*>(lparam)->hwndFrom;
        auto it = self->control_channels_.find(source);
        if (it != self->control_channels_.end()) self->dispatch(it->second, "");
    }
    return DefSubclassProc(static_cast<HWND>(hwnd), msg, static_cast<WPARAM>(wparam), static_cast<LPARAM>(lparam));
}

}  // namespace zui
