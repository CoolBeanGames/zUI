# zUI Runtime Contract (frozen)

Status: **stable public contract**. Version: `runtime-contract/1`.

This document is the promise zUI makes to applications that adopt it. Internal
implementation of the compiler and the native hosts may change freely between
releases; the behaviour described here may not change without a new contract
version and a migration note. Every subsequent zUI task implements against this
contract rather than redefining it.

zUI is a **native Windows UI framework with web-like authoring**. ZML/ZSL is a
declarative build-time syntax; it is not a browser runtime, and there is no DOM,
stylesheet cascade, script engine, or document to ship. See
[design.txt](../design.txt) for the architecture contract this builds on.

---

## 1. The two phases

zUI separates a screen's life into exactly two phases.

### Construction (once)

`ZuiHost.Build(tree)` (C#) / `zui::Host::build(root)` (C++) **constructs** the
native control tree from compiler-emitted nodes. It is the moment the WinForms
controls / Win32 HWNDs come into existence and are parented, named, themed, and
event-wired.

`Build()` is a **construction operation only**:

* Call it **once per screen**, during screen setup.
* It clears any previously constructed tree and builds a new one from scratch.
* Existing control instances, their handles, selection, scroll position, focus,
  and undo history do **not** survive a `Build()` call.
* It is **not** a render pass, a refresh, an update, a re-layout, or a
  reconciliation step. There is no virtual DOM and no diffing.

Treating `Build()` as an update mechanism is misuse. The hosts emit a debug
warning / assertion when `Build()` is called a second time on the same host so
that this misuse is caught early (see §6).

### Mutation (many times, for the life of the screen)

After construction, application code changes what the user sees by **mutating
the existing native controls in place**. Changing text, visibility, enabled
state, checked state, numeric value, selection, foreground/background, size,
focus, or collection contents is an incremental mutation — never a rebuild.

```
build once  ─►  mutate ─► mutate ─► mutate ─► ...  (screen lifetime)
```

There is no global `render()` function and no reactive render loop. A mutation
touches only the control(s) it names or the state dependents of the property it
changes.

---

## 2. Referencing controls after construction

Every node may carry `id`, `export`, and/or `bind`. After `Build()` the
application can retrieve the concrete native control by any of those names:

* C#: `host.Find("trackTitle")` → `System.Windows.Forms.Control?`
* C++: `host.find("trackTitle")` → `void*` (HWND)

Names resolve in priority order `export` → `bind` → `id`. Lookup is O(1) against
a registry populated during construction. Applications hold on to the names, not
to the control instances captured at build time (though caching a looked-up
instance for the screen's lifetime is fine — the instance is stable until the
next `Build()`).

The incremental mutation API operates through this registry:
`ui.SetText("trackTitle", title)` finds the registered control and sets its
text. The registry is the single source of truth for "which native control is
`trackTitle`".

### Incremental mutation API (implemented)

Both hosts expose one generic property channel plus ergonomic typed helpers.
Every call mutates the existing native control and returns without rebuilding.

| Operation | C# (`ZuiHost`) | C++ (`zui::Host`) |
| --- | --- | --- |
| generic write | `Set(name, prop, value)` | `set(name, prop, value)` |
| generic read | `Get(name, prop)` | `get(name, prop)` |
| text | `SetText` / `GetText` | `set_text` / `get_text` |
| visible | `SetVisible` | `set_visible` |
| enabled | `SetEnabled` | `set_enabled` |
| checked | `SetChecked` / `GetChecked` | `set_checked` / `get_checked` |
| numeric value | `SetValue` / `GetValue` | `set_value` / `get_value` |
| selection | `SetSelected` / `SetSelectedValue` / `GetSelected` | `set_selected` / `get_selected` |
| colour | `SetForeground` / `SetBackground` | `set_color` |
| size | `SetSize` | `set_size` |
| focus | `Focus(name)` | `set_focus` |
| lookup | `Find(name)` | `find(name)` |

`prop` is one of `text`, `visible`, `enabled`, `checked`, `value`, `selected`
(alias `selectedindex`), `selectedvalue`/`selectedtext`, `fg`/`foreground`,
`bg`/`background`, `width`, `height`, `focus`. An unknown control name is an
error (C# throws `KeyNotFoundException`; C++ logs and returns `false`/empty). A
property a given control does not support returns `false` / `null` rather than
rebuilding.

---

## 3. What must mutate rather than rebuild

The following changes **must** be expressible as in-place mutation of existing
native controls, with no `Build()` call:

| Change | Applies to |
| --- | --- |
| text / caption | labels, buttons, inputs, headings, status text |
| visible | any control and any container |
| enabled / disabled | any interactive control |
| checked | check, toggle |
| numeric value | slider, progress, spinner |
| selected index / value | select, dropdown, tabs |
| selected row / item | table, list, tree |
| foreground / background colour | where the control supports it |
| width / height | where a fixed dimension is meaningful |
| focus | any focusable control |
| collection contents | table, list, tree bound via `source=` |

Lists and tables in particular **must not** require rebuilding the screen (or
rebuilding the list control) when their row contents change. Collection updates
(set / append / insert / remove / replace / clear / refresh-item) mutate the
existing control and preserve selection by key where possible (zUI tasks ZU-67,
ZU-68).

---

## 4. C# / C++ parity is a framework requirement

The C# (WinForms) and C++ (Win32/common-control) backends are two
implementations of **one** framework. They will not share drawing code, and the
underlying Windows objects differ, but for every documented ZML/ZSL construct
they must expose **equivalent zUI semantics**:

* the same node kinds, attributes, flags, and nesting;
* the same control-lookup names and resolution order;
* the same logical event names and payload meanings (zUI task ZU-65);
* the same state / bind / handler behaviour (zUI task ZU-64);
* the same incremental mutation operations and their effects (ZU-63);
* the same layout meaning for structural nodes (zUI task ZU-66);
* the same theme tokens and their visual roles (zUI tasks ZU-69/70).

A feature that works on only one backend is incomplete. Conformance tests
(zUI task ZU-72) assert equivalence.

---

## 5. No browser, ever

The following may **not** be reintroduced as any part of implementing a zUI
feature:

* WebView2, CEF, Chromium, Electron, or any embedded browser / browser process;
* an HTML runtime, HTML document generation, or a `--backend html` path;
* a DOM or DOM-style API;
* a JavaScript / ECMAScript runtime;
* browser IPC, a renderer protocol, or a JSON message bus that stands in for one;
* runtime interpretation of CSS or any UI markup.

ZML/ZSL and `core/css` are **build-time inputs only**. `tests/check-native.py`
is the automated gate and must stay green.

---

## 6. `Build()` misuse guardrails

The hosts actively discourage using `Build()` as an update path:

* **C# `ZuiHost.Build`** — on the second and later call on the same host,
  `Debug.WriteLine` / `Trace` emits a warning naming the incremental API, and a
  `Debug.Assert` fires in debug builds. The call still rebuilds (so existing
  code keeps working) but the misuse is visible.
* **C++ `zui::Host::build`** — the equivalent path calls `OutputDebugStringW`
  with the same message and, in debug builds, `assert`s.

The message points here and to the incremental mutation API. Do not remove these
guards to silence a test; fix the caller to mutate instead of rebuild.

---

## 7. What this task does not do

Establishing this contract does not, by itself, implement the incremental
mutation API, the state/bind engine, the layout engine, collection binding,
virtualization, or the theme system. Those are the subsequent zUI tasks
(ZU-63 … ZU-73). This document is the target they build toward, and the bar any
future change to zUI's public behaviour must clear.
