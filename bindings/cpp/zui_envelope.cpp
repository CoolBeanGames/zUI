// zUI - the {channel,payload} envelope reader/writer.
//
// Split out from zui.cpp so it has NO dependency on a web-view backend and can
// be linked (and unit-tested) on its own.

#include "zui.h"

#include <cctype>

namespace zui {
namespace {

std::string json_escape(const std::string& s) {
    std::string out;
    out.reserve(s.size() + 8);
    for (char c : s) {
        switch (c) {
            case '"':  out += "\\\""; break;
            case '\\': out += "\\\\"; break;
            case '\n': out += "\\n";  break;
            case '\r': out += "\\r";  break;
            case '\t': out += "\\t";  break;
            default:   out += c;      break;
        }
    }
    return out;
}

// Extract the raw JSON value for `key` from a flat object. Good enough for the
// two-field envelope; not a general parser.
bool extract_value(const std::string& obj, const std::string& key, std::string& out) {
    const std::string needle = "\"" + key + "\"";
    auto k = obj.find(needle);
    if (k == std::string::npos) return false;
    auto colon = obj.find(':', k + needle.size());
    if (colon == std::string::npos) return false;
    size_t i = colon + 1;
    while (i < obj.size() && std::isspace(static_cast<unsigned char>(obj[i]))) ++i;
    if (i >= obj.size()) return false;

    if (obj[i] == '"') {
        size_t j = i + 1;
        std::string val;
        while (j < obj.size() && obj[j] != '"') {
            if (obj[j] == '\\' && j + 1 < obj.size()) { val += obj[j + 1]; j += 2; continue; }
            val += obj[j++];
        }
        out = val;
        return true;
    }
    int depth = 0;
    size_t j = i;
    for (; j < obj.size(); ++j) {
        char c = obj[j];
        if (c == '{' || c == '[') ++depth;
        else if (c == '}' || c == ']') { if (depth == 0) break; --depth; }
        else if (c == ',' && depth == 0) break;
    }
    out = obj.substr(i, j - i);
    return true;
}

}  // namespace

std::string make_envelope(const std::string& channel, const std::string& payload_json) {
    std::string payload = payload_json.empty() ? "null" : payload_json;
    return "{\"channel\":\"" + json_escape(channel) + "\",\"payload\":" + payload + "}";
}

bool parse_envelope(const std::string& raw, std::string& channel_out, std::string& payload_out) {
    if (!extract_value(raw, "channel", channel_out)) return false;
    if (!extract_value(raw, "payload", payload_out)) payload_out = "null";
    return true;
}

}  // namespace zui
