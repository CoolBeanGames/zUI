# ZSL - the zUI Scripting Language

ZSL is the declarative language that UI in a zUI application is written in. It is
**compiled ahead of time** - never interpreted at runtime. The compiler
(`zslc.py`) turns a `.zsl` file into:

| backend  | output                                                              |
|----------|--------------------------------------------------------------------|
| `html`   | a self-contained zUI document (HTML + generated glue JS)           |
| `csharp` | a C# partial class that builds that document inside a `ZuiHost`    |
| `cpp`    | a C++ translation unit that does the same inside a `zui::Host`     |

The `csharp` / `cpp` backends emit real source that is compiled into the host
binary, so the `.zsl` file and a parser are not shipped - "compiles down to
native code for performance".

## Lexical

```
comment   ::= "//" ... EOL  |  "/* ... */"
string    ::= '"' ... '"'
number    ::= digit+ ("." digit+)?
ident     ::= (letter | "_") (letter | digit | "_" | "-")*
punct     ::= "{" "}" "=" ":" "." "," ";" "->" "(" ")" "[" "]"
```

Newlines are not significant. `;` after a statement is optional.

## Grammar

```
program    ::= (node | state | handler)*

state      ::= "state" "{" (ident "=" literal)* "}"

handler    ::= "on" dotted "{" stmt* "}"

node       ::= ident string? attr* block?
attr       ::= "bind" ":" ident            // two-way binding to a state field
             | "source" "=" ident          // list source for repeating nodes
             | ident "=" value             // plain attribute
             | ident                       // boolean flag (e.g. `selectable`)
             | "->" dotted                 // event -> handler / emit
block      ::= "{" node* "}"

stmt       ::= "emit" "(" string ("," expr)? ")" ";"?
             | "call" "(" string ("," expr)? ")" ";"?
             | ident "=" expr ";"?

value      ::= string | number | ident
literal    ::= string | number | "true" | "false" | "[" "]" | "{" "}"
expr       ::= literal | dotted
dotted     ::= ident ("." ident)*
```

## Nodes

Every node name maps to a zUI component/class:

`window titlebar menubar menu item sep nav workspace sidebar section-label
panel panel-body row col fill text heading button field input textarea check
select option table column tabs tab tabpanel statusbar spinner progress drop
empty grid badge`

Common attributes: `id`, `class`, `value`, `label`, `placeholder`, `icon`,
`kind`, `shortcut`, `checked`, `disabled`, `field` (table column -> row key),
`width`, `height`, `min`, `max`.

## Bindings & events

- `bind:foo` on an input/select/check generates glue that (a) writes the control
  from `state.foo` whenever the host pushes a `state` message and (b) sends
  `{channel:"state", payload:{foo: <value>}}` on change. It also marks the node
  `data-zui-id="foo"` so the host can address it directly (see below).
- `export="name"` (any node) marks it `data-zui-id="name"` for the runtime
  **component IO** layer: the host reads it with `zui.field`/`zui.values` or a
  `query`, writes it with `zui.set` or a `set` message, and receives a `value`
  message on every user change. See `core/PROTOCOL.md`.
- `-> some.action` on a button/item generates a click handler that runs the
  matching `on some.action { ... }` block, or - if none exists - `emit("some.action")`.
- `source=items` on a `table` (with `column` children) or a container repeats its
  children once per element of `state.items`.

## ZML - the angle-bracket syntax

The same UI tree can be written in an XML-style syntax. `zslc` **auto-detects**:
a source whose first significant character is `<` is parsed as ZML, anything else
as brace ZSL. Both build the identical AST and produce byte-identical output, so
the choice is pure preference and the two can coexist in a project.

```xml
<panel title="Tracks">
  <tabs>
    <tab label="Overview" on="tab.overview">
      <text bind="summary"/>
    </tab>
    <tab label="Details">
      <table source="rows" selectable="true">
        <column field="name">Name</column>
      </table>
    </tab>
  </tabs>
</panel>
```

Mapping:

| ZML                              | meaning                                            |
|----------------------------------|---------------------------------------------------|
| `<name …>` / `<name …/>`          | a node (self-closing = no children)               |
| element text content             | the node's label/text (`<column>Name</column>`)   |
| `title="…"` or `label="…"`        | also sets the node text                           |
| `bind="x"`                       | two-way binding to `state.x`                       |
| `source="x"`                     | list source for a repeating node                  |
| `on="some.event"`                | event → handler / `emit`                          |
| `flag` or `flag="true"`          | boolean flag (`selectable`, `active`, `fill`, …)  |
| `k="v"`                          | plain attribute (`id`, `kind`, `shortcut`, …)     |
| `<!-- … -->` and `//` line       | comments                                          |

State and handlers:

```xml
<state>
  <var name="count" value="0"/>
</state>

<on event="inc">
  <set field="count" value="count.plus1"/>
  <emit channel="count" value="count"/>
</on>
```

`value=` is read as an expression: a bare identifier/dotted path is a `state`
ref, a number/`true`/`false` is a literal, anything else is a string. The
`x.plus1` / `x.minus1` helpers work the same as in ZSL.

## Example

```zsl
window "Counter" {
  row {
    button "-"      -> dec
    text  bind:count
    button "+"      -> inc
  }
}

state { count = 0 }

on inc { count = count.plus1; emit("count", count) }
on dec { count = count.minus1 }
```

See `../examples/` for full programs.
