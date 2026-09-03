// Self-contained check for the envelope reader/writer. Uses explicit checks
// (not assert) so it still runs under NDEBUG / Release.
#include "../zui.h"

#include <iostream>
#include <string>

static int failures = 0;
static void check(bool cond, const char* what) {
    if (!cond) { std::cerr << "FAIL: " << what << "\n"; ++failures; }
}

int main() {
    auto env = zui::make_envelope("save", "{\"id\":7}");
    check(env == "{\"channel\":\"save\",\"payload\":{\"id\":7}}", "make_envelope object payload");

    std::string ch, payload;
    check(zui::parse_envelope(env, ch, payload), "parse_envelope ok");
    check(ch == "save", "channel");
    check(payload == "{\"id\":7}", "object payload round-trips");

    check(zui::parse_envelope("{\"channel\":\"tab\",\"payload\":\"music\"}", ch, payload), "parse string payload");
    check(ch == "tab" && payload == "music", "string payload");

    check(zui::make_envelope("x", "") == "{\"channel\":\"x\",\"payload\":null}", "empty payload -> null");

    check(!zui::parse_envelope("not json", ch, payload), "rejects garbage");

    if (failures == 0) std::cout << "envelope_test: all checks passed\n";
    return failures == 0 ? 0 : 1;
}
