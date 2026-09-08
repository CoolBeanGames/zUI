# Native event protocol

Generated nodes carry an optional `on` channel. The host connects the native
control event to callbacks registered with `On`/`on`. `Send`/`send` invokes the
same in-process channel dispatcher for application-driven updates. Payloads are
UTF-8 strings; applications may choose JSON when structured values are useful.

No serialization boundary or external renderer is required.

## Relationship to the runtime contract

This channel is the event/messaging layer only. Screen construction vs. in-place
mutation, control lookup by `id`/`export`/`bind`, C#/C++ parity, and the
no-browser rule are defined by the frozen [runtime contract](RUNTIME_CONTRACT.md).
`Send`/`send` and `On`/`on` are the "respond to an event" and "push an
application message" parts of that contract; they are not a render or refresh
mechanism, and application code must never call `Build()`/`build()` to react to
an event.

## Normalized `on` / `->` payloads (ZU-65, in progress)

The `on` channel carries a meaningful per-control payload:

| control | fires on | payload |
| --- | --- | --- |
| `button` | click | `""` |
| `button kind="toggle"` | click | `"true"` / `"false"` (also `ontoggle`) |
| `input` / `textarea` | text change | current text |
| `check` | toggle | `"true"` / `"false"` |
| `slider` | value change | integer value |
| `number` | value change | integer value |
| `select` / `dropdown` | selection change | selected **option value** (`<option value="…">`, else the label) |
| `table` / `list` / `tree` (`selectable`) | selection change | JSON array of selected item **keys** |

Extra channels: `oncommit` (editable → value on Enter / blur), `onactivate`
(`table`/`list`/`tree` → item key on double-click or Enter). Key-addressed
selection and the `source=` collection API are in
[../compiler/GRAMMAR.md](../compiler/GRAMMAR.md); item keys come from each
record's `key` field.

The full C++/C# parity table and high-frequency-event coalescing remain part of
ZU-65.
