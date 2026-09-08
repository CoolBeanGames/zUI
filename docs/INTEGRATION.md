# zUI — integration guide for host projects

> ## ⚠️ Which zUI to pull
>
> - **Repo:** `https://github.com/CoolBeanGames/zUI`
> - **Use `main`** (or a `zUI_buildN` tag — highest N is newest). Nothing else.
> - Branches other than `main` (`refactor`, `final-build-push`, …) and any tag
>   below `zUI_build4` are **historical**. An early version of zUI rendered its
>   UI in an embedded **WebView2 / HTML / CSS / JS** — that architecture is
>   **dead and removed**. If you find `webview`, a `--backend html`, `core/js/`,
>   or an `.html` UI document, you are on the wrong branch — `git checkout main`.
> - **Sanity check after cloning:** `core/RUNTIME_CONTRACT.md` exists and its
>   §6 says *"No browser, ever"*; `compiler/zslc.py --backend` offers only
>   `csharp` and `cpp`; `tests/check-native.py` is the gate.

zUI is a themeable UI framework that sits on top of a host application (a music
app, a game engine, a project-management app, …) so every one of them presents
the same UI. This file tells an agent working in one of those projects how to
pull zUI in and use it. The visual rules are in Part 2.

zUI is a **native Windows UI framework with web-like authoring**. You describe a
screen in a declarative language; a compiler emits C# that builds WinForms
controls or C++ that builds Win32/common controls. There is **no browser,
WebView2, DOM, HTML document, stylesheet cascade, or JavaScript runtime** — and
you must not introduce one to implement a zUI feature. Markup and theme-source
CSS are build-time inputs only; they are never shipped or parsed at runtime.

Repo: <https://github.com/CoolBeanGames/zUI> — branch **`main`**, current release
**`zUI_build4`**.

Reference docs — read these before extending anything:

| file | what |
| --- | --- |
| `README.md` | overview + architecture |
| `design.txt` | the global design & implementation contract (mandatory) |
| `core/RUNTIME_CONTRACT.md` | frozen public runtime behaviour: construction vs. mutation, control lookup, state/bind/handlers, C#/C++ parity |
| `core/PROTOCOL.md` | the host ↔ UI event channel and per-control payloads |
| `compiler/GRAMMAR.md` | the ZSL / ZML UI language and every supported node |
| `docs/QUICKSTART.md` | minimal end-to-end integration |
| `samples/` | runnable native apps (see [Examples](#examples)) |

--------------------------------------------------------------------------------

# Part 1 — using zUI

## 1. Get it

Vendor it as a git submodule (preferred) or a plain clone, on `main`:

```
git submodule add https://github.com/CoolBeanGames/zUI.git third_party/zui
git -C third_party/zui checkout main
```

What the host consumes:

| path | role |
| --- | --- |
| `compiler/zslc.py` | the ZSL/ZML → C#/C++ compiler (Python 3, no dependencies) |
| `bindings/csharp/ZUI.csproj` | the C# runtime (WinForms, `net8.0-windows`, no packages) |
| `bindings/cpp/` | the C++ runtime (CMake target `zui`; links `user32` `gdi32` `comctl32`) |
| `compiler/GRAMMAR.md` | language + node reference |
| `core/RUNTIME_CONTRACT.md` | behaviour you can rely on |

Build/verify requirements: Python 3, .NET 8 SDK, and — for the C++ backend only —
CMake + MSVC C++ tools + the Windows SDK. The C# path needs none of the C++
tools.

## 2. Embed it

zUI renders into a `Form` or `Panel` the host owns (C#), or an `HWND` (C++). Pick
the binding for your language; both produce equivalent zUI semantics from the
same source.

**C# (WinForms, or WPF via `WindowsFormsHost`):**

```csharp
using var host = new ZUI.ZuiHost(form);          // any Form or Panel you own
new MyApp.Generated.MainUi().Build(host);         // construct the tree ONCE
host.On("save", payload => Save(payload));        // handle an event channel
host.SetTheme("clean");                           // "holo" (default) or "clean"
```

**C++ (Win32 + common controls):**

```cpp
zui::Host host(hwnd);
std::unordered_map<std::string, zui::MessageHandler> handlers;
handlers["save"] = [](const std::string& p){ save(p); };
build_ui(host, handlers);                         // generated entry point, once
host.set_theme("clean");
```

Reference `bindings/csharp/ZUI.csproj` from your `.csproj`, or link the `zui`
CMake target. No runtime assets, no external packages.

## 3. Author the UI

Write screens in **ZSL** (brace) or **ZML** (angle-bracket) — same tree, same
output, auto-detected — and compile ahead of time. Generated code is checked into
your project and compiled by your normal toolchain; the `.zsl`/`.zml` source is
not shipped.

```
py third_party/zui/compiler/zslc.py ui/main.zml \
    --backend csharp --class MainUi --namespace MyApp.Generated \
    -o Generated/MainUi.g.cs

py third_party/zui/compiler/zslc.py ui/main.zml \
    --backend cpp -o generated/main.g.cpp
```

Backends are `csharp` and `cpp` only. The generated file contains typed node
construction, `state`/`bind` wiring, and event-channel hookup — nothing else.

Layout is flow-based, never x/y. Nest `row` / `col`; the container docks each
child to fill its cell. `fill`, `workspace`, `sidebar`, `panel`, `table`, `tree`
and `textarea` claim leftover space; a `menubar` becomes a real menu; a
`statusbar` docks to the bottom. `compiler/GRAMMAR.md` lists every node and
attribute.

## 4. Talk to it

**Events.** One in-process channel. A node carries `on="file.save"` or
`-> file.save`; a top-level `on file.save { … }` block also works with no node.
The host wires the native control event to callbacks:

| | respond to an event | push an app message |
| --- | --- | --- |
| C# | `host.On("file.save", p => …)` | `host.Send("file.save", "…")` |
| C++ | `host.on("file.save", cb)` | `host.send("file.save", "…")` |

Payloads are UTF-8 strings; use JSON by convention when a value is structured.
Per-control payload meanings (button → click, input → text, slider → value,
table → selected row, …) are in `core/PROTOCOL.md`.

**Reading and writing components.** Tag a node with `id="name"`, `export="name"`,
or `bind="stateProp"` (resolution order: `export` > `bind` > `id`). Then:

```
host.Find("name")                 -> the concrete WinForms Control / HWND
host.GetText / GetValue / GetChecked / GetSelected
host.SetText / SetValue / SetChecked / SetVisible / SetEnabled /
    SetSelected / SetForeground / SetBackground / SetSize / Focus
```

**State / bind / handlers.** `state { count = 0 }` in the source declares an
in-process value model. `bind="count"` links a state property to a control's
natural property — one-way for display controls, two-way for editable ones
(`input`, `check`, `slider`, `select`). One property can drive many controls.

```
host.State.Set("count", "42")     // propagates to every bound control + watchers
host.State.Watch("count", v => …)
host.Bind("count", "someControl")
```

Full semantics: `core/RUNTIME_CONTRACT.md` §3.

## 4a. Lists, tables, trees — `source=` collection binding

A `table` / `list` / `tree` with `source="x"` is filled through the collection
API, **never `Build()`**. Records are string maps keyed by `key`; a table row's
other fields match `<column field="…">`, a list/tree item uses `text`.

```csharp
host.SetRows("tracks", records);   // IEnumerable<IReadOnlyDictionary<string,string>>
host.AppendRow / InsertRow(i, …) / RemoveRow(key) / UpdateRow(key, patch)
host.RefreshRow(key) / ClearRows("tracks") / GetRowKeys("tracks")
host.GetSelection("tracks") / SetSelection("tracks", keys)   // by key, no rebuild
```

Selection is preserved by key across every update. A record's `state`
(`warn` | `error` | `active` | `new`) themes the row. Channels a selectable
list/table/tree fires: `on` → JSON array of selected keys; `onactivate` →
activated key (double-click / Enter); `oncontext` → `{control, keys}` (the
handler then calls `host.PopupMenu(name, items)`). A `table virtual` handles
tens of thousands of rows without a per-row control (`onsort` → `{field, dir}`).

## 4b. The rest of the widget set

| need | node / API |
| --- | --- |
| numeric field | `<number min= max= step=/>`, two-way `bind`, `onchange` / `oncommit` |
| toggle button | `<button kind="toggle">`, `ontoggle`, `Get`/`Set "pressed"` |
| tooltip | `tooltip="…"` on any node |
| dropdown value ≠ label | `<option value="mp3-320">MP3 — 320</option>` |
| N-column form | `<grid cols="4">`, `colspan` on a child |
| own scrollbar | `<scroll>{ … }` |
| tabbed / swapped panes | `<tabs>{ <tabpanel id="a"> … }`, `ontab`, `Set "selected"`; `headless` = deck |
| draggable divider | `<splitter>{ a  b }` (`horizontal`, per-child `min`, `pos`) |
| append-only log | `<console/>` + `host.Append(name, line)` |
| image / album art | `<image src=|bind=/>`, `fit=`; `<column kind="image">` |
| rich list rows | `<list template>` — record `image`/`icon`/`text`/`subtitle`/`badge` |
| icons | `icon="play"` on button/label; `button kind="icon"`; `ZuiIcons.Names` |
| context / app menu | `host.PopupMenu`, `oncontext`; `host.SetMenuEnabled/SetMenuChecked("File/New", …)`, `onmenuopen` |
| drag & drop | `dragsource` flag; `ondrop="ch"` → `{target,targetKey?,keys}` or `{target,paths}` |
| rename in place | `rename="ch"` on a list/tree item → `{key, value}` |
| custom drawing | `<canvas/>` + `host.OnPaint(name, (g, rect) => …)` / `host.Redraw(name)` |
| "busy" emphasis | `host.Set(name, "attention", true)` |

Both backends now cover §4a/§4b. The C++ host (`bindings/cpp/zui.h`) exposes the
same node kinds, channel names, and payload shapes; the API spellings are
snake_case: `host.set_rows` / `append_row` / `remove_row` / `get_selection` /
`popup_menu` / `set_menu_enabled` / `set_image` / `on_paint`. Polished C++
layout for `grid` / `scroll` / `splitter` and full C++ theme rendering are the
remaining follow-ups (ZU-66 / ZU-70).

## 5. Build once, then mutate — the runtime contract

`Build()` / `build_ui()` is a **construction** operation. Call it exactly once
per screen. Every later change — text, value, visibility, selection, list
contents, theme — is an in-place mutation of the existing controls via the API
in §4. Calling `Build()` again destroys controls, selection, and focus and emits
a warning. There is no `render()` and no reactive re-render loop.

```
build once  ->  mutate -> mutate -> mutate -> ...   (screen lifetime)
```

## 6. Themes

Two shipped themes: **holo** (default, dark) and **clean** (light). Switch at
runtime:

| C# | C++ |
| --- | --- |
| `host.SetTheme("clean")` | `host.set_theme("clean")` |

Theme values are typed: `ZuiTheme` (C#) and `zui::Theme` (C++). `core/css/` holds
the design-token source these are translated from — designers edit it, the host
does not load it. A new theme means adding a translated value set to both runtime
types and applying it to native control properties; it never touches component
code.

## 7. Keep it consistent

- `./build.ps1 -Config test` is the required gate: compiler tests, the
  native-only policy (`tests/check-native.py` — fails on any browser document,
  package, or rendering reference), zero-warning C# builds, C# host tests, C++
  CTest, and every sample's `--self-test`.
- Adding or changing a widget covers **both** runtimes (unless a task says
  otherwise). The checklist is in `design.txt` → *"Adding or changing a
  component"*: grammar node + attributes, compiler preserves them, C# `AddNode`
  switch → WinForms control, C++ `create_node` → common control, register
  `id`/`export`/`bind`, route interaction through the `On`/`on` channel,
  generator tests + native host tests, green `build.ps1 -Config test`.
- Do not substitute snapshot/markup tests for a real control-construction check.

## Examples

| sample | shows |
| --- | --- |
| `samples/csharp` | the showcase screen hosted in a WinForms window |
| `samples/cpp` | the same showcase, Win32 |
| `samples/zforge` | a form-heavy app: `state`/`bind`, nav-driven tab switching, most of the widget set |
| `samples/zsheets` | a native CSV editor |

All build with `./build.ps1`; `-Config test` also runs each one's `--self-test`.

--------------------------------------------------------------------------------

# Part 2 — visual design standard

Treat these as requirements whenever creating or modifying UI. New UI must look
like it was there from the start. **Do not redesign the visual identity unless
explicitly told to.**

The structure, spacing, typography, density and interaction rules apply to every
theme; only the palette changes between them. The palette lives in the runtime
theme types (`ZuiTheme` / `zui::Theme`), sourced from `core/css/tokens.css`.

## Identity

Default theme **holo** — Google's Android Holo: dark chrome (near-black app,
`#171b20`-ish surfaces), Holo-blue `#33b5e5` accent, thin low-contrast dividers,
uppercase accent-coloured section headers with an underline rule, blue-tinted
selection with a left accent bar, light text.

Alternative theme **clean** — a light, neutral "native Windows utility" palette
(off-white app, white surfaces, muted desaturated-blue accent).

Both should feel: **clean · quiet · fast · native · compact · organized ·
functional.** Not: flashy · web-like · mobile-first · card-heavy · oversized ·
decorative.

Avoid: gradients (beyond the very subtle ones already in the theme),
glassmorphism, large rounded cards, oversized controls, giant headings, excessive
whitespace, floating elements, decorative shadows, pill controls without a
functional reason.

## Structure

Strong horizontal hierarchy, separated by thin borders not big containers:

```
native title bar → application menu (menubar) → primary nav strip (nav)
    → workspace (sidebar + fill) → status/device bar (statusbar)
    → contextual/playback bar (contextbar)
```

Sidebars are narrow and functional (`sidebar`, with `tree` for hierarchy):
square selection, pale-accent highlight, small secondary section headings.

Main content is `panel` / `col` / `row` / `table` / `tree` regions that share one
background and are divided by 1px borders — never independent floating cards.

## Type

Platform-native UI font (Segoe UI first), no web fonts. Compact scale, hierarchy
from **weight**, not dramatic size jumps. Do not set font sizes in generated
screens — the runtime theme owns type metrics; use `heading` / `section-label` /
`text` and let the theme size them.

## Controls

Buttons: compact, rectangular, thin border, very light fill, minimal radius —
not web CTAs. Inputs: light fill, thin border, small radius, compact height — not
giant/mobile. Tables: compact rows, subtle separators, clear left-aligned
headings; highlight rows only for a functional reason (selection, missing
metadata, error). No decorative zebra striping.

## Spacing & borders

Desktop-productivity density, not a touch UI. Lean on 1px low-contrast borders
for separation rather than padding and containers. Don't hand-tune per-screen
spacing — the layout engine and theme provide the scale.

## Iconography

Small, monochrome, line-based, consistent weight, from the shared zUI icon set
(`core/icons/`), referenced by name. Never emoji as UI icons, never mixed icon
styles.

## Interaction

Behave like a desktop app: right-click context menus, double-click actions, drag
and drop, keyboard shortcuts including Ctrl+Z / Ctrl+Y, Tab order, multi /
shift / ctrl selection, rename-in-place, tooltips, standard Windows key
behaviour. Focus, keyboard navigation, DPI, IME and painting come from the native
Windows control stack — reuse it, do not reimplement it. Behaviour zUI adds lives
in the host bindings (`bindings/csharp`, `bindings/cpp`), not in generated
screens.

## Consistency rule

Before building any new UI: inspect existing components, reuse existing patterns,
match existing spacing/sizing/typography/borders/behaviour. If a feature needs a
new reusable pattern, add it as a shared node + both-runtime implementation so
later work reuses it. A user must not be able to tell which screens were built at
different times or by different agents.
