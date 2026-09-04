// zUI - minimal C++ host sample (Win32 + WebView2).
//
// Opens the zUI showcase through zui::Host and round-trips the same messages as
// the C# sample, so both render an identical UI.
//
// Build: see CMakeLists.txt (needs the WebView2 SDK + WIL).

#include "zui.h"

#include <windows.h>
#include <objbase.h>
#include <string>

static zui::Host* g_ui = nullptr;

LRESULT CALLBACK WndProc(HWND hwnd, UINT msg, WPARAM w, LPARAM l) {
    switch (msg) {
        case WM_DESTROY:
            PostQuitMessage(0);
            return 0;
    }
    return DefWindowProc(hwnd, msg, w, l);
}

int WINAPI wWinMain(HINSTANCE hInst, HINSTANCE, PWSTR, int nShow) {
    // WebView2 requires a single-threaded apartment on the UI thread.
    CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);

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
    ui.set_core_root("zui");

    ui.on("selection", [](const std::string& json) {
        OutputDebugStringA(("selection: " + json + "\n").c_str());
    });
    ui.on("theme-changed", [](const std::string& json) {
        OutputDebugStringA(("theme: " + json + "\n").c_str());
    });
    ui.on("transport", [](const std::string& json) {
        OutputDebugStringA(("transport: " + json + "\n").c_str());
    });

    ui.load("showcase/index.html");
    ui.send("device", R"({"name":"HAPTICS' IPOD","capacity":"238.2 GB","free":"234.6 GB"})");
    ui.send("now-playing", R"({"title":"Nightdrive","sub":"Aria Kane - Long Exposure","position":108,"duration":281})");

    ShowWindow(hwnd, nShow);

    MSG m{};
    while (GetMessage(&m, nullptr, 0, 0)) {
        TranslateMessage(&m);
        DispatchMessage(&m);
    }
    return 0;
}
