// Native state / bind / handler semantics (ZU-64, core/RUNTIME_CONTRACT.md).
#include "../zui.h"
#include <windows.h>
#include <iostream>

static int failures = 0;
static void check(const char* name, bool ok) {
    std::cout << (ok ? "ok   " : "FAIL ") << name << "\n";
    if (!ok) ++failures;
}

int main() {
    HWND parent = CreateWindowExW(0, L"STATIC", L"t", WS_OVERLAPPEDWINDOW,
                                  0, 0, 640, 480, nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
    if (!parent) return 1;

    zui::Host host(parent);
    host.build(zui::Node{"root", "", {}, {
        zui::Node{"heading", "", {{"id", "titleA"}}, {}},
        zui::Node{"text", "", {{"id", "titleB"}}, {}},
        zui::Node{"slider", "", {{"bind", "count"}, {"min", "0"}, {"max", "10"}}, {}},
        zui::Node{"input", "", {{"bind", "name"}}, {}},
        zui::Node{"button", "x", {{"id", "untouched"}}, {}},
    }});

    host.state().init("title", "Hello");
    host.state().init("count", "3");
    host.state().init("name", "ada");
    host.bind("title", "titleA");
    host.bind("title", "titleB");
    host.bind("count", "count");
    host.bind("name", "name");

    int titleWatch = 0;
    host.state().watch("title", [&](const std::string&) { ++titleWatch; });

    host.state().flush();
    check("initial state -> label A", host.get_text("titleA") == "Hello");
    check("initial state -> label B", host.get_text("titleB") == "Hello");
    check("initial numeric state -> slider", host.get_value("count") == 3);
    check("initial text state -> input", host.get_text("name") == "ada");

    host.state().set("title", "World");
    check("one-way update reaches label A", host.get_text("titleA") == "World");
    check("one-way update reaches label B", host.get_text("titleB") == "World");
    check("watcher fired", titleWatch >= 1);

    // Unrelated control text is never touched by an unrelated property change.
    check("unrelated control untouched", host.get_text("untouched") == "x");

    host.state().mutate("count", "plus1");
    check("mutate plus1 advances state", host.state().get_int("count") == 4);
    check("mutate plus1 propagated to slider", host.get_value("count") == 4);

    host.state().mutate("count", "minus1");
    check("mutate minus1", host.state().get_int("count") == 3);

    // Handler idiom: a channel handler mutating state, no rebuild.
    host.on("bump", [&](const std::string&) { host.state().mutate("count", "plus1"); });
    host.send("bump");
    check("handler channel mutates state", host.state().get_int("count") == 4);

    // Two-way write-back path (simulate the control change notification).
    SetWindowTextW(static_cast<HWND>(host.find("name")), L"grace");
    SendMessageW(parent, WM_COMMAND,
                 MAKEWPARAM(0, EN_CHANGE), reinterpret_cast<LPARAM>(host.find("name")));
    check("two-way: EN_CHANGE writes input text back to state", host.state().get_string("name") == "grace");

    DestroyWindow(parent);
    std::cout << (failures == 0 ? "state_test: all passed\n" : "state_test: failures\n");
    return failures == 0 ? 0 : 1;
}
