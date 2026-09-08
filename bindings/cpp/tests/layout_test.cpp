// Native C++ layout engine (ZU-66 / ZU-89): measure -> arrange, real horizontal
// rows, nested containers, fill/stretch, sidebar min width, resize reposition.
#include "../zui.h"
#include <windows.h>
#include <iostream>

static int failures = 0;
static void check(const char* name, bool ok) {
    std::cout << (ok ? "ok   " : "FAIL ") << name << "\n";
    if (!ok) ++failures;
}

// Client-space bounds of a named control, relative to the host parent.
static RECT bounds(zui::Host& host, HWND parent, const char* name) {
    RECT r{}; GetWindowRect(static_cast<HWND>(host.find(name)), &r);
    POINT tl{r.left, r.top}, br{r.right, r.bottom};
    ScreenToClient(parent, &tl); ScreenToClient(parent, &br);
    return {tl.x, tl.y, br.x, br.y};
}

int main() {
    HWND parent = CreateWindowExW(0, L"STATIC", L"t", WS_OVERLAPPEDWINDOW,
                                  0, 0, 1000, 700, nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
    if (!parent) return 1;
    // Give the parent a known client size.
    SetWindowPos(parent, nullptr, 0, 0, 1000, 700, SWP_NOMOVE | SWP_NOZORDER);
    RECT client{}; GetClientRect(parent, &client);
    int W = client.right, H = client.bottom;

    zui::Host host(parent);
    host.build(zui::Node{"root", "", {}, {
        zui::Node{"workspace", "", {}, {
            zui::Node{"sidebar", "", {{"min", "220"}}, {
                zui::Node{"text", "Library", {{"id", "libLabel"}}, {}},
            }},
            zui::Node{"fill", "", {}, {
                zui::Node{"row", "", {{"id", "toolbar"}}, {
                    zui::Node{"button", "A", {{"id", "btnA"}}, {}},
                    zui::Node{"button", "B", {{"id", "btnB"}}, {}},
                    zui::Node{"input", "", {{"id", "search"}}, {}},
                }},
                zui::Node{"table", "", {{"id", "grid"}}, {
                    zui::Node{"column", "Name", {{"field", "name"}}, {}},
                }},
            }},
        }},
        zui::Node{"statusbar", "", {{"id", "status"}}, {
            zui::Node{"text", "ready", {{"id", "statusText"}}, {}},
        }},
    }});

    auto side = bounds(host, parent, "libLabel");
    auto toolbar = bounds(host, parent, "toolbar");
    auto grid = bounds(host, parent, "grid");
    auto a = bounds(host, parent, "btnA");
    auto b = bounds(host, parent, "btnB");
    auto search = bounds(host, parent, "search");
    auto status = bounds(host, parent, "status");

    check("sidebar honours its min width (~220)", side.right >= 200 && side.right <= 280);
    check("content sits to the right of the sidebar", grid.left > side.right);
    check("content fill stretches to near the window edge", grid.right > W - 60);
    check("row lays out horizontally: B is right of A", b.left >= a.right - 2);
    check("row lays out horizontally: search is right of B", search.left >= b.right - 2);
    check("row fill child (input) stretches across the row", search.right > grid.right - 40);
    check("toolbar sits above the table (nested vertical)", toolbar.bottom <= grid.top + 2);
    check("statusbar is pinned near the bottom", status.top > H - 80);
    check("statusbar spans the width", status.right - status.left > W - 40);

    // ----- resize: everything repositions, nothing is recreated -----
    void* gridHwnd = host.find("grid");
    host.relayout(600, 500);
    auto grid2 = bounds(host, parent, "grid");
    check("resize repositioned the table", grid2.right != grid.right);
    check("resize shrank the content to the new width", grid2.right <= 600 && grid2.right > 300);
    check("resize did not recreate the control", host.find("grid") == gridHwnd);

    host.relayout(W, H);
    auto grid3 = bounds(host, parent, "grid");
    check("resize back restores the layout", grid3.right > W - 60);

    DestroyWindow(parent);
    std::cout << (failures == 0 ? "layout_test: all passed\n" : "layout_test: failures\n");
    return failures == 0 ? 0 : 1;
}
