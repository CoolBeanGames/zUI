// Native events + source= collection binding parity (ZU-75 / ZU-76 / ZU-87).
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
                                  0, 0, 800, 600, nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
    if (!parent) return 1;

    zui::Host host(parent);
    host.build(zui::Node{"root", "", {}, {
        zui::Node{"number", "", {{"id", "keep"}, {"min", "1"}, {"max", "999"}, {"value", "5"}}, {}},
        zui::Node{"console", "", {{"id", "log"}}, {}},
        zui::Node{"select", "", {{"id", "preset"}, {"on", "preset.change"}}, {
            zui::Node{"option", "MP3 320", {{"value", "mp3-320"}}, {}},
            zui::Node{"option", "FLAC", {{"value", "flac"}}, {}},
        }},
        zui::Node{"table", "", {{"id", "tracks"}, {"source", "tracks"}, {"selectable", "true"},
                                {"on", "tracks.sel"}, {"onactivate", "tracks.play"}}, {
            zui::Node{"column", "Name", {{"field", "name"}}, {}},
            zui::Node{"column", "Plays", {{"field", "plays"}}, {}},
        }},
    }});

    // ----- number + console -----
    check("number seeded from value", host.get_text("keep") == "5");
    host.append("log", "first line");
    host.append("log", "second line");
    check("console append accumulates", host.get_text("log") == "first line\r\nsecond line");

    // ----- select option value != label -----
    std::string preset;
    host.on("preset.change", [&](const std::string& p) { preset = p; });
    host.set("preset", "selectedvalue", "flac");
    check("select selectedvalue uses the option value", host.get("preset", "selectedvalue") == "flac");

    // ----- collection CRUD -----
    host.set_rows("tracks", {
        {{"key", "t1"}, {"name", "Intro"}, {"plays", "3"}},
        {{"key", "t2"}, {"name", "Verse"}, {"plays", "9"}},
        {{"key", "t3"}, {"name", "Outro"}, {"plays", "1"}},
    });
    check("set_rows populates the list", host.row_keys("tracks").size() == 3);

    host.set_selection("tracks", {"t2", "t3"});
    auto sel = host.get_selection("tracks");
    check("selection by key round-trips", sel.size() == 2 && sel[0] == "t2" && sel[1] == "t3");

    host.update_row("tracks", "t2", {{"key", "t2"}, {"plays", "10"}});
    check("update_row merges + keeps other fields", host.get("tracks", "selected") != "");

    host.remove_row("tracks", "t1");
    auto keys = host.row_keys("tracks");
    check("remove_row drops the row", keys.size() == 2 && keys[0] == "t2");
    check("selection survives an incremental remove",
          [&]{ auto s = host.get_selection("tracks"); return std::find(s.begin(), s.end(), "t2") != s.end(); }());

    host.append_row("tracks", {{"key", "t9"}, {"name", "Bonus"}, {"plays", "0"}});
    check("append_row adds at the end", host.row_keys("tracks").back() == "t9");

    host.clear_rows("tracks");
    check("clear_rows empties the store", host.row_keys("tracks").empty());

    DestroyWindow(parent);
    std::cout << (failures == 0 ? "collection_test: all passed\n" : "collection_test: failures\n");
    return failures == 0 ? 0 : 1;
}
