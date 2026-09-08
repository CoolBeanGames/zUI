// Native host tests for the incremental mutation contract (core/RUNTIME_CONTRACT.md).
// Proves: a screen is built once; many property mutations follow; the HWND
// instances are preserved; no rebuild happens during mutation.
#include "../zui.h"
#include <windows.h>
#include <iostream>

static int failures = 0;
static void check(const char* name, bool ok) {
    std::cout << (ok ? "ok   " : "FAIL ") << name << "\n";
    if (!ok) ++failures;
}

int main() {
    HWND parent = CreateWindowExW(0, L"STATIC", L"test", WS_OVERLAPPEDWINDOW,
                                  0, 0, 640, 480, nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
    if (!parent) return 1;

    zui::Host host(parent);
    host.build(zui::Node{"root", "", {}, {
        zui::Node{"heading", "Now playing", {{"id", "trackTitle"}}, {}},
        zui::Node{"slider", "", {{"bind", "progress"}, {"min", "0"}, {"max", "100"}}, {}},
        zui::Node{"check", "Shuffle", {{"export", "shuffle"}}, {}},
        zui::Node{"text", "Loading", {{"id", "loading"}}, {}},
        zui::Node{"input", "", {{"id", "search"}}, {}},
        zui::Node{"select", "", {{"id", "rate"}}, {
            zui::Node{"option", "1x", {}, {}},
            zui::Node{"option", "1.5x", {}, {}},
            zui::Node{"option", "2x", {}, {}},
        }},
    }});

    // Instances captured at construction time.
    void* title = host.find("trackTitle");
    void* progress = host.find("progress");
    void* shuffle = host.find("shuffle");
    void* rate = host.find("rate");
    check("lookup by id / bind / export all resolve", title && progress && shuffle && rate);

    // Many mutations, no build() call.
    host.set_text("trackTitle", "Paranoid Android");
    host.set_value("progress", 42);
    host.set_checked("shuffle", true);
    host.set_visible("loading", false);
    host.set_enabled("search", false);
    host.set_selected("rate", 2);
    host.set_color("trackTitle", 0x000000ff, 0x00202020);
    host.set_text("trackTitle", "Let Down");

    check("set_text mutated existing control", host.get_text("trackTitle") == "Let Down");
    check("set_value mutated existing slider", host.get_value("progress") == 42);
    check("set_checked mutated existing checkbox", host.get_checked("shuffle"));
    check("set_visible cleared WS_VISIBLE on existing control",
          (GetWindowLongW(static_cast<HWND>(host.find("loading")), GWL_STYLE) & WS_VISIBLE) == 0);
    check("set_enabled mutated existing input", !IsWindowEnabled(static_cast<HWND>(host.find("search"))));
    check("set_selected mutated existing combobox", host.get_selected("rate") == 2);

    // Same HWNDs -> proof nothing was recreated.
    check("heading HWND preserved", title == host.find("trackTitle"));
    check("slider HWND preserved", progress == host.find("progress"));
    check("checkbox HWND preserved", shuffle == host.find("shuffle"));
    check("combobox HWND preserved", rate == host.find("rate"));

    // A second build() would trip the construction-only guard; ordinary mutation
    // must never recreate the tree. Count every descendant HWND as a rebuild
    // sentinel: containers nest their children (ZU-66), so the total, not the
    // direct-child count, is what must stay constant.
    struct Counter {
        static int all(HWND w) {
            int n = 0;
            for (HWND c = GetWindow(w, GW_CHILD); c; c = GetWindow(c, GW_HWNDNEXT)) n += 1 + all(c);
            return n;
        }
    };
    check("descendant HWND count stable after mutations (no rebuild)", Counter::all(parent) == 7);
    check("root is a single nested container", GetWindow(parent, GW_CHILD) != nullptr &&
          GetWindow(GetWindow(parent, GW_CHILD), GW_HWNDNEXT) == nullptr);

    DestroyWindow(parent);
    std::cout << (failures == 0 ? "mutation_test: all passed\n" : "mutation_test: failures\n");
    return failures == 0 ? 0 : 1;
}
