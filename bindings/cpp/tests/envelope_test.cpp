// Minimal self-contained check for the envelope reader/writer.
#include "../zui.h"

#include <cassert>
#include <iostream>

int main() {
    auto env = zui::make_envelope("save", "{\"id\":7}");
    assert(env == "{\"channel\":\"save\",\"payload\":{\"id\":7}}");

    std::string ch, payload;
    assert(zui::parse_envelope(env, ch, payload));
    assert(ch == "save");
    assert(payload == "{\"id\":7}");

    assert(zui::parse_envelope("{\"channel\":\"tab\",\"payload\":\"music\"}", ch, payload));
    assert(ch == "tab");
    assert(payload == "music");

    std::cout << "envelope_test ok\n";
    return 0;
}
