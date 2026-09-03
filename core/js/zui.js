/* zUI runtime.
 *
 * Provides:
 *   - zui.send(channel, payload)      UI  -> host
 *   - zui.receive(channel, handler)   host -> UI
 *   - zui.setTheme(name)
 *   - zui.toast(message, {kind, timeout})
 *   - zui.menu(items, x, y)           show a context/popover menu
 *   - automatic wiring for [data-zui] components on DOMContentLoaded
 *
 * The host bridge is deliberately transport-agnostic. Bindings inject a
 * `window.__zuiHost.postMessage` function; when absent (e.g. opening the
 * showcase directly in a browser) messages are logged instead.
 */
(function (global) {
  "use strict";

  var listeners = Object.create(null);

  function hostPost(msg) {
    var h = global.__zuiHost;
    if (h && typeof h.postMessage === "function") { h.postMessage(msg); return; }
    if (global.chrome && chrome.webview && chrome.webview.postMessage) { chrome.webview.postMessage(msg); return; }
    console.debug("[zui] host message (no bridge):", msg);
  }

  var zui = {
    version: "0.1.0",

    send: function (channel, payload) {
      hostPost(JSON.stringify({ channel: channel, payload: payload === undefined ? null : payload }));
    },

    receive: function (channel, handler) {
      (listeners[channel] || (listeners[channel] = [])).push(handler);
      return function () {
        listeners[channel] = (listeners[channel] || []).filter(function (h) { return h !== handler; });
      };
    },

    /* Called by the binding (or window message events) when the host speaks. */
    _dispatch: function (raw) {
      var msg;
      try { msg = typeof raw === "string" ? JSON.parse(raw) : raw; }
      catch (e) { console.warn("[zui] bad host message", raw); return; }
      (listeners[msg.channel] || []).forEach(function (h) {
        try { h(msg.payload); } catch (e) { console.error("[zui] handler error", e); }
      });
    },

    setTheme: function (name) {
      document.documentElement.setAttribute("data-zui-theme", name);
      zui.send("theme-changed", name);
    },

    toast: function (message, opts) {
      opts = opts || {};
      var host = document.querySelector(".zui-toasts");
      if (!host) { host = document.createElement("div"); host.className = "zui-toasts"; document.body.appendChild(host); }
      var el = document.createElement("div");
      el.className = "zui-toast" + (opts.kind ? " zui-toast--" + opts.kind : "");
      el.textContent = message;
      host.appendChild(el);
      setTimeout(function () { el.remove(); }, opts.timeout || 4000);
      return el;
    },

    menu: function (items, x, y) {
      closeMenus();
      var el = document.createElement("div");
      el.className = "zui-menu";
      items.forEach(function (it) {
        if (it === "-" || it.separator) {
          var s = document.createElement("div"); s.className = "zui-menu__sep"; el.appendChild(s); return;
        }
        var mi = document.createElement("div");
        mi.className = "zui-menu__item" + (it.checked ? " zui-menu__item--checked" : "");
        if (it.disabled) mi.setAttribute("aria-disabled", "true");
        mi.textContent = it.label;
        if (it.shortcut) {
          var sc = document.createElement("span"); sc.className = "zui-menu__shortcut"; sc.textContent = it.shortcut; mi.appendChild(sc);
        }
        mi.addEventListener("click", function () {
          if (it.disabled) return;
          closeMenus();
          if (typeof it.action === "function") it.action();
          else if (it.channel) zui.send(it.channel, it.payload);
        });
        el.appendChild(mi);
      });
      document.body.appendChild(el);
      var w = el.offsetWidth, h = el.offsetHeight;
      el.style.left = Math.min(x, innerWidth - w - 4) + "px";
      el.style.top = Math.min(y, innerHeight - h - 4) + "px";
      openMenu = el;
      return el;
    }
  };

  var openMenu = null;
  function closeMenus() { if (openMenu) { openMenu.remove(); openMenu = null; } }
  document.addEventListener("mousedown", function (e) {
    if (openMenu && !openMenu.contains(e.target)) closeMenus();
  });
  document.addEventListener("keydown", function (e) { if (e.key === "Escape") closeMenus(); });

  /* --------------------------------------------------------------------- */
  /* Declarative component wiring                                          */
  /* --------------------------------------------------------------------- */

  function wire(root) {
    root = root || document;

    /* Tabs / nav: [data-zui="tabs"] container, [data-zui-tab="id"] triggers,
       [data-zui-tabpanel="id"] panels. */
    root.querySelectorAll('[data-zui="tabs"]').forEach(function (group) {
      group.addEventListener("click", function (e) {
        var t = e.target.closest("[data-zui-tab]");
        if (!t || !group.contains(t)) return;
        var id = t.getAttribute("data-zui-tab");
        var owned = Array.prototype.map.call(
          group.querySelectorAll("[data-zui-tab]"),
          function (x) { return x.getAttribute("data-zui-tab"); });
        group.querySelectorAll("[data-zui-tab]").forEach(function (x) {
          x.classList.toggle("zui-active", x === t);
        });
        document.querySelectorAll("[data-zui-tabpanel]").forEach(function (p) {
          var pid = p.getAttribute("data-zui-tabpanel");
          if (owned.indexOf(pid) === -1) return;      // not controlled by this group
          p.classList.toggle("zui-active", pid === id);
        });
        zui.send("tab", id);
      });
    });

    /* Select / dropdown: <div class="zui-select" data-zui="select"> with a
       nested <template class="zui-menu-items"> or data-options JSON. */
    root.querySelectorAll('[data-zui="select"]').forEach(function (sel) {
      if (sel.__zuiWired) return; sel.__zuiWired = true;
      sel.setAttribute("tabindex", "0");
      sel.addEventListener("click", function () {
        var opts;
        try { opts = JSON.parse(sel.getAttribute("data-options") || "[]"); } catch (e) { opts = []; }
        var r = sel.getBoundingClientRect();
        sel.setAttribute("aria-expanded", "true");
        var m = zui.menu(opts.map(function (o) {
          var label = typeof o === "string" ? o : o.label;
          return {
            label: label,
            action: function () {
              sel.querySelector(".zui-select__value")
                ? (sel.querySelector(".zui-select__value").textContent = label)
                : (sel.firstChild ? sel.firstChild.textContent = label : sel.textContent = label);
              sel.setAttribute("data-value", typeof o === "string" ? o : o.value);
              zui.send("select", { name: sel.getAttribute("data-name"), value: sel.getAttribute("data-value") });
            }
          };
        }), r.left, r.bottom);
        m.style.minWidth = r.width + "px";
        var clear = function () { sel.setAttribute("aria-expanded", "false"); document.removeEventListener("mousedown", clear); };
        setTimeout(function () { document.addEventListener("mousedown", clear); });
      });
    });

    /* Menu bar: [data-zui="menubar"] with items carrying data-menu (JSON). */
    root.querySelectorAll('[data-zui="menubar"] .zui-menubar__item').forEach(function (item) {
      if (item.__zuiWired) return; item.__zuiWired = true;
      item.addEventListener("click", function () {
        var spec;
        try { spec = JSON.parse(item.getAttribute("data-menu") || "[]"); } catch (e) { spec = []; }
        var r = item.getBoundingClientRect();
        item.setAttribute("aria-expanded", "true");
        zui.menu(spec, r.left, r.bottom);
        var clear = function () { item.setAttribute("aria-expanded", "false"); document.removeEventListener("mousedown", clear); };
        setTimeout(function () { document.addEventListener("mousedown", clear); });
      });
    });

    /* Splitters: a .zui-splitter between two flex siblings resizes the pane on
       its "primary" side (previous sibling for --v, previous for --h) by setting
       an explicit flex-basis. Double-click resets. */
    root.querySelectorAll(".zui-splitter").forEach(function (sp) {
      if (sp.__zuiSplit) return; sp.__zuiSplit = true;
      var vertical = !sp.classList.contains("zui-splitter--h"); // --v = column resize
      var prev = sp.previousElementSibling;
      if (!prev) return;
      var startBasis = null;
      sp.addEventListener("mousedown", function (e) {
        e.preventDefault();
        var rect = prev.getBoundingClientRect();
        var start = vertical ? e.clientX : e.clientY;
        var base = vertical ? rect.width : rect.height;
        document.body.classList.add(vertical ? "zui-cursor-col" : "zui-cursor-row");
        function move(ev) {
          var delta = (vertical ? ev.clientX : ev.clientY) - start;
          var next = Math.max(48, base + delta);
          prev.style.flex = "0 0 " + next + "px";
        }
        function up() {
          document.removeEventListener("mousemove", move);
          document.removeEventListener("mouseup", up);
          document.body.classList.remove("zui-cursor-col", "zui-cursor-row");
          zui.send("splitter", { basis: parseInt(prev.style.flexBasis || prev.style.flex, 10) || null });
        }
        document.addEventListener("mousemove", move);
        document.addEventListener("mouseup", up);
      });
      sp.addEventListener("dblclick", function () { prev.style.flex = startBasis || ""; });
    });

    /* Context menus: any element with data-zui-context (JSON menu spec). */
    root.querySelectorAll("[data-zui-context]").forEach(function (el) {
      if (el.__zuiCtx) return; el.__zuiCtx = true;
      el.addEventListener("contextmenu", function (e) {
        var spec;
        try { spec = JSON.parse(el.getAttribute("data-zui-context") || "[]"); } catch (x) { return; }
        e.preventDefault();
        zui.menu(spec, e.clientX, e.clientY);
      });
    });

    /* Tooltips: [data-zui-tip="text"]. */
    root.querySelectorAll("[data-zui-tip]").forEach(function (el) {
      if (el.__zuiTip) return; el.__zuiTip = true;
      var tip;
      el.addEventListener("mouseenter", function () {
        tip = document.createElement("div");
        tip.className = "zui-tooltip";
        tip.textContent = el.getAttribute("data-zui-tip");
        document.body.appendChild(tip);
        var r = el.getBoundingClientRect();
        tip.style.left = r.left + "px";
        tip.style.top = (r.bottom + 4) + "px";
      });
      el.addEventListener("mouseleave", function () { if (tip) { tip.remove(); tip = null; } });
    });

    /* Drop targets: .zui-drop elements emit a "drop" channel message. */
    root.querySelectorAll(".zui-drop").forEach(function (el) {
      if (el.__zuiDrop) return; el.__zuiDrop = true;
      ["dragenter", "dragover"].forEach(function (ev) {
        el.addEventListener(ev, function (e) { e.preventDefault(); el.classList.add("zui-drop--over"); });
      });
      ["dragleave", "drop"].forEach(function (ev) {
        el.addEventListener(ev, function () { el.classList.remove("zui-drop--over"); });
      });
      el.addEventListener("drop", function (e) {
        e.preventDefault();
        var files = Array.prototype.map.call(e.dataTransfer.files || [], function (f) { return f.name; });
        zui.send("drop", { target: el.getAttribute("data-name") || null, files: files });
      });
    });

    /* Table / list selection with Ctrl and Shift. */
    root.querySelectorAll('[data-zui="selectable"]').forEach(function (container) {
      if (container.__zuiSel) return; container.__zuiSel = true;
      var rows = function () { return Array.prototype.slice.call(container.querySelectorAll("[data-zui-row]")); };
      var anchor = null;
      container.addEventListener("click", function (e) {
        var row = e.target.closest("[data-zui-row]");
        if (!row) return;
        var all = rows();
        if (e.shiftKey && anchor) {
          var a = all.indexOf(anchor), b = all.indexOf(row);
          all.forEach(function (r, i) { r.classList.toggle("zui-selected", i >= Math.min(a, b) && i <= Math.max(a, b)); });
        } else if (e.ctrlKey || e.metaKey) {
          row.classList.toggle("zui-selected"); anchor = row;
        } else {
          all.forEach(function (r) { r.classList.toggle("zui-selected", r === row); }); anchor = row;
        }
        zui.send("selection", rows().filter(function (r) { return r.classList.contains("zui-selected"); })
          .map(function (r) { return r.getAttribute("data-zui-row"); }));
      });
    });
  }

  zui.wire = wire;

  document.addEventListener("DOMContentLoaded", function () { wire(document); });

  /* Standard browser message channel (used by some web-view hosts). */
  global.addEventListener("message", function (e) {
    if (e.data && (e.data.channel || typeof e.data === "string")) zui._dispatch(e.data);
  });
  if (global.chrome && chrome.webview) {
    chrome.webview.addEventListener("message", function (e) { zui._dispatch(e.data); });
  }

  global.zui = zui;
})(window);
