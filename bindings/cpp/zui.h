// zUI - C++ binding (header-only interface, thin implementation in zui.cpp).
//
// Wraps a platform web view and bridges the zUI JSON message bus to C++. The
// same core CSS/JS is used by the C# binding so both render identically.
//
//   zui::Host ui(native_window_handle);
//   ui.set_core_root("zui");
//   ui.load("showcase/index.html");
//   ui.on("save", [](const std::string& json) { save(json); });
//   ui.send("theme", "\"holo\"");
//
// The web-view implementation is pluggable via zui::WebViewBackend so the same
// code targets WebView2 on Windows and WebKitGTK / WKWebView elsewhere.

#ifndef ZUI_H
#define ZUI_H

#include <functional>
#include <memory>
#include <string>
#include <unordered_map>
#include <vector>

namespace zui {

using MessageHandler = std::function<void(const std::string& payload_json)>;

// Abstract web-view the Host drives. A concrete backend is supplied per platform
// (see zui_webview2.cpp for the Windows one).
class WebViewBackend {
public:
    virtual ~WebViewBackend() = default;
    virtual void navigate(const std::string& url) = 0;
    virtual void post_message(const std::string& json) = 0;
    virtual void set_on_message(std::function<void(const std::string&)> cb) = 0;
    virtual void inject_startup_script(const std::string& js) = 0;
    virtual void map_virtual_host(const std::string& host, const std::string& folder) = 0;
};

class Host {
public:
    // Uses the platform default backend for the given native window/parent.
    explicit Host(void* native_parent);
    // Or inject a custom backend (tests, alternative web views).
    explicit Host(std::unique_ptr<WebViewBackend> backend);
    ~Host();

    Host(const Host&) = delete;
    Host& operator=(const Host&) = delete;

    // Folder that contains the copied `zui/` core assets. Default: "zui".
    void set_core_root(const std::string& path);

    // Load a document relative to the core root's parent folder.
    void load(const std::string& relative_path);

    // host -> UI. `payload_json` must be a valid JSON value (or "" for null).
    void send(const std::string& channel, const std::string& payload_json = "");

    // UI -> host. Multiple handlers per channel are allowed.
    void on(const std::string& channel, MessageHandler handler);

    void set_theme(const std::string& name);

private:
    void dispatch(const std::string& raw_json);

    std::unique_ptr<WebViewBackend> backend_;
    std::string core_root_ = "zui";
    std::unordered_map<std::string, std::vector<MessageHandler>> handlers_;
};

// Minimal helpers for building/reading the {channel,payload} envelope without a
// full JSON dependency. Implemented in zui.cpp.
std::string make_envelope(const std::string& channel, const std::string& payload_json);
bool parse_envelope(const std::string& raw, std::string& channel_out, std::string& payload_out);

}  // namespace zui

#endif  // ZUI_H
