# C++ native runtime

Link the `zui` CMake target and construct `zui::Host` with a parent HWND. A
generated `build_ui` function passes a `zui::Node` tree to the host, which creates
Win32 and common-control HWNDs. Event callbacks are ordinary C++ functions.

Dependencies are the Windows SDK libraries `user32`, `gdi32`, and `comctl32`.
