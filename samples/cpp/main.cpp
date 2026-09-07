// zUI native Win32 sample. Every widget is an HWND/common control.

#include "zui.h"

#include <windows.h>
#include <string>
#include <unordered_map>

void build_ui(
    zui::Host& host,
    const std::unordered_map<std::string, zui::MessageHandler>& handlers);

static zui::Host* g_ui = nullptr;

LRESULT CALLBACK WndProc(HWND hwnd, UINT msg, WPARAM w, LPARAM l) {
    switch (msg) {
        case WM_DESTROY:
            PostQuitMessage(0);
            return 0;
    }
    return DefWindowProc(hwnd, msg, w, l);
}

int WINAPI wWinMain(HINSTANCE hInst, HINSTANCE, PWSTR commandLine, int nShow) {
    WNDCLASSW wc{};
    wc.lpfnWndProc = WndProc;
    wc.hInstance = hInst;
    wc.lpszClassName = L"ZuiSampleWindow";
    RegisterClassW(&wc);

    HWND hwnd = CreateWindowExW(
        0, wc.lpszClassName, L"zUI sample (C++)",
        WS_OVERLAPPEDWINDOW, CW_USEDEFAULT, CW_USEDEFAULT, 1180, 820,
        nullptr, nullptr, hInst, nullptr);

    zui::Host ui(hwnd);
    g_ui = &ui;
    ui.on("selection", [](const std::string& json) {
        OutputDebugStringA(("selection: " + json + "\n").c_str());
    });
    ui.on("theme-changed", [](const std::string& json) {
        OutputDebugStringA(("theme: " + json + "\n").c_str());
    });
    ui.on("transport", [](const std::string& json) {
        OutputDebugStringA(("transport: " + json + "\n").c_str());
    });

    // The ZSL/ZML screen is AOT-compiled into showcase.g.cpp and linked into
    // this native executable. Handlers are ordinary C++ callbacks.
    build_ui(ui, {
        {"playlist.new", [](const std::string&) { OutputDebugStringA("playlist.new\n"); }},
        {"track.edit", [](const std::string&) { OutputDebugStringA("track.edit\n"); }},
    });
    if (commandLine && wcsstr(commandLine, L"--self-test"))
        return ui.find("tracks") ? 0 : 1;
    ShowWindow(hwnd, nShow);

    MSG m{};
    while (GetMessage(&m, nullptr, 0, 0)) {
        TranslateMessage(&m);
        DispatchMessage(&m);
    }
    return 0;
}
