#include "../zui.h"
#include <windows.h>
#include <iostream>

int main() {
    HWND parent = CreateWindowExW(0, L"STATIC", L"test", WS_OVERLAPPEDWINDOW,
                                  0, 0, 640, 480, nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
    if (!parent) return 1;
    zui::Host host(parent);
    bool dispatched = false;
    host.on("save", [&](const std::string& value) { dispatched = value == "native"; });
    host.build(zui::Node{"root", "", {}, {
        zui::Node{"button", "Save", {{"export", "saveButton"}, {"on", "save"}}, {}}
    }});
    host.send("save", "native");
    const bool ok = dispatched && host.find("saveButton") != nullptr;
    DestroyWindow(parent);
    if (!ok) { std::cerr << "native host build/dispatch failed\n"; return 1; }
    std::cout << "host_test: native controls passed\n";
    return 0;
}
