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

    /* Modal dialog. opts: {title, body (string|HTMLElement), buttons:[{label,
       value, primary}], dismissable=true}. Returns a Promise resolving to the
       chosen button's value (or null on dismiss). Native <dialog> = focus trap
       + Esc handled by the platform. */
    dialog: function (opts) {
      opts = opts || {};
      return new Promise(function (resolve) {
        var dlg = document.createElement("dialog");
        dlg.className = "zui-dialog";
        var done = function (val) {
          if (dlg.open) dlg.close();
          dlg.remove();
          resolve(val);
        };

        if (opts.title != null) {
          var t = document.createElement("div"); t.className = "zui-dialog__title";
          t.appendChild(document.createTextNode(opts.title));
          if (opts.dismissable !== false) {
            var x = document.createElement("button"); x.className = "zui-dialog__x"; x.textContent = "×";
            x.addEventListener("click", function () { done(null); });
            t.appendChild(x);
          }
          dlg.appendChild(t);
        }

        var body = document.createElement("div"); body.className = "zui-dialog__body";
        if (opts.body instanceof Node) body.appendChild(opts.body);
        else { var p = document.createElement("p"); p.textContent = opts.body || ""; body.appendChild(p); }
        dlg.appendChild(body);

        var buttons = opts.buttons || [{ label: "OK", value: true, primary: true }];
        var foot = document.createElement("div"); foot.className = "zui-dialog__footer";
        buttons.forEach(function (b) {
          var el = document.createElement("button");
          el.className = "zui-btn" + (b.primary ? " zui-btn--primary" : "");
          el.textContent = b.label;
          el.addEventListener("click", function () { done(b.value !== undefined ? b.value : b.label); });
          foot.appendChild(el);
        });
        dlg.appendChild(foot);

        dlg.addEventListener("cancel", function (e) {   // Esc
          e.preventDefault();
          if (opts.dismissable !== false) done(null);
        });

        document.body.appendChild(dlg);
        dlg.showModal();
        var focusEl = dlg.querySelector(".zui-dialog__body .zui-input, .zui-dialog__body .zui-select") ||
          dlg.querySelector(".zui-btn--primary") || dlg.querySelector(".zui-btn");
        if (focusEl) focusEl.focus();
      });
    },

    /* Confirm helper. */
    confirm: function (message, opts) {
      opts = opts || {};
      return zui.dialog({
        title: opts.title || "Confirm",
        body: message,
        buttons: [
          { label: opts.cancelLabel || "Cancel", value: false },
          { label: opts.okLabel || "OK", value: true, primary: true }
        ]
      });
    },

    /* Non-modal popover anchored to an element. Returns { close() }. */
    popover: function (anchor, content, opts) {
      opts = opts || {};
      if (typeof anchor === "string") anchor = document.querySelector(anchor);
      var pop = document.createElement("div");
      pop.className = "zui-popover";
      if (content instanceof Node) pop.appendChild(content); else pop.textContent = content || "";
      document.body.appendChild(pop);
      var r = anchor.getBoundingClientRect();
      var pw = pop.offsetWidth, ph = pop.offsetHeight;
      var top = (opts.placement === "top") ? r.top - ph - 4 : r.bottom + 4;
      pop.style.left = Math.max(4, Math.min(r.left, innerWidth - pw - 4)) + "px";
      pop.style.top = Math.max(4, Math.min(top, innerHeight - ph - 4)) + "px";
      var close = function () {
        pop.remove();
        document.removeEventListener("mousedown", outside, true);
        document.removeEventListener("keydown", onKey, true);
      };
      var outside = function (e) { if (!pop.contains(e.target) && e.target !== anchor) close(); };
      var onKey = function (e) { if (e.key === "Escape") close(); };
      setTimeout(function () {
        document.addEventListener("mousedown", outside, true);
        document.addEventListener("keydown", onKey, true);
      });
      return { close: close, el: pop };
    },

    setTheme: function (name) {
      document.documentElement.setAttribute("data-zui-theme", name);
      zui.send("theme-changed", name);
    },

    /* ---- interaction layer ------------------------------------------- */

    /* Global keyboard shortcuts. zui.shortcuts({ "Ctrl+K": fn, "Ctrl+Shift+P": fn }).
       Keys: Ctrl/Alt/Shift/Meta + a key name (case-insensitive). Ignored while
       typing in an input/textarea unless the combo has a modifier other than Shift.
       Returns an unregister fn. */
    shortcuts: function (map) {
      Object.keys(map).forEach(function (combo) { shortcutMap[normCombo(combo)] = map[combo]; });
      return function () { Object.keys(map).forEach(function (c) { delete shortcutMap[normCombo(c)]; }); };
    },

    /* Undo/redo history. Push a reversible step; Ctrl+Z / Ctrl+Y (or Ctrl+Shift+Z)
       run it. Each step: {label?, undo(), redo()}. Also emits "undo"/"redo". */
    history: (function () {
      var past = [], future = [];
      return {
        push: function (step) { past.push(step); future.length = 0; zui.send("history", { canUndo: true, canRedo: false, label: step.label }); },
        undo: function () {
          var s = past.pop(); if (!s) return false;
          try { s.undo(); } catch (e) { console.error(e); }
          future.push(s); zui.send("undo", { label: s.label });
          zui.send("history", { canUndo: past.length > 0, canRedo: true, label: s.label });
          return true;
        },
        redo: function () {
          var s = future.pop(); if (!s) return false;
          try { s.redo(); } catch (e) { console.error(e); }
          past.push(s); zui.send("redo", { label: s.label });
          zui.send("history", { canUndo: true, canRedo: future.length > 0, label: s.label });
          return true;
        },
        clear: function () { past.length = future.length = 0; },
        get canUndo() { return past.length > 0; },
        get canRedo() { return future.length > 0; }
      };
    })(),

    /* Compact corner toast. opts: {kind:"ok|warn|error", timeout, action:{label,channel|onClick}}.
       Stack is capped so notifications never take over the screen. */
    toast: function (message, opts) {
      opts = opts || {};
      var MAX = 4;
      var host = document.querySelector(".zui-toasts");
      if (!host) { host = document.createElement("div"); host.className = "zui-toasts"; document.body.appendChild(host); }

      var el = document.createElement("div");
      el.className = "zui-toast" + (opts.kind ? " zui-toast--" + opts.kind : "");
      var GLYPH = { ok: "✓", warn: "⚠", error: "✕" };
      if (GLYPH[opts.kind]) {
        var g = document.createElement("span"); g.className = "zui-toast__glyph"; g.textContent = GLYPH[opts.kind]; el.appendChild(g);
      }
      var msg = document.createElement("span"); msg.className = "zui-toast__msg"; msg.textContent = message; el.appendChild(msg);

      var dismiss = function () { el.remove(); };
      if (opts.action) {
        var a = document.createElement("button"); a.className = "zui-toast__action"; a.textContent = opts.action.label;
        a.addEventListener("click", function () {
          if (typeof opts.action.onClick === "function") opts.action.onClick();
          else if (opts.action.channel) zui.send(opts.action.channel, opts.action.payload);
          dismiss();
        });
        el.appendChild(a);
      }
      var x = document.createElement("button"); x.className = "zui-toast__close"; x.textContent = "×";
      x.addEventListener("click", dismiss); el.appendChild(x);

      host.appendChild(el);
      while (host.children.length > MAX) host.firstChild.remove();
      if (opts.timeout !== 0) setTimeout(dismiss, opts.timeout || 4000);
      return el;
    },

    menu: function (items, x, y) {
      closeMenus();
      return openSubmenu(items, x, y, 0);
    }
  };

  var menuStack = [];          // [{el, items}] - index 0 is the root menu
  var menuActive = -1;         // active index within the deepest menu
  var typeahead = "";
  var typeaheadTimer = null;

  /* ---- shortcut registry + global key handling --------------------- */
  var shortcutMap = Object.create(null);

  function normCombo(c) {
    var parts = String(c).split("+").map(function (p) { return p.trim().toLowerCase(); });
    var mods = { ctrl: false, alt: false, shift: false, meta: false }, key = "";
    parts.forEach(function (p) {
      if (p === "ctrl" || p === "control") mods.ctrl = true;
      else if (p === "alt" || p === "option") mods.alt = true;
      else if (p === "shift") mods.shift = true;
      else if (p === "meta" || p === "cmd" || p === "win") mods.meta = true;
      else key = p;
    });
    return (mods.ctrl ? "c" : "") + (mods.alt ? "a" : "") + (mods.shift ? "s" : "") + (mods.meta ? "m" : "") + ":" + key;
  }
  function eventCombo(e) {
    var key = (e.key || "").toLowerCase();
    if (key === " ") key = "space";
    return (e.ctrlKey ? "c" : "") + (e.altKey ? "a" : "") + (e.shiftKey ? "s" : "") + (e.metaKey ? "m" : "") + ":" + key;
  }
  document.addEventListener("keydown", function (e) {
    var combo = eventCombo(e);
    var inField = /^(input|textarea|select)$/i.test(e.target.tagName || "") || e.target.isContentEditable;
    var hasCmdMod = /[cam]/.test(combo.split(":")[0]);
    if (!inField || hasCmdMod) {
      if (combo === "c:z" || combo === "m:z") { if (zui.history.undo()) { e.preventDefault(); return; } }
      if (combo === "c:y" || combo === "cs:z" || combo === "ms:z") { if (zui.history.redo()) { e.preventDefault(); return; } }
    }
    var fn = shortcutMap[combo];
    if (fn && (!inField || hasCmdMod)) { e.preventDefault(); fn(e); }
  }, true);

  function openMenuGet() { return menuStack.length ? menuStack[menuStack.length - 1].el : null; }

  /* "&File" -> a mnemonic on F. Returns {text, key}. */
  function parseMnemonic(label) {
    var m = /&(.)/.exec(label || "");
    return { text: (label || "").replace(/&(.)/, "$1"), key: m ? m[1].toLowerCase() : null,
             idx: m ? m.index : -1 };
  }

  function openSubmenu(items, x, y, level) {
    closeMenusBelow(level);
    var el = document.createElement("div");
    el.className = "zui-menu";
    el.setAttribute("tabindex", "-1");

    items.forEach(function (it) {
      if (it === "-" || it.separator) {
        var s = document.createElement("div"); s.className = "zui-menu__sep"; el.appendChild(s); return;
      }
      var mi = document.createElement("div");
      var sub = it.items || it.submenu;
      var mn = parseMnemonic(it.label);
      mi.className = "zui-menu__item" + (it.checked ? " zui-menu__item--checked" : "");
      if (it.disabled) mi.setAttribute("aria-disabled", "true");
      if (mn.key) mi.setAttribute("data-mnemonic", mn.key);

      if (mn.idx >= 0) {
        mi.appendChild(document.createTextNode(mn.text.slice(0, mn.idx)));
        var u = document.createElement("u"); u.textContent = mn.text.charAt(mn.idx); mi.appendChild(u);
        mi.appendChild(document.createTextNode(mn.text.slice(mn.idx + 1)));
      } else {
        mi.appendChild(document.createTextNode(mn.text));
      }
      if (it.shortcut) {
        var sc = document.createElement("span"); sc.className = "zui-menu__shortcut";
        sc.textContent = it.shortcut; mi.appendChild(sc);
      }
      if (sub) {
        var ar = document.createElement("span"); ar.className = "zui-menu__arrow"; ar.textContent = "▸";
        mi.appendChild(ar);
        mi.addEventListener("mouseenter", function () {
          var r = mi.getBoundingClientRect();
          openSubmenu(sub, r.right - 2, r.top - 5, level + 1);
        });
      } else {
        mi.addEventListener("mouseenter", function () { closeMenusBelow(level + 1); });
      }
      mi.addEventListener("click", function (e) {
        if (it.disabled) return;
        if (sub) { e.stopPropagation(); var r = mi.getBoundingClientRect(); openSubmenu(sub, r.right - 2, r.top - 5, level + 1); return; }
        closeMenus();
        if (typeof it.action === "function") it.action();
        else if (it.channel) zui.send(it.channel, it.payload);
      });
      el.appendChild(mi);
    });

    document.body.appendChild(el);
    var w = el.offsetWidth, h = el.offsetHeight;
    var left = x + w > innerWidth - 4 ? Math.max(4, (level ? x - w - w : innerWidth - w - 4)) : x;
    el.style.left = Math.max(4, left) + "px";
    el.style.top = Math.min(Math.max(4, y), innerHeight - h - 4) + "px";
    menuStack[level] = { el: el, items: items };
    menuActive = -1;
    el.focus();
    return el;
  }

  function closeMenusBelow(level) {
    while (menuStack.length > level) {
      var m = menuStack.pop();
      if (m && m.el) m.el.remove();
    }
    menuActive = -1;
  }
  function closeMenus() { closeMenusBelow(0); }

  function menuItems() {
    var el = openMenuGet();
    return el
      ? Array.prototype.filter.call(el.querySelectorAll(".zui-menu__item"),
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
    var top = openMenuGet();
    if (top && !menuStack.some(function (m) { return m.el.contains(e.target); })) closeMenus();
  });

  document.addEventListener("keydown", function (e) {
    if (!openMenuGet()) return;
    var items = menuItems();
    if (e.key === "Escape") {
      if (menuStack.length > 1) closeMenusBelow(menuStack.length - 1);
      else closeMenus();
      e.preventDefault();
    }
    else if (e.key === "ArrowDown") { setMenuActive(menuActive + 1); e.preventDefault(); }
    else if (e.key === "ArrowUp") { setMenuActive(menuActive - 1); e.preventDefault(); }
    else if (e.key === "Home") { setMenuActive(0); e.preventDefault(); }
    else if (e.key === "End") { setMenuActive(items.length - 1); e.preventDefault(); }
    else if (e.key === "ArrowRight" && menuActive >= 0 && items[menuActive] &&
             items[menuActive].querySelector(".zui-menu__arrow")) {
      items[menuActive].click(); e.preventDefault();
    }
    else if (e.key === "ArrowLeft" && menuStack.length > 1) {
      closeMenusBelow(menuStack.length - 1); e.preventDefault();
    }
    else if (e.key === "Enter" || e.key === " ") {
      if (menuActive >= 0 && items[menuActive]) { items[menuActive].click(); e.preventDefault(); }
    } else if (e.key.length === 1) {
      var kl = e.key.toLowerCase();
      // mnemonic match first, then type-ahead
      var mi = items.filter(function (n) { return n.getAttribute("data-mnemonic") === kl; });
      if (mi.length === 1) { mi[0].click(); return; }
      typeahead += kl;
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

      // initialise roles / roving tabindex; #hash can preselect a tab
      var hash = (location.hash || "").slice(1);
      var active = (hash && group.querySelector('[data-zui-tab="' + (window.CSS ? CSS.escape(hash) : hash) + '"]')) ||
        group.querySelector("[data-zui-tab].zui-active") ||
        group.querySelector("[data-zui-tab]");
      if (active) activate(active);
    });

    /* Contextual / playback bar: [data-zui="contextbar"]. Transport buttons
       carrying data-transport emit "transport"; the host pushes "now-playing"
       {title, sub, position, duration, positionPct} to update it. */
    (root || document).querySelectorAll('[data-zui="contextbar"]').forEach(function (bar) {
      if (bar.__zuiCtxBar) return; bar.__zuiCtxBar = true;
      bar.querySelectorAll("[data-transport]").forEach(function (b) {
        b.addEventListener("click", function () { zui.send("transport", b.getAttribute("data-transport")); });
      });
      var fmt = function (s) {
        if (s == null) return "";
        s = Math.round(s); return Math.floor(s / 60) + ":" + ("0" + (s % 60)).slice(-2);
      };
      zui.receive("now-playing", function (p) {
        if (!p) return;
        var set = function (cls, v) { var n = bar.querySelector("." + cls); if (n && v != null) n.textContent = v; };
        set("zui-contextbar__title", p.title);
        set("zui-contextbar__sub", p.sub);
        var times = bar.querySelectorAll(".zui-contextbar__time");
        if (times[0] && p.position != null) times[0].textContent = fmt(p.position);
        if (times[1] && p.duration != null) times[1].textContent = fmt(p.duration);
        var pctv = p.positionPct != null ? p.positionPct
          : (p.position != null && p.duration ? (p.position / p.duration) * 100 : null);
        var bar2 = bar.querySelector(".zui-progress__bar");
        if (bar2 && pctv != null) bar2.style.width = Math.max(0, Math.min(100, pctv)) + "%";
      });
    });

    /* Device / status bar: a .zui-statusbar with data-zui-device reacts to the
       "device" channel - {name, capacity, free, actions?} connects it, null
       disconnects. Matches design.txt STATUS / DEVICE BAR. */
    (root || document).querySelectorAll(".zui-statusbar[data-zui-device]").forEach(function (bar) {
      if (bar.__zuiDevice) return; bar.__zuiDevice = true;
      var set = function (txt, cls) {
        var n = bar.querySelector("." + cls);
        if (n) n.textContent = txt == null ? "" : txt;
      };
      zui.receive("device", function (d) {
        if (!d) {
          bar.classList.remove("zui-statusbar--connected");
          set("No device connected", "zui-statusbar__name");
          return;
        }
        bar.classList.add("zui-statusbar--connected");
        set(d.name, "zui-statusbar__name");
        set(d.capacity != null ? d.capacity + " capacity" : "", "zui-statusbar__capacity");
        set(d.free != null ? d.free + " free" : "", "zui-statusbar__free");
      });
      bar.querySelectorAll("[data-device-action]").forEach(function (b) {
        b.addEventListener("click", function () { zui.send("device-action", b.getAttribute("data-device-action")); });
      });
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

    /* Menu bar: [data-zui="menubar"] with items carrying data-menu (JSON).
       Alt (or Alt+letter) activates it, like a native Windows menu. */
    root.querySelectorAll('[data-zui="menubar"]').forEach(function (bar0) {
      if (bar0.__zuiAlt) return; bar0.__zuiAlt = true;
      document.addEventListener("keydown", function (e) {
        if (e.key === "Alt" && !e.repeat) { document.body.classList.toggle("zui-alt"); }
        else if (e.altKey && e.key.length === 1) {
          var hit = Array.prototype.filter.call(bar0.querySelectorAll(".zui-menubar__item"), function (it) {
            return (it.getAttribute("data-mnemonic") || it.textContent.trim().charAt(0)).toLowerCase() === e.key.toLowerCase();
          })[0];
          if (hit) { e.preventDefault(); document.body.classList.add("zui-alt"); hit.click(); }
        } else if (e.key === "Escape") {
          document.body.classList.remove("zui-alt");
        }
      });
    });
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

    /* Sortable / resizable table columns: [data-zui="table"] on a <table>.
       A <th> opts into sorting with data-field; all get a resize grip.
       Sorting reorders the tbody rows in place and emits "sort". */
    (root || document).querySelectorAll('table[data-zui-table], table[data-zui="selectable"]').forEach(function (table) {
      if (table.__zuiTable) return; table.__zuiTable = true;
      var ths = Array.prototype.slice.call(table.querySelectorAll("thead th"));
      ths.forEach(function (th, ci) {
        // resize grip
        var grip = document.createElement("span");
        grip.className = "zui-col-resize";
        th.appendChild(grip);
        grip.addEventListener("mousedown", function (e) {
          e.preventDefault(); e.stopPropagation();
          var startX = e.clientX, startW = th.offsetWidth;
          var rel = zui.cursor.push("col");
          function mv(ev) { th.style.width = Math.max(32, startW + ev.clientX - startX) + "px"; }
          function up() {
            document.removeEventListener("mousemove", mv); document.removeEventListener("mouseup", up); rel();
            zui.send("column-resize", { column: th.getAttribute("data-field") || ci, width: th.offsetWidth });
          }
          document.addEventListener("mousemove", mv); document.addEventListener("mouseup", up);
        });
        // sort
        var field = th.getAttribute("data-field");
        if (field == null) return;
        th.setAttribute("data-sort", "");
        th.addEventListener("click", function () {
          var cur = th.getAttribute("data-sort");
          var dir = cur === "asc" ? "desc" : "asc";
          ths.forEach(function (o) { if (o !== th) o.setAttribute("data-sort", o.hasAttribute("data-sort") ? "" : null); });
          th.setAttribute("data-sort", dir);
          var tb = table.tBodies[0];
          if (tb) {
            var rows = Array.prototype.slice.call(tb.rows);
            rows.sort(function (a, b) {
              var av = a.cells[ci] ? a.cells[ci].textContent.trim() : "";
              var bv = b.cells[ci] ? b.cells[ci].textContent.trim() : "";
              var na = parseFloat(av), nb = parseFloat(bv);
              var cmp = (!isNaN(na) && !isNaN(nb)) ? na - nb : av.localeCompare(bv);
              return dir === "asc" ? cmp : -cmp;
            });
            rows.forEach(function (r) { tb.appendChild(r); });
          }
          zui.send("sort", { column: field, dir: dir });
        });
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

    /* Tree / outline: [data-zui="tree"] over a flat list of .zui-tree__item rows
       (data-id, data-depth, aria-expanded on groups). Descendants are the
       following rows with greater depth. */
    (root || document).querySelectorAll('[data-zui="tree"]').forEach(function (tree) {
      if (tree.__zuiTree) return; tree.__zuiTree = true;
      tree.setAttribute("role", "tree");

      function rows() { return Array.prototype.slice.call(tree.querySelectorAll(".zui-tree__item")); }
      function depth(r) { return +(r.getAttribute("data-depth") || 0); }
      function visibleRows() { return rows().filter(function (r) { return !r.hidden; }); }
      function descendants(r) {
        var all = rows(), i = all.indexOf(r), d = depth(r), out = [];
        for (var k = i + 1; k < all.length; k++) { if (depth(all[k]) <= d) break; out.push(all[k]); }
        return out;
      }
      function applyCollapse() {
        var all = rows();
        all.forEach(function (r, i) {
          var hide = false, d = depth(r);
          for (var j = i - 1; j >= 0 && d > 0; j--) {
            if (depth(all[j]) < d) {
              d = depth(all[j]);
              if (all[j].getAttribute("aria-expanded") === "false") { hide = true; break; }
            }
          }
          r.hidden = hide;
        });
      }
      function toggle(r, want) {
        if (!r.hasAttribute("aria-expanded")) return;
        var open = want != null ? want : r.getAttribute("aria-expanded") === "false";
        r.setAttribute("aria-expanded", String(open));
        applyCollapse();
        emit(r, open ? "expand" : "collapse");
      }
      function selected() { return rows().filter(function (r) { return r.classList.contains("zui-selected"); }); }
      function select(r, add, range) {
        var all = visibleRows();
        if (range && anchor) {
          var a = all.indexOf(anchor), b = all.indexOf(r);
          all.forEach(function (x, i) { x.classList.toggle("zui-selected", i >= Math.min(a, b) && i <= Math.max(a, b)); });
        } else if (add) {
          r.classList.toggle("zui-selected"); anchor = r;
        } else {
          rows().forEach(function (x) { x.classList.toggle("zui-selected", x === r); }); anchor = r;
        }
        rows().forEach(function (x) { x.tabIndex = x === r ? 0 : -1; });
        emit(r, "select");
      }
      function emit(r, action) {
        zui.send("tree", {
          name: tree.getAttribute("data-name") || tree.getAttribute("data-zui-id") || null,
          id: r.getAttribute("data-id") || r.textContent.trim(),
          action: action,
          selected: selected().map(function (x) { return x.getAttribute("data-id") || x.textContent.trim(); })
        });
      }
      var anchor = null;

      tree.addEventListener("click", function (e) {
        var r = e.target.closest(".zui-tree__item");
        if (!r) return;
        if (e.target.closest(".zui-tree__twist")) { toggle(r); return; }
        select(r, e.ctrlKey || e.metaKey, e.shiftKey);
      });
      tree.addEventListener("dblclick", function (e) {
        var r = e.target.closest(".zui-tree__item");
        if (r && r.hasAttribute("aria-expanded")) toggle(r);
        else if (r) emit(r, "activate");
      });
      tree.addEventListener("keydown", function (e) {
        var vis = visibleRows();
        var cur = vis.indexOf(e.target.closest(".zui-tree__item"));
        if (cur === -1) return;
        var r = vis[cur], nxt = null;
        if (e.key === "ArrowDown") nxt = vis[cur + 1];
        else if (e.key === "ArrowUp") nxt = vis[cur - 1];
        else if (e.key === "Home") nxt = vis[0];
        else if (e.key === "End") nxt = vis[vis.length - 1];
        else if (e.key === "ArrowRight") {
          if (r.getAttribute("aria-expanded") === "false") { toggle(r, true); }
          else nxt = vis[cur + 1];
        } else if (e.key === "ArrowLeft") {
          if (r.getAttribute("aria-expanded") === "true") { toggle(r, false); }
          else {
            for (var k = cur - 1; k >= 0; k--) { if (depth(vis[k]) < depth(r)) { nxt = vis[k]; break; } }
          }
        } else if (e.key === "Enter" || e.key === " ") { emit(r, "activate"); e.preventDefault(); }
        else if (e.key === "F2") { zui.renameInPlace(r.querySelector(".zui-tree__label") || r, function (v) { emit(r, "rename"); }); e.preventDefault(); }
        if (nxt) { select(nxt, false, e.shiftKey); nxt.focus(); e.preventDefault(); }
      });

      // init
      rows().forEach(function (r) {
        r.style.setProperty("--depth", depth(r));
        r.setAttribute("role", "treeitem");
        if (!r.querySelector(".zui-tree__twist") && r.hasAttribute("aria-expanded")) {
          var t = document.createElement("span"); t.className = "zui-tree__twist";
          r.insertBefore(t, r.firstChild);
        }
        r.tabIndex = -1;
      });
      var first = tree.querySelector(".zui-tree__item.zui-selected") || tree.querySelector(".zui-tree__item");
      if (first) first.tabIndex = 0;
      applyCollapse();
    });

    /* Sliders: keep the accent fill + <output> in sync (IO-independent). */
    (root || document).querySelectorAll(".zui-slider").forEach(function (sl) {
      if (sl.__zuiSlider) return; sl.__zuiSlider = true;
      syncSlider(sl);
      sl.addEventListener("input", function () { syncSlider(sl); });
    });

    /* Dual-thumb range: two .zui-slider in a .zui-range, kept from crossing. */
    (root || document).querySelectorAll(".zui-range").forEach(function (rg) {
      if (rg.__zuiRange) return; rg.__zuiRange = true;
      var lo = rg.querySelector(".zui-slider:first-child");
      var hi = rg.querySelector(".zui-slider:last-child");
      if (!lo || !hi || lo === hi) return;
      function clamp() {
        if (+lo.value > +hi.value - 1) { if (document.activeElement === lo) lo.value = +hi.value - 1; else hi.value = +lo.value + 1; }
        syncSlider(lo); syncSlider(hi);
        if (rg.hasAttribute("data-zui-id")) zui.send("value", { id: rg.getAttribute("data-zui-id"), kind: "range2", value: [+lo.value, +hi.value] });
      }
      lo.addEventListener("input", clamp); hi.addEventListener("input", clamp); clamp();
    });

    wireIo(root);
  }

  /* --------------------------------------------------------------------- */
  /* Component IO - one two-way value protocol for every component.        */
  /*                                                                       */
  /* Any element with data-zui-id is addressable:                          */
  /*   out (UI -> host): a "value" message {id, kind, value} on each change */
  /*                     + zui.values(scope) / zui.field(id) / zui.bind()  */
  /*   in  (host -> UI): "set" {id,value}, "set-many" {id:value}, "query"  */
  /*                     + zui.set(id, value)                              */
  /* Programmatic set() never re-emits (loop-safe).                        */
  /* --------------------------------------------------------------------- */

  var ioBinds = Object.create(null);   // id -> [handler]
  var scrollThrottle = Object.create(null);

  function asNode(root) {
    if (!root) return document;
    if (typeof root === "string") return document.querySelector(root) || document;
    return root;
  }
  function ioEls(root) {
    return Array.prototype.slice.call(asNode(root).querySelectorAll("[data-zui-id]"));
  }
  function ioById(id) { return document.querySelector('[data-zui-id="' + (window.CSS ? CSS.escape(id) : id) + '"]'); }

  function syncSlider(el) {
    var min = parseFloat(el.min || 0), max = parseFloat(el.max || 100);
    var f = max > min ? ((parseFloat(el.value) - min) / (max - min)) * 100 : 0;
    el.style.setProperty("--zui-slider-fill", f + "%");
    var out = el.parentElement && el.parentElement.querySelector("output");
    if (out) out.textContent = el.value;
  }

  function pct(n, d) { return d > 0 ? Math.round((n / d) * 1000) / 10 : 0; }
  function clampPct(v) { v = parseFloat(v); return isNaN(v) ? 0 : Math.max(0, Math.min(100, v)); }

  function ioKind(el) {
    if (el.matches('[data-zui="tree"]')) return "tree";
    if (el.matches('[role="radiogroup"], .zui-choice-group[data-zui-id]') && el.querySelector('input[type=radio]')) return "radio";
    if (el.matches("input[type=checkbox]")) return "boolean";
    if (el.matches("input[type=radio]")) return "boolean";
    if (el.matches("input[type=range]")) return "range";
    if (el.matches("input, textarea")) return "text";
    if (el.matches("select")) return "select";
    if (el.classList.contains("zui-select")) return "select";
    if (el.classList.contains("zui-progress__bar") || el.classList.contains("zui-progress")) return "progress";
    if (el.matches("button, .zui-btn")) return el.hasAttribute("aria-pressed") ? "boolean" : "button";
    if (el.classList.contains("zui-scroll") || el.hasAttribute("data-zui-scroll") ||
        getComputedStyle(el).overflowY === "auto" || getComputedStyle(el).overflowY === "scroll") return "scroll";
    return "text"; // labels / text nodes
  }

  function ioGet(el) {
    var k = ioKind(el);
    if (k === "tree") {
      return Array.prototype.map.call(el.querySelectorAll(".zui-tree__item.zui-selected"),
        function (r) { return r.getAttribute("data-id") || r.textContent.trim(); });
    }
    if (k === "radio") {
      var on = el.querySelector("input[type=radio]:checked");
      return on ? on.value : null;
    }
    if (k === "boolean") {
      return el.matches("input") ? el.checked : el.getAttribute("aria-pressed") === "true";
    }
    if (k === "range") return parseFloat(el.value);
    if (k === "text" && el.matches("input, textarea")) return el.value;
    if (k === "select") {
      return el.matches("select") ? el.value : el.getAttribute("data-value");
    }
    if (k === "progress") {
      var bar = el.classList.contains("zui-progress__bar") ? el : el.querySelector(".zui-progress__bar");
      var wrap = bar ? bar.parentElement : el;
      if (wrap && wrap.classList.contains("zui-progress--indeterminate")) return "indeterminate";
      return bar ? clampPct(bar.style.width) : 0;
    }
    if (k === "button") return null;
    if (k === "scroll") {
      return {
        top: el.scrollTop, left: el.scrollLeft,
        topPct: pct(el.scrollTop, el.scrollHeight - el.clientHeight),
        leftPct: pct(el.scrollLeft, el.scrollWidth - el.clientWidth)
      };
    }
    return el.textContent;
  }

  function ioSet(el, v) {
    var k = ioKind(el);
    if (k === "tree") {
      var want = Array.isArray(v) ? v : [v];
      el.querySelectorAll(".zui-tree__item").forEach(function (r) {
        r.classList.toggle("zui-selected", want.indexOf(r.getAttribute("data-id") || r.textContent.trim()) !== -1);
      });
    } else if (k === "radio") {
      var pick = el.querySelector('input[type=radio][value="' + (window.CSS ? CSS.escape(v) : v) + '"]');
      if (pick) pick.checked = true;
    } else if (k === "boolean") {
      if (el.matches("input")) {
        if (v === "indeterminate") { el.indeterminate = true; }
        else { el.indeterminate = false; el.checked = !!v; }
      } else el.setAttribute("aria-pressed", String(v === true || (v && v.pressed)));
    } else if (k === "range") {
      el.value = v;
      syncSlider(el);
    } else if (k === "text" && el.matches("input, textarea")) {
      el.value = v == null ? "" : v;
    } else if (k === "select") {
      if (el.matches("select")) el.value = v;
      else {
        el.setAttribute("data-value", v);
        var lbl = el.querySelector(".zui-select__value");
        if (lbl) lbl.textContent = v;
      }
    } else if (k === "progress") {
      var bar = el.classList.contains("zui-progress__bar") ? el : el.querySelector(".zui-progress__bar");
      var wrap = bar ? bar.parentElement : el;
      if (v === "indeterminate") { if (wrap) wrap.classList.add("zui-progress--indeterminate"); }
      else if (bar) { if (wrap) wrap.classList.remove("zui-progress--indeterminate"); bar.style.width = clampPct(v) + "%"; }
    } else if (k === "button") {
      if (v === "click" || (v && v.action === "click")) {
        el.click();
        el.classList.add("zui-btn--flash");
        setTimeout(function () { el.classList.remove("zui-btn--flash"); }, 220);
      } else if (v && typeof v === "object") {
        if ("busy" in v) zui.busy(el, v.busy);
        if ("disabled" in v) el.disabled = !!v.disabled;
        if ("pressed" in v) el.setAttribute("aria-pressed", String(!!v.pressed));
        if ("label" in v) {
          var t = Array.prototype.filter.call(el.childNodes, function (n) { return n.nodeType === 3; })[0];
          if (t) t.nodeValue = v.label; else el.textContent = v.label;
        }
      }
    } else if (k === "scroll") {
      if (typeof v === "number") el.scrollTop = v;
      else if (v && typeof v === "object") {
        if ("top" in v) el.scrollTop = v.top;
        if ("left" in v) el.scrollLeft = v.left;
        if ("topPct" in v) el.scrollTop = (v.topPct / 100) * (el.scrollHeight - el.clientHeight);
        if ("leftPct" in v) el.scrollLeft = (v.leftPct / 100) * (el.scrollWidth - el.clientWidth);
      }
    } else {
      el.textContent = v == null ? "" : v;
    }
  }

  function ioEmit(el, extra) {
    var id = el.getAttribute("data-zui-id");
    var msg = { id: id, kind: ioKind(el), value: ioGet(el) };
    if (extra) for (var kk in extra) msg[kk] = extra[kk];
    zui.send("value", msg);
    (ioBinds[id] || []).forEach(function (h) { try { h(msg.value, msg); } catch (e) { console.error(e); } });
  }

  function wireIo(root) {
    ioEls(root).forEach(function (el) {
      if (el.__zuiIo) return; el.__zuiIo = true;
      var k = ioKind(el);
      if (k === "text" && el.matches("input, textarea")) {
        el.addEventListener("input", function () { ioEmit(el); });
        el.addEventListener("change", function () { ioEmit(el, { committed: true }); });
      } else if (k === "range") {
        syncSlider(el);
        el.addEventListener("input", function () { syncSlider(el); ioEmit(el); });
      } else if (k === "radio") {
        el.addEventListener("change", function () { ioEmit(el); });
      } else if (k === "boolean" && el.matches("input")) {
        el.addEventListener("change", function () { ioEmit(el); });
      } else if (k === "boolean") { // toggle button
        el.addEventListener("click", function () { setTimeout(function () { ioEmit(el); }); });
      } else if (k === "select" && el.matches("select")) {
        el.addEventListener("change", function () { ioEmit(el); });
      } else if (k === "select") {
        el.addEventListener("click", function () {
          var seen = el.getAttribute("data-value");
          var iv = setInterval(function () {
            if (el.getAttribute("data-value") !== seen) { clearInterval(iv); ioEmit(el); }
          }, 60);
          setTimeout(function () { clearInterval(iv); }, 8000);
        });
      } else if (k === "button") {
        el.addEventListener("click", function () { ioEmit(el, { event: "click" }); });
      } else if (k === "scroll") {
        el.addEventListener("scroll", function () {
          var id = el.getAttribute("data-zui-id");
          if (scrollThrottle[id]) return;
          scrollThrottle[id] = setTimeout(function () { scrollThrottle[id] = null; ioEmit(el); }, 100);
        }, { passive: true });
      }
    });

    /* Generic double-click action: data-zui-dblclick="channel". */
    (root || document).querySelectorAll("[data-zui-dblclick]").forEach(function (el) {
      if (el.__zuiDbl) return; el.__zuiDbl = true;
      el.addEventListener("dblclick", function () {
        zui.send(el.getAttribute("data-zui-dblclick"), { id: el.getAttribute("data-id") || null });
      });
    });

    /* Declarative shortcuts: data-zui-shortcut="Ctrl+K" clicks the element. */
    (root || document).querySelectorAll("[data-zui-shortcut]").forEach(function (el) {
      if (el.__zuiSc) return; el.__zuiSc = true;
      var m = {};
      m[el.getAttribute("data-zui-shortcut")] = function () { el.click(); };
      el.__zuiScUnbind = zui.shortcuts(m);
    });

    /* Rename-in-place: dblclick (or F2 when focused) an element carrying
       data-zui-rename="channel"; commit emits {channel, value}. */
    (root || document).querySelectorAll("[data-zui-rename]").forEach(function (el) {
      if (el.__zuiRename) return; el.__zuiRename = true;
      el.setAttribute("tabindex", el.getAttribute("tabindex") || "0");
      var start = function () {
        zui.renameInPlace(el, function (val) { zui.send(el.getAttribute("data-zui-rename"), { value: val }); });
      };
      el.addEventListener("dblclick", start);
      el.addEventListener("keydown", function (e) { if (e.key === "F2") { e.preventDefault(); start(); } });
    });

    /* [data-zui-submit] button -> emit the enclosing form's values. */
    (root || document).querySelectorAll("[data-zui-submit]").forEach(function (btn) {
      if (btn.__zuiSubmit) return; btn.__zuiSubmit = true;
      btn.addEventListener("click", function () {
        var scope = (btn.parentElement && btn.parentElement.closest("[data-zui-form]")) || document;
        zui.send("submit", { form: btn.getAttribute("data-zui-submit") || null, values: zui.values(scope) });
      });
    });
  }

  /* public IO API */
  zui.values = function (root) {
    var out = {};
    ioEls(root).forEach(function (el) {
      var id = el.getAttribute("data-zui-id");
      if (ioKind(el) !== "button") out[id] = ioGet(el);
    });
    return out;
  };
  zui.field = function (id) { var el = ioById(id); return el ? ioGet(el) : undefined; };
  zui.set = function (id, value) {
    if (id && typeof id === "object") { Object.keys(id).forEach(function (k) { zui.set(k, id[k]); }); return; }
    var el = ioById(id);
    if (el) ioSet(el, value);
  };
  /* Mark a field valid/invalid/warn with an optional message under it.
     zui.mark(id, "error", "Name is required")  |  zui.mark(id, "ok") */
  zui.mark = function (id, level, message) {
    var el = ioById(id);
    if (!el) return;
    var ctl = el.matches(".zui-input, .zui-textarea, .zui-select") ? el
      : el.closest(".zui-input, .zui-textarea, .zui-select") || el;
    ctl.classList.remove("zui-invalid", "zui-warn");
    if (level === "error") ctl.classList.add("zui-invalid");
    else if (level === "warn") ctl.classList.add("zui-warn");
    var field = ctl.closest(".zui-field");
    if (field) {
      var msg = field.querySelector(".zui-field__msg");
      if (message) {
        if (!msg) { msg = document.createElement("div"); msg.className = "zui-field__msg"; field.appendChild(msg); }
        msg.textContent = message;
        msg.className = "zui-field__msg" + (level === "error" ? " zui-field__msg--error" : level === "warn" ? " zui-field__msg--warn" : "");
      } else if (msg) { msg.remove(); }
    }
    return level !== "error";
  };

  /* Validate a scope against {id: rule}. rule: {required, pattern, min, max,
     validate:fn}. Returns true if all pass; marks each field. */
  zui.validate = function (scope, rules) {
    var ok = true;
    Object.keys(rules || {}).forEach(function (id) {
      var r = rules[id], v = zui.field(id), err = null;
      if (r.required && (v === "" || v == null || v === false)) err = r.requiredMessage || "Required";
      else if (r.pattern && v && !new RegExp(r.pattern).test(v)) err = r.message || "Invalid format";
      else if (r.min != null && parseFloat(v) < r.min) err = "Must be ≥ " + r.min;
      else if (r.max != null && parseFloat(v) > r.max) err = "Must be ≤ " + r.max;
      else if (typeof r.validate === "function") err = r.validate(v) || null;
      zui.mark(id, err ? "error" : "ok", err || "");
      if (err) ok = false;
    });
    return ok;
  };

  /* Rename-in-place: replace an element's text with an input; commit on
     Enter/blur, cancel on Escape. onCommit(newValue) -> truthy to accept. */
  zui.renameInPlace = function (el, onCommit) {
    if (el.__renaming) return; el.__renaming = true;
    var original = el.textContent.trim();
    var input = document.createElement("input");
    input.className = "zui-rename";
    input.value = original;
    el.textContent = "";
    el.appendChild(input);
    input.focus(); input.select();
    var done = function (commit) {
      if (el.__done) return; el.__done = true;
      var val = input.value.trim();
      el.__renaming = false;
      if (commit && val && val !== original && (!onCommit || onCommit(val) !== false)) el.textContent = val;
      else el.textContent = original;
      delete el.__done;
    };
    input.addEventListener("keydown", function (e) {
      if (e.key === "Enter") { e.preventDefault(); done(true); }
      else if (e.key === "Escape") { e.preventDefault(); done(false); }
    });
    input.addEventListener("blur", function () { done(true); });
  };

  /* Windowed rendering for very large lists/tables. Only visible rows exist in
     the DOM, so 100k rows scroll smoothly.
       zui.virtualList(scrollEl, {
         count, rowHeight, render(index) -> HTMLElement, overscan=8 })
     Returns { refresh(newCount), scrollToIndex(i), destroy() }. */
  zui.virtualList = function (scrollEl, opts) {
    scrollEl = asNode(scrollEl);
    var rowH = opts.rowHeight, overscan = opts.overscan || 8;
    var count = opts.count;
    scrollEl.style.position = scrollEl.style.position || "relative";
    scrollEl.style.overflowY = "auto";
    var sizer = document.createElement("div");
    sizer.style.cssText = "position:relative;width:100%";
    var pool = document.createElement("div");
    pool.style.cssText = "position:absolute;top:0;left:0;right:0";
    sizer.appendChild(pool);
    scrollEl.appendChild(sizer);

    function layout() {
      sizer.style.height = count * rowH + "px";
      var top = scrollEl.scrollTop;
      var first = Math.max(0, Math.floor(top / rowH) - overscan);
      var last = Math.min(count, Math.ceil((top + scrollEl.clientHeight) / rowH) + overscan);
      pool.style.transform = "translateY(" + first * rowH + "px)";
      pool.textContent = "";
      for (var i = first; i < last; i++) {
        var el = opts.render(i);
        el.style.height = rowH + "px";
        pool.appendChild(el);
      }
    }
    var onScroll = function () { window.requestAnimationFrame(layout); };
    scrollEl.addEventListener("scroll", onScroll, { passive: true });
    layout();
    return {
      refresh: function (n) { if (n != null) count = n; layout(); },
      scrollToIndex: function (i) { scrollEl.scrollTop = i * rowH; },
      destroy: function () { scrollEl.removeEventListener("scroll", onScroll); sizer.remove(); }
    };
  };

  zui.bind = function (id, handler) {
    (ioBinds[id] || (ioBinds[id] = [])).push(handler);
    return function () { ioBinds[id] = (ioBinds[id] || []).filter(function (h) { return h !== handler; }); };
  };

  /* inbound channels (registered once) */
  /* Runtime screen swap: the host pushes a full compiled document string and we
     replace the live page with it (scripts included). The AOT csharp/cpp
     backends prefer host.LoadDocument (a real navigation); this is the
     in-place alternative for swapping screens without re-navigating. */
  zui.receive("__document", function (html) {
    if (typeof html !== "string" || !html) return;
    try { document.open(); document.write(html); document.close(); }
    catch (e) { console.error("[zui] __document swap failed", e); }
  });

  zui.receive("theme", function (name) { if (name) zui.setTheme(typeof name === "string" ? name : name.name); });
  zui.receive("toast", function (p) {
    if (!p) return;
    if (typeof p === "string") zui.toast(p);
    else zui.toast(p.message, p);
  });
  zui.receive("dialog", function (p) {
    if (!p) return;
    zui.dialog(p).then(function (val) { zui.send("dialog-result", { id: p.id || null, value: val }); });
  });
  zui.receive("set", function (p) { if (p) zui.set(p.id, p.value); });
  zui.receive("set-many", function (p) { if (p) zui.set(p); });
  zui.receive("query", function (p) {
    if (p && p.id) zui.send("value", { id: p.id, kind: (ioById(p.id) ? ioKind(ioById(p.id)) : null), value: zui.field(p.id) });
    else zui.send("values", zui.values());
  });

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
