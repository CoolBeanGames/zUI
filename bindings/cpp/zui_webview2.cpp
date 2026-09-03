// zUI - Windows WebView2 backend for the C++ binding.
//
// Requires the WebView2 SDK (Microsoft.Web.WebView2 NuGet or the standalone
// SDK) and WIL (Windows Implementation Library). Linked in by the host build;
// see samples/cpp/CMakeLists.txt.
//
// This translation unit provides zui::make_default_backend().

#include "zui.h"

#include <windows.h>
#include <wrl.h>
#include <wil/com.h>
#include <WebView2.h>

#include <functional>
#include <string>

using namespace Microsoft::WRL;

namespace zui {

namespace {

std::wstring widen(const std::string& s) {
    if (s.empty()) return {};
    int n = MultiByteToWideChar(CP_UTF8, 0, s.data(), (int)s.size(), nullptr, 0);
    std::wstring w(n, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, s.data(), (int)s.size(), w.data(), n);
    return w;
}
std::string narrow(const std::wstring& w) {
    if (w.empty()) return {};
    int n = WideCharToMultiByte(CP_UTF8, 0, w.data(), (int)w.size(), nullptr, 0, nullptr, nullptr);
    std::string s(n, '\0');
    WideCharToMultiByte(CP_UTF8, 0, w.data(), (int)w.size(), s.data(), n, nullptr, nullptr);
    return s;
}

class WebView2Backend : public WebViewBackend {
public:
    explicit WebView2Backend(HWND parent) : parent_(parent) {
        CreateCoreWebView2EnvironmentWithOptions(
            nullptr, nullptr, nullptr,
            Callback<ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler>(
                [this](HRESULT, ICoreWebView2Environment* env) -> HRESULT {
                    env_ = env;
                    env->CreateCoreWebView2Controller(
                        parent_,
                        Callback<ICoreWebView2CreateCoreWebView2ControllerCompletedHandler>(
                            [this](HRESULT, ICoreWebView2Controller* controller) -> HRESULT {
                                controller_ = controller;
                                controller_->get_CoreWebView2(webview_.put());

                                RECT rc; GetClientRect(parent_, &rc);
                                controller_->put_Bounds(rc);

                                wil::com_ptr<ICoreWebView2Settings> settings;
                                if (SUCCEEDED(webview_->get_Settings(settings.put())))
                                    settings->put_AreDefaultContextMenusEnabled(FALSE);

                                webview_->AddScriptToExecuteOnDocumentCreated(
                                    L"window.__zuiHost={postMessage:function(m){window.chrome.webview.postMessage(m);}};",
                                    nullptr);

                                webview_->add_WebMessageReceived(
                                    Callback<ICoreWebView2WebMessageReceivedEventHandler>(
                                        [this](ICoreWebView2*, ICoreWebView2WebMessageReceivedEventArgs* args) -> HRESULT {
                                            wil::unique_cotaskmem_string raw;
                                            if (SUCCEEDED(args->TryGetWebMessageAsString(&raw)) && raw && on_msg_)
                                                on_msg_(narrow(raw.get()));
                                            return S_OK;
                                        }).Get(),
                                    &msg_token_);

                                // host -> UI messages only land once the document's JS is
                                // listening; buffer until then.
                                webview_->add_DOMContentLoaded(
                                    Callback<ICoreWebView2DOMContentLoadedEventHandler>(
                                        [this](ICoreWebView2*, ICoreWebView2DOMContentLoadedEventArgs*) -> HRESULT {
                                            dom_ready_ = true;
                                            for (auto& m : pending_msgs_)
                                                webview_->PostWebMessageAsString(widen(m).c_str());
                                            pending_msgs_.clear();
                                            return S_OK;
                                        }).Get(),
                                    &dom_token_);
                                webview_->add_NavigationStarting(
                                    Callback<ICoreWebView2NavigationStartingEventHandler>(
                                        [this](ICoreWebView2*, ICoreWebView2NavigationStartingEventArgs*) -> HRESULT {
                                            dom_ready_ = false;
                                            return S_OK;
                                        }).Get(),
                                    &nav_token_);

                                flush_pending();
                                return S_OK;
                            }).Get());
                    return S_OK;
                }).Get());
    }

    void navigate(const std::string& url) override {
        if (webview_) webview_->Navigate(widen(url).c_str());
        else pending_nav_ = url;
    }

    void post_message(const std::string& json) override {
        if (webview_ && dom_ready_) webview_->PostWebMessageAsString(widen(json).c_str());
        else pending_msgs_.push_back(json);
    }

    void set_on_message(std::function<void(const std::string&)> cb) override { on_msg_ = std::move(cb); }

    void inject_startup_script(const std::string& js) override {
        if (webview_) webview_->AddScriptToExecuteOnDocumentCreated(widen(js).c_str(), nullptr);
        else pending_scripts_.push_back(js);
    }

    void map_virtual_host(const std::string& host, const std::string& folder) override {
        wil::com_ptr<ICoreWebView2_3> wv3;
        if (webview_ && SUCCEEDED(webview_->QueryInterface(IID_PPV_ARGS(wv3.put())))) {
            wv3->SetVirtualHostNameToFolderMapping(
                widen(host).c_str(), widen(folder).c_str(),
                COREWEBVIEW2_HOST_RESOURCE_ACCESS_KIND_ALLOW);
        } else {
            pending_hosts_.push_back({host, folder});
        }
    }

private:
    void flush_pending() {
        for (auto& s : pending_scripts_) inject_startup_script(s);
        for (auto& h : pending_hosts_) map_virtual_host(h.first, h.second);
        pending_scripts_.clear(); pending_hosts_.clear();
        if (!pending_nav_.empty()) { navigate(pending_nav_); pending_nav_.clear(); }
        // pending_msgs_ are flushed on DOMContentLoaded, not here.
    }

    HWND parent_;
    wil::com_ptr<ICoreWebView2Environment> env_;
    wil::com_ptr<ICoreWebView2Controller> controller_;
    wil::com_ptr<ICoreWebView2> webview_;
    EventRegistrationToken msg_token_{};
    EventRegistrationToken dom_token_{};
    EventRegistrationToken nav_token_{};
    bool dom_ready_ = false;
    std::function<void(const std::string&)> on_msg_;

    std::string pending_nav_;
    std::vector<std::string> pending_msgs_;
    std::vector<std::string> pending_scripts_;
    std::vector<std::pair<std::string, std::string>> pending_hosts_;
};

}  // namespace

std::unique_ptr<WebViewBackend> make_default_backend(void* native_parent) {
    return std::make_unique<WebView2Backend>(static_cast<HWND>(native_parent));
}

}  // namespace zui
