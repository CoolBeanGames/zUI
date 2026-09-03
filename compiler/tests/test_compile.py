"""Dependency-free tests for zslc. Run: py compiler/tests/test_compile.py"""
import os
import sys

HERE = os.path.dirname(__file__)
sys.path.insert(0, os.path.join(HERE, ".."))

import zslc  # noqa: E402

EXAMPLES = os.path.normpath(os.path.join(HERE, "..", "..", "examples"))


def _check(cond, msg):
    if not cond:
        raise AssertionError(msg)


def test_lex_basic():
    toks = zslc.lex('window "x" { button "ok" -> a.b }')
    kinds = [t.kind for t in toks]
    _check("arrow" in kinds, "arrow token missing")
    _check(kinds[-1] == "eof", "no eof")


def test_parse_counter():
    src = open(os.path.join(EXAMPLES, "counter.zsl"), encoding="utf-8").read()
    prog = zslc.compile_source(src)
    _check(len(prog.roots) == 1 and prog.roots[0].name == "window", "root not window")
    _check(prog.state["count"] == 0, "state.count wrong")
    _check("inc" in prog.handlers and "dec" in prog.handlers, "handlers missing")


def test_html_backend():
    src = open(os.path.join(EXAMPLES, "counter.zsl"), encoding="utf-8").read()
    doc = zslc.HtmlGen(zslc.compile_source(src)).gen()
    _check("<!DOCTYPE html>" in doc, "no doctype")
    _check("zui-window" in doc and "zui-btn" in doc, "component classes missing")
    _check("zui.receive('state'" in doc, "state glue missing")
    _check("plus1" not in doc or "+1)" in doc, "plus1 helper not lowered")


def test_showcase_all_backends():
    src = open(os.path.join(EXAMPLES, "showcase.zsl"), encoding="utf-8").read()
    prog = zslc.compile_source(src)
    html_out = zslc.HtmlGen(prog).gen()
    _check("zui-menubar" in html_out and "zui-table" in html_out, "showcase html incomplete")
    cs = zslc.gen_csharp(prog, "CompiledUi", "ZUI.Generated")
    _check("public partial class CompiledUi" in cs and "host.On(" in cs, "csharp backend broken")
    cpp = zslc.gen_cpp(prog, "build_ui")
    _check("zui::Host" in cpp and "R\"ZSL(" in cpp, "cpp backend broken")


def test_parse_error_reported():
    try:
        zslc.compile_source("window { button ->")
    except zslc.ParseError:
        return
    raise AssertionError("expected ParseError")


def main():
    tests = [v for k, v in sorted(globals().items()) if k.startswith("test_")]
    failed = 0
    for t in tests:
        try:
            t()
            print(f"ok   {t.__name__}")
        except AssertionError as e:
            failed += 1
            print(f"FAIL {t.__name__}: {e}")
    print(f"\n{len(tests) - failed}/{len(tests)} passed")
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
