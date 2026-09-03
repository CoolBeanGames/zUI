# zUI host ⇄ UI protocol

One JSON channel carries everything between a zUI document and its host. Every
message is `{ "channel": <string>, "payload": <any> }`.

- **UI → host:** `zui.send(channel, payload)` → the binding's IPC
  (`window.__zuiHost.postMessage` / `chrome.webview.postMessage`).
- **host → UI:** the binding calls `zui._dispatch({channel, payload})`;
  handlers register with `zui.receive(channel, fn)`.

C#: `host.Send(channel, obj)` / `host.On(channel, json => …)`.
C++: `ui.send(channel, json)` / `ui.on(channel, cb)`.

---

## Component IO — reading & writing any component

Give a component `data-zui-id="name"` (in ZSL/ZML: `export="name"`, or any
`bind:name`). It is then both readable and writable.

### Values by component

| component | `data-zui-id` on | kind | value |
|---|---|---|---|
| text / search input, textarea | the `<input>`/`<textarea>` | `text` | string |
| checkbox | the `<input type=checkbox>` | `boolean` | bool |
| toggle button | the `<button aria-pressed>` | `boolean` | bool |
| dropdown / select | `<select>` or `.zui-select` | `select` | selected value |
| progress bar | the `.zui-progress__bar` | `progress` | `0`–`100`, or `"indeterminate"` |
| plain label / text node | the `<span>` | `text` | text content |
| button | the `<button>` | `button` | — (fires click events) |
| scrolling panel | the scroll container | `scroll` | `{top,left,topPct,leftPct}` |
| table / list selection | `[data-zui="selectable"]` container | — | see `selection` channel |

### UI → host  (automatic, on every user change)

```
channel "value"   { id, kind, value }          // + committed:true on blur for text
                  { id, kind:"button", value:null, event:"click" }
channel "submit"  { form, values }             // a [data-zui-submit] button;
                                               //   values = every id in the nearest [data-zui-form]
```

### host → UI

```
channel "set"       { id, value }
channel "set-many"  { "<id>": value, ... }
channel "query"     { id }        → UI replies  "value"  { id, kind, value }
channel "query"     (no id)       → UI replies  "values" { "<id>": value, ... }
```

Writing a **button**: `value` may be `"click"` (fires it, with a flash), or an
object `{ busy, disabled, pressed, label }`.
Writing a **scroll panel**: a number (px `scrollTop`) or `{top,left,topPct,leftPct}`.
Writing a **progress bar**: `0`–`100` or `"indeterminate"`.

Programmatic writes never echo a `value` message back (no feedback loops).

### In-page API (same behaviour, no host)

```js
zui.values(scope?)      // { id: value, ... }
zui.field(id)           // one value
zui.set(id, value)      // write; zui.set({id: value, ...}) for many
zui.bind(id, fn)        // fn(value, msg) on every change; returns an unbind fn
zui.mark(id, level, msg?)         // "error" | "warn" | "ok" - border + field message
zui.validate(scope, {id: rule})   // rule: {required, pattern, min, max, validate:fn}
zui.renameInPlace(el, onCommit)   // swap text for an input; Enter/blur commit, Esc cancel
```

An element with `data-zui-rename="channel"` renames on double-click / F2 and
emits `{channel, {value}}` on commit.

---

## Other channels emitted by components

| channel | payload | from |
|---|---|---|
| `tab` / `tab-close` | tab id | nav & tab strips |
| `selection` | `["rowId", …]` | selectable tables/lists |
| `select` | `{name, value}` | dropdowns |
| `toggle` | `{name, pressed}` | toggle buttons |
| `drop` | `{target, paths[], files[]}` | drop targets |
| `reorder` | `{name, order[]}` | `[data-zui="reorder"]` lists |
| `splitter` | `{basis}` | panel splitters |
| `window` | `"minimize"\|"maximize"\|"restore"\|"close"` | title-bar buttons |
| `theme-changed` | theme name | `zui.setTheme()` |
| a node's `-> some.event` | its event payload | ZSL/ZML events |

## Channels a host can push

| channel | payload | effect |
|---|---|---|
| `set` / `set-many` / `query` | see above | component IO |
| `theme` | `"holo"` \| `"clean"` | `zui.setTheme()` (wire in your doc) |
| `state` | `{ field: value, … }` | updates ZSL/ZML `state` + re-renders bindings |
| anything a `zui.receive(...)` in your document listens for | — | app-specific |
