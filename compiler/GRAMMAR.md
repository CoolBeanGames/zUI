# ZSL/ZML native UI language

ZSL and ZML are equivalent build-time syntaxes. `zslc.py` auto-detects them and
emits native C# or C++ source. Source UI files are never interpreted or shipped.

## Commands

```powershell
py compiler/zslc.py screen.zml --backend csharp -o Screen.g.cs
py compiler/zslc.py screen.zml --backend cpp -o screen.g.cpp
```

## ZSL grammar

```text
program ::= (node | state | handler)*
node    ::= ident string? attribute* ("{" node* "}")?
attribute ::= ident "=" value | "bind" ":" ident | "->" dotted | ident
state   ::= "state" "{" (ident "=" value ";"?)* "}"
handler ::= "on" dotted "{" statement* "}"
statement ::= ("emit" | "call") "(" string ("," value)? ")" ";"?
            | ident "=" value ";"?
value   ::= string | number | true | false | [] | {} | dotted
dotted  ::= ident ("." ident)*
```

Comments are `// line` or `/* block */`. Semicolons are optional.

## ZML mapping

Elements are nodes, text/title/label supplies node text, and attributes map to
node metadata. `bind`, `source`, and `on` have dedicated meanings. A value of
`true` creates a flag; `false` omits it.

```xml
<window title="Counter">
  <row>
    <button on="dec">-</button>
    <text bind="count"/>
    <button kind="primary" on="inc">+</button>
  </row>
</window>
<state><var name="count" value="0"/></state>
<on event="inc"><emit channel="count" value="count"/></on>
```

## Nodes

Structural: `window`, `titlebar`, `menubar`, `menu`, `item`, `sep`, `nav`,
`workspace`, `sidebar`, `section-label`, `panel`, `panel-body`, `row`, `col`,
`fill`, `grid`, `scroll`, `splitter`, `statusbar`, `contextbar`, `tabs`,
`tabpanel`, `empty`, `drop`.

Controls: `text`, `heading`, `button`, `field`, `input`, `textarea`, `check`,
`select`, `option`, `dropdown`, `slider`, `number`, `progress`, `spinner`,
`loading`, `table`, `column`, `list`, `tree`, `treeitem`, `image`, `console`.

`grid cols=N` places children row-major into N equal columns (`colspan` on a
child, `gap` on the grid). `scroll` gives its contents an own vertical
scrollbar. `splitter` wraps two children in a draggable `SplitContainer`
(`horizontal` flag; `min` on each child; `pos`). `tabs` shows one `tabpanel`
(by `id`) at a time, fires `ontab`, and `Set(name,"selected",id)` switches it;
a `headless` / `flat` flag drops the strip (a deck). `console` is a read-only
monospace log fed by `host.Append(name, text)`. `image src= | bind=` loads a
file path or byte buffer (`fit` = `uniform` | `fill` | `none` | `stretch`).

Common metadata: `id`, `export`, `bind`, `source`, `on`, `value`, `placeholder`,
`kind`, `shortcut`, `field`, `min`, `max`, `step`, `cols`, `colspan`, `gap`,
`pos`, `src`, `fit`, `lines`, `width`, `height`, `disabled`, `active`,
`selectable`, `headless`, `tooltip`.

Extra event channels (payload in parentheses): `onchange` (editable → current
value on every edit — same as `on` for `input`), `oncommit` (editable → value on
Enter / blur), `onactivate` (`table`/`list`/`tree` → item key on double-click or
Enter), `ontoggle` (`button kind="toggle"` → `"true"`/`"false"`), `ontab`
(`tabs` → selected `tabpanel` id), `oncontext` (right-click / Menu key on a
`selectable` control → `{control, keys}`; the handler calls
`host.PopupMenu(name, items)`), `ondrop` (drop onto the node → `{target,
targetKey?, keys?}` for an internal drag carrying `dragsource` keys, or
`{target, paths}` for an OS file drop), `onmenuopen` (menu-bar `menu` about to
drop → its path). `dragsource` (flag) makes a `selectable` control a drag
source. `<option value="x">Label</option>` — the change event and
`selectedvalue` use the value.

Menu-bar items are addressable after `Build()` by their `"Menu/Item"` path:
`host.SetMenuEnabled(path, bool)` / `host.SetMenuChecked(path, bool)`.

The compiler preserves this information in typed node constructors. Native host
implementations decide the concrete control, layout, theme properties, and event.

## Runtime semantics

Compiling a screen produces a **construction** description. The generated
`Build()` / `build_ui()` entry point builds the native control tree once. `state`,
`bind`, top-level `on` handlers, and `source` are declarative relationships that
the native runtime wires to in-place control mutation — they are never a signal
to re-run `Build()`.

### state / bind / handlers

The generated entry point, after building the tree, calls (in order):

1. `State.Init(name, value)` for every `state` entry — string values, seeded but
   not yet propagated.
2. `Bind(stateProperty, controlName)` for every node carrying `bind:` — the
   control name is its `export`, else `id`, else the bind name itself.
3. `On(channel, …)` for every channel named by a node `->` / `on=` *or* by a
   top-level `on` block. The generated body lowers each statement
   (`x = x.plus1` → `Mutate`, `x = y` → `Assign`, `x = 5` → `Set`,
   `emit(c, x)` → `Send(c, GetString(x))`, `emit(c)` → `Send(c, "")`) and then
   calls the application hook (`partial void On_<channel>` in C#, the `handlers`
   map entry in C++).
4. `State.Flush()` once, applying every seeded value to its bound controls.

A `bind` on an editable control (`input`, `textarea`, `check`, `slider`,
`select`) is two-way: a user edit writes back to the property. One property may
be bound to many controls. `Mutate` ops are `plus1`, `minus1`, `toggle`/`not`.

### source — collection binding

A `table`, `list`, or `tree` carrying `source="<name>"` is filled through the
host collection API, never `Build()`:

```
host.SetRows(name, records)      host.AppendRow / InsertRow(i,…) / RemoveRow(key)
host.UpdateRow(key, record)      host.RefreshRow(key) / ClearRows / GetRowKeys
```

A record is a string map. A table row is `{ key, <field>: value, …, state? }`
(`<field>` matches `<column field="…">`); a `list`/`tree` item is `{ key, text,
…, state? }`. Rows are identified by `key`; selection is preserved by key across
every update. `state` (`normal` | `warn` | `error` | `new` | `active`) applies a
themed row style. `host.GetSelection(name)` / `SetSelection(name, keys)` and
`Get`/`Set(name, "selection", …)` read/write the selection by key.

See [../core/RUNTIME_CONTRACT.md](../core/RUNTIME_CONTRACT.md) §3 for the frozen
contract that generated code and both hosts must honour, including C#/C++
semantic parity and the prohibition on any browser/DOM/JS runtime.
