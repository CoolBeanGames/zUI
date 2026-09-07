# Native samples

- `csharp`: generated showcase hosted in a WinForms window.
- `cpp`: the same generated showcase hosted by a Win32 window.
- `zsheets`: native CSV editor using `ToolStrip` and `DataGridView`.
- `zforge`: an RPG character-sheet builder authored in `CharacterForge.zsl`
  (menubar, nav, sidebar, tree, table, sliders, selects, checks, textarea,
  progress) and driven entirely through the state/bind/handler layer.

Build all samples with `./build.ps1 -Config debug`. `-Config test` also runs each
sample's `--self-test`.

## zforge

`CharacterForge.zsl` is the only UI source; `build.ps1` recompiles it to
`zforge/generated/CharacterForgeUi.g.cs`. `Program.cs` calls `Build(host)` once,
then does everything else by mutating existing controls and writing state:
`host.State.Set("str", …)` propagates to the bound slider, `host.State.Watch`
recomputes the sidebar budget, and the `roll.attrs` / `pack.add` / `forge.random`
channels are ordinary `host.On` handlers. No second `Build()` call anywhere.
