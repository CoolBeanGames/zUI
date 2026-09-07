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
`fill`, `grid`, `statusbar`, `contextbar`, `tabs`, `tabpanel`, `empty`, `drop`.

Controls: `text`, `heading`, `button`, `field`, `input`, `textarea`, `check`,
`select`, `option`, `dropdown`, `slider`, `progress`, `spinner`, `loading`,
`table`, `column`, `tree`, `treeitem`.

Common metadata: `id`, `export`, `bind`, `source`, `on`, `value`, `placeholder`,
`kind`, `shortcut`, `field`, `min`, `max`, `width`, `height`, `disabled`,
`active`, `selectable`.

The compiler preserves this information in typed node constructors. Native host
implementations decide the concrete control, layout, theme properties, and event.
