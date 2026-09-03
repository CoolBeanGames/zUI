# zUI icon set

Small, monochrome, line-based icons of consistent visual weight (design.txt
ICONOGRAPHY). No colour, no emoji as UI icons, one style throughout.

- **Grid:** 16×16, 1.5 stroke, round caps/joins, `currentColor`.
- `zui-icons.svg` — the `<symbol>` sprite.
- `sprite.js` — injects the sprite into the page so `<use href="#name">` works
  from **any** origin (including `file://` for the local showcase).

## Use

```html
<!-- host app served same-origin: reference the file -->
<svg class="zui-icon"><use href="zui/icons/zui-icons.svg#play"></use></svg>

<!-- or inject once (works everywhere) -->
<script src="zui/icons/sprite.js"></script>
<svg class="zui-icon"><use href="#play"></use></svg>
```

`.zui-icon` is 14px and follows text colour; `--sm` (12), `--lg` (18),
`--muted`. In a `.zui-nav__item` use `.zui-nav__icon`.

## Names

`play pause stop prev next shuffle repeat search music disc list grid folder
check close plus minus chevron-right chevron-down eject sync gear warning info
trash edit device download podcast`

Add an icon: append a `<symbol id="…" viewBox="0 0 16 16">` to `zui-icons.svg`
matching the stroke conventions, then regenerate `sprite.js`
(`py - <<'…'` snippet in the ZU-26 commit, or just re-embed the file text).
