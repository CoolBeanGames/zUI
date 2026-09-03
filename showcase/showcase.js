/* Showcase behaviour - exercises the zUI message bus with no host attached. */

/* A tiny demo "host" so the Component IO panel round-trips with no backend:
   it receives UI->host messages and reacts the way a real host would. */
(function () {
  var clicks = 0;
  window.__zuiHost = {
    postMessage: function (raw) {
      var msg = JSON.parse(raw);
      if (msg.channel === "value") {
        var p = msg.payload;
        if (p.id === "io-name") zui.set("io-echo", p.value ? 'Host received: "' + p.value + '"' : "—");
        if (p.id === "io-go" && p.event === "click") zui.set("io-count", (++clicks) + " clicks");
      }
      if (msg.channel === "submit") {
        zui.toast("submit " + msg.payload.form + ": " + JSON.stringify(msg.payload.values), { kind: "ok" });
      }
    }
  };
})();

zui.receive("view.theme", function (name) { zui.setTheme(name); });
zui.receive("help.about", function () {
  zui.toast("zUI " + zui.version + " - holo theme", { kind: "ok" });
});
zui.receive("file.new", function () { zui.toast("New"); });
zui.receive("file.open", function () { zui.toast("Open..."); });

/* Sidebar selection (single-select). */
document.querySelectorAll(".zui-sidebar__item").forEach(function (item) {
  item.addEventListener("click", function () {
    document.querySelectorAll(".zui-sidebar__item").forEach(function (i) { i.classList.remove("zui-active"); });
    item.classList.add("zui-active");
    zui.send("navigate", item.textContent.trim());
  });
});

/* Reflect selection + drop events from the runtime. */
zui.receive("selection", function (ids) {
  if (ids && ids.length) zui.toast(ids.length + " track(s) selected");
});
zui.receive("drop", function (info) {
  zui.toast("Added " + (info.files.length || 0) + " file(s) to " + info.target);
});

/* Virtualised 10k-row list. */
window.bigListApi = null;
document.addEventListener("DOMContentLoaded", function () {
  var host = document.getElementById("bigList");
  if (!host || !window.zui || !zui.virtualList) return;
  var NAMES = ["Nightdrive", "Paper Cranes", "Coastline", "Quartz", "Afterimage", "Slow Signal"];
  window.bigListApi = zui.virtualList(host, {
    count: 10000, rowHeight: 24,
    render: function (i) {
      var row = document.createElement("div");
      row.className = "zui-list__item zui-t-truncate";
      row.style.display = "flex";
      row.innerHTML = '<span style="width:56px;color:var(--zui-text-secondary)">' + (i + 1) +
        '</span><span>' + NAMES[i % NAMES.length] + " #" + i + "</span>";
      return row;
    }
  });
});

/* Simulated device connect/disconnect from a host. */
zui.receive("device", function (dev) {
  var bar = document.getElementById("deviceBar");
  if (!dev) {
    bar.classList.remove("zui-statusbar--connected");
    document.getElementById("deviceName").textContent = "No device connected";
    return;
  }
  bar.classList.add("zui-statusbar--connected");
  document.getElementById("deviceName").textContent = dev.name;
});
