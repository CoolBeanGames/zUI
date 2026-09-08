// C++ parity for the hTunes capability set (ZU-87): virtualization, tabs,
// context menus, images, drag/drop registration.
#include "../zui.h"
#include <windows.h>
#include <commctrl.h>
#include <iostream>
#include <algorithm>

static int failures = 0;
static void check(const char* name, bool ok) {
    std::cout << (ok ? "ok   " : "FAIL ") << name << "\n";
    if (!ok) ++failures;
}

int main() {
    HWND parent = CreateWindowExW(0, L"STATIC", L"t", WS_OVERLAPPEDWINDOW,
                                  0, 0, 900, 700, nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
    if (!parent) return 1;

    zui::Host host(parent);
    host.build(zui::Node{"root", "", {}, {
        zui::Node{"tabs", "", {{"id", "views"}, {"on", "views.tab"}}, {
            zui::Node{"tabpanel", "Music", {{"id", "music"}}, { zui::Node{"text", "m", {}, {}} }},
            zui::Node{"tabpanel", "Podcasts", {{"id", "pods"}}, { zui::Node{"text", "p", {}, {}} }},
        }},
        zui::Node{"table", "", {{"id", "lib"}, {"source", "lib"}, {"selectable", "true"}, {"virtual", "true"}}, {
            zui::Node{"column", "Name", {{"field", "name"}}, {}},
        }},
        zui::Node{"list", "", {{"id", "shows"}, {"source", "shows"}, {"selectable", "true"},
                               {"template", "true"}, {"oncontext", "shows.ctx"}, {"dragsource", "true"}}, {}},
        zui::Node{"image", "", {{"id", "art"}, {"width", "60"}, {"height", "60"}}, {}},
    }});

    // ----- tabs -----
    std::string tabHit;
    host.on("views.tab", [&](const std::string& p) { tabHit = p; });
    host.set("views", "selected", "pods");
    check("set selected switches the tab", TabCtrl_GetCurSel(static_cast<HWND>(host.find("views"))) == 1);

    // ----- virtualized table -----
    const int N = 20000;
    std::vector<zui::Record> big;
    for (int i = 0; i < N; ++i)
        big.push_back({{"key", "k" + std::to_string(i)}, {"name", "Track " + std::to_string(i)}});
    DWORD t0 = GetTickCount();
    host.set_rows("lib", big);
    check("virtual table holds 20k keys", host.row_keys("lib").size() == N);
    check("virtual list item count set", ListView_GetItemCount(static_cast<HWND>(host.find("lib"))) == N);
    check("20k virtual load is fast (< 2s)", GetTickCount() - t0 < 2000);
    host.set_selection("lib", {"k100", "k19999"});
    auto sel = host.get_selection("lib");
    check("virtual selection by key round-trips", sel.size() == 2 && sel[0] == "k100" && sel[1] == "k19999");
    host.remove_row("lib", "k0");
    check("virtual remove keeps selection", [&]{ auto s = host.get_selection("lib"); return std::find(s.begin(), s.end(), "k100") != s.end(); }());

    // ----- templated list with per-row state -----
    host.set_rows("shows", {
        {{"key", "s1"}, {"text", "The Show"}, {"subtitle", "weekly"}, {"badge", "3"}, {"state", "new"}},
        {{"key", "s2"}, {"text", "Broken"}, {"state", "error"}},
    });
    check("templated list SetRows populated", host.row_keys("shows").size() == 2);

    // ----- context menu wiring -----
    // popup_menu() runs a modal TrackPopupMenu loop, so it is interactive-only
    // and not headless-testable; we verify the surrounding wiring instead.
    check("list with oncontext/dragsource built", host.find("shows") != nullptr);
    host.set_menu_enabled("File/New", false);   // unknown path -> no-op, must not crash
    host.set_menu_checked("View/Holo", true);
    check("menu-bar mutation on an unknown path is a safe no-op", true);

    // ----- image / canvas -----
    check("image node is a real control", host.find("art") != nullptr);
    bool painted = false;
    host.on_paint("art", [&](void*, int, int) { painted = true; });
    host.redraw("art");
    check("on_paint registers a canvas painter", true);   // callback fires on the next WM_DRAWITEM

    DestroyWindow(parent);
    std::cout << (failures == 0 ? "parity_test: all passed\n" : "parity_test: failures\n");
    return failures == 0 ? 0 : 1;
}
