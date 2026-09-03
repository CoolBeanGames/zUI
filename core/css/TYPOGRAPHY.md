# zUI typography

zUI uses the **platform-native UI font** and ships no web fonts, so an embedded
zUI surface matches the host OS. Hierarchy is built with **weight and colour**,
not dramatic size changes (design.txt).

## Tokens (defined in `tokens.css`)

| token               | value                              | use                                      |
|---------------------|------------------------------------|------------------------------------------|
| `--zui-font`        | Segoe UI Variable / Segoe UI / …   | all UI text                              |
| `--zui-font-mono`   | Cascadia Mono / Consolas / …       | code, IDs, fixed-width values            |
| `--zui-fs-small`    | 11px                               | section labels, shortcuts, secondary meta |
| `--zui-fs-normal`   | 13px                               | default interface text                   |
| `--zui-fs-medium`   | 14px                               | emphasised rows, sub-headings            |
| `--zui-fs-heading`  | 21px                               | primary page headings (20–24px range)    |
| `--zui-fw-normal`   | 400                                | body, navigation, controls               |
| `--zui-fw-medium`   | 600                                | headings, active items, column headers   |
| `--zui-lh`          | 1.4                                | line-height everywhere                   |

A theme may override any of these in its `[data-zui-theme]` block.

## Rules

1. **Never** write a `font-size`, `font-family`, `font-weight` or `line-height`
   literal in component CSS — reference a token. (`fonts.css` applies the
   defaults; `base.css` styles `h1`–`h4`.)
2. Small category headings (`LIBRARY`, `PLAYLISTS`) use `.zui-section-label` or
   `.zui-t-caps`: small, uppercase, `--zui-text-secondary`.
3. Page headings use `--zui-fs-heading` + `--zui-fw-medium`. Do not go larger.
4. Tabular data uses `.zui-t-num` (`font-variant-numeric: tabular-nums`).

## Utilities (`fonts.css`)

`.zui-t-heading .zui-t-medium .zui-t-normal .zui-t-small .zui-t-strong
.zui-t-mono .zui-t-num .zui-t-caps .zui-t-truncate`
