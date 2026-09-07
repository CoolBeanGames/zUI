// generated from ZSL/ZML to native Win32 controls - do not edit.
#include "zui.h"
#include <string>
#include <unordered_map>

void build_ui(zui::Host& host,
    const std::unordered_map<std::string, zui::MessageHandler>& handlers) {
    host.build(zui::Node{"root", "", {}, {
        zui::Node{"window", "zUI Showcase", {}, {zui::Node{"menubar", "", {}, {zui::Node{"menu", "File", {}, {zui::Node{"item", "New", {{"shortcut", "Ctrl+N"}, {"on", "file.new"}}, {}}, zui::Node{"item", "Open...", {{"shortcut", "Ctrl+O"}, {"on", "file.open"}}, {}}, zui::Node{"sep", "", {}, {}}, zui::Node{"item", "Exit", {{"on", "file.exit"}}, {}}}}, zui::Node{"menu", "View", {}, {zui::Node{"item", "Holo (dark)", {{"on", "view.holo"}}, {}}, zui::Node{"item", "Clean (light)", {{"on", "view.clean"}}, {}}}}, zui::Node{"menu", "Help", {}, {zui::Node{"item", "About zUI", {{"on", "help.about"}}, {}}}}}}, zui::Node{"nav", "", {{"bind", "section"}}, {zui::Node{"item", "Components", {{"value", "components"}, {"active", "true"}}, {}}, zui::Node{"item", "Forms", {{"value", "forms"}}, {}}, zui::Node{"item", "Data", {{"value", "data"}}, {}}}}, zui::Node{"workspace", "", {}, {zui::Node{"sidebar", "", {}, {zui::Node{"section-label", "Library", {}, {}}, zui::Node{"item", "Artists", {{"active", "true"}}, {}}, zui::Node{"item", "Albums", {}, {}}, zui::Node{"item", "Songs", {}, {}}, zui::Node{"section-label", "Playlists", {}, {}}, zui::Node{"item", "Recently Added", {}, {}}}}, zui::Node{"fill", "", {}, {zui::Node{"panel", "Tracks", {}, {zui::Node{"table", "", {{"id", "tracks"}, {"source", "tracks"}, {"selectable", "true"}}, {zui::Node{"column", "#", {{"field", "index"}}, {}}, zui::Node{"column", "Name", {{"field", "name"}}, {}}, zui::Node{"column", "Artist", {{"field", "artist"}}, {}}, zui::Node{"column", "Plays", {{"field", "plays"}}, {}}}}}}, zui::Node{"row", "", {}, {zui::Node{"button", "Edit metadata", {{"on", "track.edit"}}, {}}, zui::Node{"button", "New playlist", {{"kind", "primary"}, {"on", "playlist.new"}}, {}}, zui::Node{"spinner", "", {}, {}}, zui::Node{"progress", "", {{"bind", "scan"}}, {}}}}}}}}, zui::Node{"statusbar", "", {}, {zui::Node{"text", "HAPTICS' IPOD", {}, {}}, zui::Node{"text", "234.6 GB free", {}, {}}}}}}
    }});
    host.state().init("section", "components");
    host.state().init("scan", "40");
    host.state().init("tracks", "[]");
    host.bind("section", "section");
    host.bind("scan", "scan");
    if (auto it = handlers.find("file.exit"); it != handlers.end()) host.on("file.exit", it->second);
    if (auto it = handlers.find("file.new"); it != handlers.end()) host.on("file.new", it->second);
    if (auto it = handlers.find("file.open"); it != handlers.end()) host.on("file.open", it->second);
    if (auto it = handlers.find("help.about"); it != handlers.end()) host.on("help.about", it->second);
    host.on("playlist.new", [&host, &handlers](const std::string& p) {
        host.send("playlist.new", "");
        if (auto it = handlers.find("playlist.new"); it != handlers.end()) it->second(p);
    });
    if (auto it = handlers.find("track.edit"); it != handlers.end()) host.on("track.edit", it->second);
    if (auto it = handlers.find("view.clean"); it != handlers.end()) host.on("view.clean", it->second);
    if (auto it = handlers.find("view.holo"); it != handlers.end()) host.on("view.holo", it->second);
    host.state().flush();
}
