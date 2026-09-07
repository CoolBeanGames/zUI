#ifndef ZUI_H
#define ZUI_H

#include <functional>
#include <string>
#include <unordered_map>
#include <vector>

namespace zui {

using MessageHandler = std::function<void(const std::string& payload)>;

struct Node {
    std::string kind;
    std::string text;
    std::unordered_map<std::string, std::string> attributes;
    std::vector<Node> children;
};

struct Theme {
    unsigned long window = 0x00161310;
    unsigned long surface = 0x00201b17;
    unsigned long raised = 0x002b2520;
    unsigned long text = 0x00f7f4ee;
    unsigned long muted = 0x00b3aa99;
    unsigned long accent = 0x00e5b533;
    unsigned long border = 0x00453d35;
};

/// Builds compiler-emitted nodes directly as Win32 HWND controls.
class Host {
public:
    explicit Host(void* native_parent);
    ~Host();
    Host(const Host&) = delete;
    Host& operator=(const Host&) = delete;

    void build(const Node& root);
    void on(const std::string& channel, MessageHandler handler);
    void send(const std::string& channel, const std::string& payload = "");
    void set_theme(const std::string& name);
    void* find(const std::string& export_name) const;

    // ----- Incremental native control mutation (see core/RUNTIME_CONTRACT.md) -----
    // Mutates the EXISTING HWND registered under an id/bind/export name. Never
    // recreates a control and never calls build().
    // Generic string API:
    bool set(const std::string& name, const std::string& property, const std::string& value);
    std::string get(const std::string& name, const std::string& property) const;
    // Ergonomic typed helpers:
    bool set_text(const std::string& name, const std::string& text);
    bool set_visible(const std::string& name, bool visible);
    bool set_enabled(const std::string& name, bool enabled);
    bool set_checked(const std::string& name, bool checked);
    bool set_value(const std::string& name, int value);
    bool set_selected(const std::string& name, int index);
    bool set_color(const std::string& name, unsigned long foreground, unsigned long background);
    bool set_size(const std::string& name, int width, int height);
    bool set_focus(const std::string& name);
    std::string get_text(const std::string& name) const;
    bool get_checked(const std::string& name) const;
    int get_value(const std::string& name) const;
    int get_selected(const std::string& name) const;

private:
    void* create_node(void* parent, const Node& node, int& x, int& y, int width);
    void dispatch(const std::string& channel, const std::string& payload);
    void* require(const std::string& name) const;
    static long long subclass_proc(void*, unsigned, unsigned long long, long long, unsigned long long, unsigned long long);

    struct ControlColor { unsigned long fg = 0xffffffff; unsigned long bg = 0xffffffff; void* brush = nullptr; };

    void* parent_ = nullptr;
    int build_count_ = 0;
    Theme theme_{};
    std::unordered_map<std::string, std::vector<MessageHandler>> handlers_;
    std::unordered_map<void*, std::string> control_channels_;
    std::unordered_map<void*, std::string> control_kinds_;
    std::unordered_map<void*, ControlColor> control_colors_;
    std::unordered_map<std::string, void*> exports_;
};

std::string make_envelope(const std::string& channel, const std::string& payload_json);
bool parse_envelope(const std::string& raw, std::string& channel_out, std::string& payload_out);

}  // namespace zui
#endif
