# zUI themes

A theme is a single CSS file that re-declares the design tokens from
`tokens.css` under a `[data-zui-theme="<name>"]` selector. **No component CSS
changes for a new theme** — every component reads tokens only.

## Shipped themes

| theme   | file                | look                                                     |
|---------|---------------------|----------------------------------------------------------|
| `holo`  | `themes/holo.css`   | **default.** Android Holo — dark chrome, Holo-blue `#33b5e5` accent, accent caps headers with an underline rule, blue-tinted selection with a left accent bar |
| `clean` | `themes/clean.css`  | light, neutral native-Windows-utility palette (the `design.txt` aesthetic), muted blue accent |

`:root` in `tokens.css` already carries the `holo` values, so a document with no
`data-zui-theme` renders in Holo.

## Selecting a theme

```html
<html data-zui-theme="holo">   <!-- or "clean" -->
<link rel="stylesheet" href="core/css/zui.css">
<link rel="stylesheet" href="core/css/themes/holo.css">
<link rel="stylesheet" href="core/css/themes/clean.css">
```

At runtime: `zui.setTheme("clean")` (sets the attribute on `<html>` and emits a
`theme-changed` message). Hosts push `zui.receive("theme", …)`.

## Authoring a new theme

1. Copy `themes/clean.css`, rename the selector to `[data-zui-theme="mytheme"]`.
2. Set every token. The full list, with roles, is documented in `tokens.css` and
   `TYPOGRAPHY.md`. The colour hooks a theme must cover:

   surfaces: `--zui-bg-app --zui-bg-surface --zui-bg-surface-alt --zui-bg-raised
   --zui-bg-selected --zui-bg-hover --zui-bg-active --zui-bg-selected-strong`
   lines: `--zui-border --zui-border-strong`
   text: `--zui-text --zui-text-secondary --zui-text-disabled --zui-text-on-accent
   --zui-label-color`
   accent: `--zui-accent --zui-accent-hover --zui-accent-weak`
   status: `--zui-warn --zui-error --zui-ok --zui-on-danger`
   parts: `--zui-scrollbar-thumb --zui-scrollbar-thumb-hover --zui-tooltip-bg
   --zui-tooltip-text`
   plus `--zui-shadow-pop`, `--zui-control-h`, and `color-scheme`.

3. Optionally override geometry/motion tokens. Never override the spacing scale
   unless the whole theme is a different density.
4. Add the `<link>` and you're done — no JS, no component edits.
