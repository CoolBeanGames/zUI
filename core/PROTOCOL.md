# Native event protocol

Generated nodes carry an optional `on` channel. The host connects the native
control event to callbacks registered with `On`/`on`. `Send`/`send` invokes the
same in-process channel dispatcher for application-driven updates. Payloads are
UTF-8 strings; applications may choose JSON when structured values are useful.

No serialization boundary or external renderer is required.
