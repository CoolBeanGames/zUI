// zUI - C++ binding, Host orchestration.
//
// The {channel,payload} helpers are in zui_envelope.cpp. The web-view backend is
// platform specific (zui_webview2.cpp for Windows); an executable that
// constructs `Host(void*)` must link one, or pass its own WebViewBackend.

#include "zui.h"

#include <fstream>

namespace zui {

// Provided by the platform backend translation unit (zui_webview2.cpp).
std::unique_ptr<WebViewBackend> make_default_backend(void* native_parent);

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

void Host::load_document(const std::string& html) {
    const std::string file = core_root_ + "/../__zui_compiled.html";
    { std::ofstream(file, std::ios::binary) << html; }
    load("__zui_compiled.html");
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
