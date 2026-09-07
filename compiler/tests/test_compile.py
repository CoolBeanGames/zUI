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


def test_csharp_native_backend():
    src = open(os.path.join(EXAMPLES, "counter.zsl"), encoding="utf-8").read()
    code = zslc.gen_csharp(zslc.compile_source(src), "CounterUi", "ZUI.Generated")
    _check("System.Windows.Forms.Control Build" in code, "native Build method missing")
    _check('new ZUI.ZuiNode("button"' in code, "button node missing")
    _check("<!DOCTYPE" not in code and "<script" not in code, "document markup leaked into native output")


def test_only_native_generators_exist():
    _check(not hasattr(zslc, "HtmlGen"), "legacy document generator must not exist")
    _check(hasattr(zslc, "gen_csharp") and hasattr(zslc, "gen_cpp"), "native generators missing")


def test_table_source_is_native_metadata():
    prog = zslc.compile_source(
        '<table id="rows" source="rows"><column field="name">Name</column></table>'
        '<state><var name="rows" value="[]"/></state>')
    code = zslc.gen_csharp(prog, "TableUi", "ZUI.Generated")
    _check('["source"] = "rows"' in code and 'new ZUI.ZuiNode("column"' in code,
           "table source and columns must be native node metadata")


def test_showcase_all_backends():
    src = open(os.path.join(EXAMPLES, "showcase.zsl"), encoding="utf-8").read()
    prog = zslc.compile_source(src)
    cs = zslc.gen_csharp(prog, "CompiledUi", "ZUI.Generated")
    _check("public partial class CompiledUi" in cs and "host.On(" in cs and "host.Build(" in cs, "csharp backend broken")
    cpp = zslc.gen_cpp(prog, "build_ui")
    _check("zui::Host" in cpp and "zui::Node" in cpp and "R\"ZSL(" not in cpp, "cpp backend broken")
    _check("handlers.find" in cpp and "implement in host" not in cpp,
           "cpp backend must link without undefined callback stubs")


def test_parse_error_reported():
    try:
        zslc.compile_source("window { button ->")
    except zslc.ParseError:
        return
    raise AssertionError("expected ParseError")


def test_zml_detection_and_parse():
    src = '<window title="X"><button on="a.b">Go</button></window>'
    _check(zslc._looks_like_zml(src), "ZML not detected")
    _check(not zslc._looks_like_zml('window { }'), "brace misdetected as ZML")
    prog = zslc.compile_source(src)
    _check(prog.roots[0].name == "window" and prog.roots[0].text == "X", "zml window/title")
    btn = prog.roots[0].children[0]
    _check(btn.name == "button" and btn.event == "a.b" and btn.text == "Go", "zml button")


def test_zml_equals_zsl():
    for stem in ("counter", "showcase"):
        zsl = open(os.path.join(EXAMPLES, stem + ".zsl"), encoding="utf-8").read()
        zml = open(os.path.join(EXAMPLES, stem + ".zml"), encoding="utf-8").read()
        for backend in ("csharp", "cpp"):
            a = _render(zslc.compile_source(zsl), backend)
            b = _render(zslc.compile_source(zml), backend)
            _check(a == b, f"{stem}.{backend}: zml output != zsl output")


def _render(prog, backend):
    if backend == "csharp":
        return zslc.gen_csharp(prog, "CompiledUi", "ZUI.Generated")
    return zslc.gen_cpp(prog, "build_ui")


def test_export_emits_native_lookup_name():
    for src in ('panel { input "x" export=q  button "Go" export=go }',
                '<panel><input export="q"/><button export="go">Go</button></panel>'):
        h = zslc.gen_csharp(zslc.compile_source(src), "Ui", "Generated")
        _check('["export"] = "q"' in h, f"input export -> lookup metadata ({src[:1]})")
        _check('["export"] = "go"' in h, f"button export -> lookup metadata ({src[:1]})")


def test_bind_also_exports():
    h = zslc.gen_csharp(zslc.compile_source('col { text bind:status }'), "Ui", "Generated")
    _check('["bind"] = "status"' in h, "bind is native lookup metadata")


def test_zml_comments_and_selfclose():
    prog = zslc.compile_source('<!-- hi --><col><spinner/><text bind="x"/></col>')
    col = prog.roots[0]
    _check(col.name == "col" and len(col.children) == 2, "self-close children")
    _check(col.children[1].bind == "x", "zml bind attr")


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
