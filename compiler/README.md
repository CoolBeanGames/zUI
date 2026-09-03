# zslc - the ZSL compiler

`zslc.py` compiles a UI description (see [`GRAMMAR.md`](GRAMMAR.md)) ahead of
time. Nothing is interpreted at runtime.

Two interchangeable syntaxes, auto-detected (a source starting with `<` is ZML):

- **brace ZSL** (`.zsl`) — `panel "Tracks" { button "Play" }`
- **ZML** (`.zml`) — `<panel title="Tracks"><button>Play</button></panel>`

Both build the same AST and emit byte-identical output.

```
py compiler/zslc.py examples/showcase.zsl --backend html   -o out.html
py compiler/zslc.py examples/showcase.zsl --backend csharp -o CompiledUi.g.cs --class ShowcaseUi --namespace App.Ui
py compiler/zslc.py examples/showcase.zsl --backend cpp    -o showcase.g.cpp --func build_showcase
```

## Pipeline

```
source ──▶ lex ──▶ parse ──▶ Program{roots, state, handlers}
                                  │
                    ┌─────────────┼─────────────┐
                  html          csharp          cpp
              self-contained   partial class   translation unit
              zUI document     for ZuiHost     for zui::Host
```

- **html** - a standalone document that links `core/css/zui.css` + `core/js/zui.js`
  and a generated glue script that binds `state`, wires `-> action` events to the
  `on { … }` handler blocks (falling back to `emit`), and repeats `source=` rows.
- **csharp / cpp** - emit source that embeds the compiled document and registers
  a typed hook per distinct event (`partial void On_foo(...)` / `void on_foo(...)`),
  so the UI ships as compiled native code with no parser dependency ("compiles
  down to native code for performance").

## Tests

```
py compiler/tests/test_compile.py
```

Covers the lexer, parser, error reporting, and all three backends against the
example programs. `build.ps1` runs this in the `test` configuration.

## Limitations (bones stage)

- Node attributes must sit on the same line as the node keyword; a token on a
  later line starts the next sibling. This is the only place newlines matter.
- Expression language is intentionally tiny: literals, `state` refs, and the
  `x.plus1` / `x.minus1` helpers. Richer expressions are a later task.
- `csharp` / `cpp` backends currently hand the host the compiled HTML plus typed
  hooks; direct widget-tree construction APIs are a follow-up.
