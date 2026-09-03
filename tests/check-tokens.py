#!/usr/bin/env python3
"""Guards the theming contract:
  1. no component/base CSS hard-codes a colour (must go through a token),
  2. every var(--zui-*) a component uses is defined in tokens.css,
  3. holo.css and clean.css define the same token set (minus the ones :root
     provides that a theme may inherit).
Run: py tests/check-tokens.py   (also run by build.ps1 -Config test)
"""
import os
import re
import sys

ROOT = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", "core", "css"))

TOKEN_DEF = re.compile(r"(--zui-[a-z0-9-]+)\s*:")
TOKEN_USE = re.compile(r"var\((--zui-[a-z0-9-]+)")
# a colour literal that is not inside a var() fallback or a comment
COLOR_LIT = re.compile(r"(?<!\w)(#[0-9a-fA-F]{3,8}\b|rgba?\([^)]*\)|hsla?\([^)]*\))")
COLOR_PROPS = ("color", "background", "background-color", "border-color",
               "box-shadow", "fill", "stroke", "outline-color", "border")


def read(p):
    with open(p, encoding="utf-8") as fh:
        return fh.read()


def strip_comments(s):
    return re.sub(r"/\*.*?\*/", "", s, flags=re.DOTALL)


def main():
    errors = []
    defined = set(TOKEN_DEF.findall(strip_comments(read(os.path.join(ROOT, "tokens.css")))))

    comp_files = []
    for base in ("base.css", "fonts.css"):
        comp_files.append(os.path.join(ROOT, base))
    cdir = os.path.join(ROOT, "components")
    comp_files += [os.path.join(cdir, f) for f in sorted(os.listdir(cdir)) if f.endswith(".css")]

    for path in comp_files:
        src = strip_comments(read(path))
        rel = os.path.relpath(path, ROOT)
        local = set(TOKEN_DEF.findall(src))   # component-private vars (e.g. --zui-slider-fill)
        for tok in set(TOKEN_USE.findall(src)):
            if tok not in defined and tok not in local:
                errors.append(f"{rel}: uses undefined token {tok}")
        # colour literals on colour-ish declarations
        for m in re.finditer(r"([a-z-]+)\s*:\s*([^;{}]+)", src):
            prop, val = m.group(1).strip(), m.group(2)
            if prop in COLOR_PROPS and COLOR_LIT.search(val):
                # allow transparent / currentColor / none
                errors.append(f"{rel}: colour literal in `{prop}: {val.strip()[:60]}`")

    theme_tokens = {}
    for th in ("holo.css", "clean.css"):
        theme_tokens[th] = set(TOKEN_DEF.findall(strip_comments(read(os.path.join(ROOT, "themes", th)))))
    only_holo = theme_tokens["holo.css"] - theme_tokens["clean.css"]
    only_clean = theme_tokens["clean.css"] - theme_tokens["holo.css"]
    for t in sorted(only_holo):
        errors.append(f"themes: {t} defined in holo.css but not clean.css")
    for t in sorted(only_clean):
        errors.append(f"themes: {t} defined in clean.css but not holo.css")

    if errors:
        print("TOKEN CHECK FAILED:")
        for e in errors:
            print("  -", e)
        return 1
    print(f"token check OK  ({len(defined)} tokens, {len(comp_files)} files)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
