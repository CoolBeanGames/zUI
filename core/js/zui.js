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

    /* Toggle a button's busy spinner (keeps its box size, disables it). */
    busy: function (el, on) {
      if (typeof el === "string") el = document.querySelector(el);
      if (el) el.classList.toggle("zui-btn--busy", on !== false);
    },

    /* Central cursor manager. A stack so nested operations restore correctly:
     *   var done = zui.cursor.push("busy");  ... ; done();
     *   zui.cursor("forbidden", el)          // one-off on an element
     *   await zui.cursor.while("busy", fn)   // scoped to a promise
     * Valid names: drag link text busy forbidden col row  (see tooltip.css)
     */
    cursor: (function () {
      var stack = [];
      var CLASSES = ["zui-cursor-drag", "zui-cursor-link", "zui-cursor-text",
        "zui-cursor-busy", "zui-cursor-forbidden", "zui-cursor-col", "zui-cursor-row"];
      function apply() {
        var b = document.body;
        if (!b) return;
        CLASSES.forEach(function (c) { b.classList.remove(c); });
        var top = stack[stack.length - 1];
        if (top) b.classList.add("zui-cursor-" + top);
      }
      function fn(name, el) {
        if (el) { // one-off, caller owns cleanup
          CLASSES.forEach(function (c) { el.classList.remove(c); });
          if (name) el.classList.add("zui-cursor-" + name);
          return;
        }
        stack = name ? [name] : [];
        apply();
      }
      fn.push = function (name) {
        stack.push(name); apply();
        var popped = false;
        return function () { if (popped) return; popped = true; var i = stack.lastIndexOf(name); if (i !== -1) stack.splice(i, 1); apply(); };
      };
      fn.pop = function () { stack.pop(); apply(); };
      fn.clear = function () { stack = []; apply(); };
      fn.while = function (name, work) {
        var done = fn.push(name);
        var r;
        try { r = typeof work === "function" ? work() : work; }
        catch (e) { done(); throw e; }
        return Promise.resolve(r).then(
          function (v) { done(); return v; },
          function (e) { done(); throw e; });
      };
      return fn;
    })(),

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
      menuActive = -1;
      el.setAttribute("tabindex", "-1");
      el.focus();
      return el;
    }
  };

  var openMenu = null;
  var menuActive = -1;
  var typeahead = "";
  var typeaheadTimer = null;

  function closeMenus() {
    if (openMenu) { openMenu.remove(); openMenu = null; menuActive = -1; }
  }

  function menuItems() {
    return openMenu
      ? Array.prototype.filter.call(openMenu.querySelectorAll(".zui-menu__item"),
          function (i) { return i.getAttribute("aria-disabled") !== "true"; })
      : [];
  }

  function setMenuActive(i) {
    var items = menuItems();
    if (!items.length) return;
    menuActive = (i + items.length) % items.length;
    items.forEach(function (it, ix) { it.classList.toggle("zui-active", ix === menuActive); });
    items[menuActive].scrollIntoView({ block: "nearest" });
  }

  document.addEventListener("mousedown", function (e) {
    if (openMenu && !openMenu.contains(e.target)) closeMenus();
  });

  document.addEventListener("keydown", function (e) {
    if (!openMenu) return;
    var items = menuItems();
    if (e.key === "Escape") { closeMenus(); e.preventDefault(); }
    else if (e.key === "ArrowDown") { setMenuActive(menuActive + 1); e.preventDefault(); }
    else if (e.key === "ArrowUp") { setMenuActive(menuActive - 1); e.preventDefault(); }
    else if (e.key === "Home") { setMenuActive(0); e.preventDefault(); }
    else if (e.key === "End") { setMenuActive(items.length - 1); e.preventDefault(); }
    else if (e.key === "Enter" || e.key === " ") {
      if (menuActive >= 0 && items[menuActive]) { items[menuActive].click(); e.preventDefault(); }
    } else if (e.key.length === 1) {
      typeahead += e.key.toLowerCase();
      clearTimeout(typeaheadTimer);
      typeaheadTimer = setTimeout(function () { typeahead = ""; }, 600);
      for (var k = 0; k < items.length; k++) {
        if (items[k].textContent.toLowerCase().indexOf(typeahead) === 0) { setMenuActive(k); break; }
      }
    }
  });

  /* --------------------------------------------------------------------- */
  /* Declarative component wiring                                          */
  /* --------------------------------------------------------------------- */

  function wire(root) {
    root = root || document;

    /* Tabs / nav: [data-zui="tabs"] container, [data-zui-tab="id"] triggers,
       [data-zui-tabpanel="id"] panels. Behaves as a native desktop tab strip:
       click / arrow-key selection, roving tabindex, ARIA roles, closeable tabs. */
    root.querySelectorAll('[data-zui="tabs"]').forEach(function (group) {
      if (group.__zuiTabs) return; group.__zuiTabs = true;
      group.setAttribute("role", "tablist");

      function tabs() { return Array.prototype.slice.call(group.querySelectorAll("[data-zui-tab]")); }

      function activate(t) {
        if (!t) return;
        var id = t.getAttribute("data-zui-tab");
        var owned = tabs().map(function (x) { return x.getAttribute("data-zui-tab"); });
        tabs().forEach(function (x) {
          var on = x === t;
          x.classList.toggle("zui-active", on);
          x.setAttribute("role", "tab");
          x.setAttribute("aria-selected", String(on));
          x.tabIndex = on ? 0 : -1;
        });
        document.querySelectorAll("[data-zui-tabpanel]").forEach(function (p) {
          var pid = p.getAttribute("data-zui-tabpanel");
          if (owned.indexOf(pid) === -1) return;
          var on = pid === id;
          p.classList.toggle("zui-active", on);
          p.setAttribute("role", "tabpanel");
          p.hidden = !on;
        });
        zui.send("tab", id);
      }

      group.addEventListener("click", function (e) {
        var close = e.target.closest(".zui-tab__close");
        if (close) {
          e.stopPropagation();
          var tab = close.closest("[data-zui-tab]");
          var id = tab.getAttribute("data-zui-tab");
          var wasActive = tab.classList.contains("zui-active");
          var sibs = tabs();
          var idx = sibs.indexOf(tab);
          tab.remove();
          var panel = document.querySelector('[data-zui-tabpanel="' + CSS.escape(id) + '"]');
          if (panel) panel.remove();
          zui.send("tab-close", id);
          if (wasActive) activate(tabs()[Math.min(idx, tabs().length - 1)]);
          return;
        }
        var t = e.target.closest("[data-zui-tab]");
        if (t && group.contains(t)) activate(t);
      });

      group.addEventListener("keydown", function (e) {
        var sibs = tabs();
        var cur = sibs.indexOf(e.target.closest("[data-zui-tab]"));
        if (cur === -1) return;
        var next = null;
        if (e.key === "ArrowRight" || e.key === "ArrowDown") next = sibs[(cur + 1) % sibs.length];
        else if (e.key === "ArrowLeft" || e.key === "ArrowUp") next = sibs[(cur - 1 + sibs.length) % sibs.length];
        else if (e.key === "Home") next = sibs[0];
        else if (e.key === "End") next = sibs[sibs.length - 1];
        else if ((e.key === "Delete" || e.key === "Backspace")) {
          var c = sibs[cur].querySelector(".zui-tab__close"); if (c) { c.click(); e.preventDefault(); }
          return;
        }
        if (next) { activate(next); next.focus(); e.preventDefault(); }
      });

      // initialise roles / roving tabindex
      var active = group.querySelector("[data-zui-tab].zui-active") || group.querySelector("[data-zui-tab]");
      if (active) activate(active);
    });

    /* Title-bar window controls: a .zui-titlebar__btn carries data-window with
       one of minimize|maximize|restore|close. The host performs the action;
       zUI just forwards it. */
    root.querySelectorAll(".zui-titlebar__btn[data-window]").forEach(function (btn) {
      if (btn.__zuiWin) return; btn.__zuiWin = true;
      btn.addEventListener("click", function () {
        zui.send("window", btn.getAttribute("data-window"));
      });
    });

    /* Toggle buttons: data-zui="toggle" flips aria-pressed and emits `toggle`. */
    root.querySelectorAll('[data-zui="toggle"]').forEach(function (btn) {
      if (btn.__zuiToggle) return; btn.__zuiToggle = true;
      if (!btn.hasAttribute("aria-pressed")) btn.setAttribute("aria-pressed", "false");
      btn.addEventListener("click", function () {
        var on = btn.getAttribute("aria-pressed") === "true";
        btn.setAttribute("aria-pressed", String(!on));
        zui.send("toggle", { name: btn.getAttribute("data-name") || btn.textContent.trim(), pressed: !on });
      });
    });

    /* Split-button caret: opens the menu described by its data-menu JSON. */
    root.querySelectorAll(".zui-btn--caret[data-menu]").forEach(function (caret) {
      if (caret.__zuiCaret) return; caret.__zuiCaret = true;
      caret.addEventListener("click", function (e) {
        e.stopPropagation();
        var spec;
        try { spec = JSON.parse(caret.getAttribute("data-menu") || "[]"); } catch (x) { return; }
        var r = caret.getBoundingClientRect();
        zui.menu(spec, r.right - 4, r.bottom);
        setMenuActive(0);
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
        setMenuActive(0);
      });
      sel.addEventListener("keydown", function (e) {
        if (e.key === "Enter" || e.key === " " || e.key === "ArrowDown") {
          e.preventDefault();
          if (sel.getAttribute("aria-expanded") !== "true") sel.click();
        }
      });
    });

    /* Menu bar: [data-zui="menubar"] with items carrying data-menu (JSON). */
    root.querySelectorAll('[data-zui="menubar"] .zui-menubar__item').forEach(function (item) {
      if (item.__zuiWired) return; item.__zuiWired = true;
      item.setAttribute("tabindex", "0");
      var bar = item.parentElement;
      item.addEventListener("click", function () {
        var spec;
        try { spec = JSON.parse(item.getAttribute("data-menu") || "[]"); } catch (e) { spec = []; }
        var r = item.getBoundingClientRect();
        item.setAttribute("aria-expanded", "true");
        var m = zui.menu(spec, r.left, r.bottom);
        setMenuActive(0);
        /* Left/Right move to the adjacent top-level menu while one is open. */
        m.addEventListener("keydown", function (e) {
          if (e.key !== "ArrowLeft" && e.key !== "ArrowRight") return;
          var sibs = Array.prototype.slice.call(bar.querySelectorAll(".zui-menubar__item"));
          var ix = sibs.indexOf(item) + (e.key === "ArrowRight" ? 1 : -1);
          var nxt = sibs[(ix + sibs.length) % sibs.length];
          if (nxt) { closeMenus(); nxt.click(); e.preventDefault(); }
        });
        var clear = function () { item.setAttribute("aria-expanded", "false"); document.removeEventListener("mousedown", clear); };
        setTimeout(function () { document.addEventListener("mousedown", clear); });
      });
      item.addEventListener("keydown", function (e) {
        if (e.key === "Enter" || e.key === " " || e.key === "ArrowDown") { e.preventDefault(); item.click(); }
      });
    });

    /* Elements that request a cursor while hovered: data-zui-cursor="link". */
    root.querySelectorAll("[data-zui-cursor]").forEach(function (el) {
      if (el.__zuiCur) return; el.__zuiCur = true;
      var name = el.getAttribute("data-zui-cursor");
      el.addEventListener("mouseenter", function () { zui.cursor(name, el); });
      el.addEventListener("mouseleave", function () { zui.cursor(null, el); });
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
        var releaseCursor = zui.cursor.push(vertical ? "col" : "row");
        function move(ev) {
          var delta = (vertical ? ev.clientX : ev.clientY) - start;
          var next = Math.max(48, base + delta);
          prev.style.flex = "0 0 " + next + "px";
        }
        function up() {
          document.removeEventListener("mousemove", move);
          document.removeEventListener("mouseup", up);
          releaseCursor();
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
        var list = Array.prototype.map.call(e.dataTransfer.files || [], function (f) {
          return { name: f.name, path: f.path || null, size: f.size, type: f.type };
        });
        zui.send("drop", {
          target: el.getAttribute("data-name") || null,
          files: list,
          paths: list.map(function (f) { return f.path || f.name; })
        });
      });
    });

    /* Internal list reordering: [data-zui="reorder"] container whose direct
       children are the items. Drag an item to move it; a thin line marks the
       insertion point; on drop the new order is emitted on "reorder". */
    root.querySelectorAll('[data-zui="reorder"]').forEach(function (list) {
      if (list.__zuiReorder) return; list.__zuiReorder = true;
      var dragging = null;

      Array.prototype.forEach.call(list.children, function (it) { it.draggable = true; });

      list.addEventListener("dragstart", function (e) {
        dragging = e.target.closest("[data-zui-row], li, .zui-list__item");
        if (dragging) { dragging.classList.add("zui-reorder-ghost"); e.dataTransfer.effectAllowed = "move"; }
      });
      list.addEventListener("dragend", function () {
        if (dragging) dragging.classList.remove("zui-reorder-ghost");
        list.querySelectorAll(".zui-reorder-into").forEach(function (n) { n.classList.remove("zui-reorder-into"); });
        dragging = null;
      });
      list.addEventListener("dragover", function (e) {
        if (!dragging) return;
        e.preventDefault();
        var over = e.target.closest("[data-zui-row], li, .zui-list__item");
        if (!over || over === dragging) return;
        var r = over.getBoundingClientRect();
        var after = (e.clientY - r.top) / r.height > 0.5;
        list.querySelectorAll(".zui-reorder-into").forEach(function (n) { n.classList.remove("zui-reorder-into"); });
        over.classList.add("zui-reorder-into");
        list.insertBefore(dragging, after ? over.nextSibling : over);
      });
      list.addEventListener("drop", function (e) {
        e.preventDefault();
        var ids = Array.prototype.map.call(list.children, function (c) {
          return c.getAttribute("data-zui-row") || c.getAttribute("data-id") || c.textContent.trim();
        });
        zui.send("reorder", { name: list.getAttribute("data-name") || null, order: ids });
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
