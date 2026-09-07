#include "../zui.h"

#include <iostream>
#include <memory>
#include <string>
#include <vector>

class MockBackend final : public zui::WebViewBackend {
public:
    void navigate(const std::string& url) override { navigated = url; }
    void post_message(const std::string& json) override { messages.push_back(json); }
    void set_on_message(std::function<void(const std::string&)> cb) override { on_message = std::move(cb); }
    void inject_startup_script(const std::string& js) override { scripts.push_back(js); }
    void map_virtual_host(const std::string&, const std::string&) override {}

    std::string navigated;
    std::vector<std::string> messages;
    std::vector<std::string> scripts;
    std::function<void(const std::string&)> on_message;
};

// zui.cpp also contains the native-parent constructor. This test injects its
// backend, but supplies the platform factory symbol so the portable zui target
// can link without WebView2.
namespace zui {
std::unique_ptr<WebViewBackend> make_default_backend(void*) { return {}; }
}

int main() {
    auto mock = std::make_unique<MockBackend>();
    auto* inspect = mock.get();
    zui::Host host(std::move(mock));
    if (inspect->scripts.size() != 1 ||
        inspect->scripts[0].find("window.__zuiHost") == std::string::npos ||
        !inspect->on_message) {
        std::cerr << "injected backend was not configured like the default backend\n";
        return 1;
    }

    bool dispatched = false;
    host.on("ready", [&](const std::string& payload) { dispatched = payload == "true"; });
    inspect->on_message("{\"channel\":\"ready\",\"payload\":true}");
    if (!dispatched) {
        std::cerr << "injected backend message callback was not connected\n";
        return 1;
    }
    std::cout << "host_test: all checks passed\n";
    return 0;
}
