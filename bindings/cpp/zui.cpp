// zUI - C++ binding, platform-independent core.
//
// The web-view backend is platform specific and lives elsewhere
// (zui_webview2.cpp for Windows). This file implements Host orchestration and a
// tiny JSON envelope reader/writer so the binding has no external dependency.

#include "zui.h"

#include <cctype>

namespace zui {

// Forward-declared factory provided by the platform backend translation unit.
std::unique_ptr<WebViewBackend> make_default_backend(void* native_parent);

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
    // Object / array / literal: copy until the matching end at depth 0.
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

Host::Host(void* native_parent)
    : backend_(make_default_backend(native_parent)) {
    backend_->set_on_message([this](const std::string& raw) { dispatch(raw); });
    backend_->inject_startup_script(
        "window.__zuiHost={postMessage:function(m){window.chrome.webview.postMessage(m);}};");
}

Host::Host(std::unique_ptr<WebViewBackend> backend)
    : backend_(std::move(backend)) {
    backend_->set_on_message([this](const std::string& raw) { dispatch(raw); });
}

Host::~Host() = default;

void Host::set_core_root(const std::string& path) { core_root_ = path; }

void Host::load(const std::string& relative_path) {
    backend_->map_virtual_host("zui.app", core_root_ + "/..");
    backend_->navigate("https://zui.app/" + relative_path);
}

void Host::send(const std::string& channel, const std::string& payload_json) {
    backend_->post_message(make_envelope(channel, payload_json));
}

void Host::on(const std::string& channel, MessageHandler handler) {
    handlers_[channel].push_back(std::move(handler));
}

void Host::set_theme(const std::string& name) { send("theme", "\"" + name + "\""); }

void Host::dispatch(const std::string& raw_json) {
    std::string channel, payload;
    if (!parse_envelope(raw_json, channel, payload)) return;
    auto it = handlers_.find(channel);
    if (it == handlers_.end()) return;
    for (auto& h : it->second) h(payload);
}

}  // namespace zui
