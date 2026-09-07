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

The normalized per-control event payload contract (button → click, input →
text, slider → value, table → selected row, …) is specified in this file as it
is implemented (zUI task ZU-65).
