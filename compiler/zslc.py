#!/usr/bin/env python3
"""zslc - the ZSL (zUI Scripting Language) compiler.

Ahead-of-time compiles a .zsl UI description to a zUI document and/or native
host code. See GRAMMAR.md.

    zslc.py input.zsl --backend html   [-o out.html]
    zslc.py input.zsl --backend csharp [-o Out.g.cs]   [--class Name] [--namespace N]
    zslc.py input.zsl --backend cpp    [-o out.g.cpp]   [--func build_ui]

Exit code 0 on success, 1 on a compile error.
"""
from __future__ import annotations

import argparse
import html
import json
import re
import sys
from dataclasses import dataclass, field
from typing import Any


# --------------------------------------------------------------------------- #
# Lexer
# --------------------------------------------------------------------------- #

TOKEN_RE = re.compile(r"""
    (?P<ws>\s+)
  | (?P<lc>//[^\n]*)
  | (?P<bc>/\*.*?\*/)
  | (?P<str>"(?:[^"\\]|\\.)*")
  | (?P<num>\d+(?:\.\d+)?)
  | (?P<arrow>->)
  | (?P<punct>[{}=:.,;()\[\]])
  | (?P<ident>[A-Za-z_][A-Za-z0-9_-]*)
""", re.VERBOSE | re.DOTALL)


@dataclass
class Tok:
    kind: str
    value: str
    line: int


class LexError(Exception):
    pass


def lex(src: str) -> list[Tok]:
    toks: list[Tok] = []
    i, line = 0, 1
    while i < len(src):
        m = TOKEN_RE.match(src, i)
        if not m:
            raise LexError(f"line {line}: unexpected character {src[i]!r}")
        kind = m.lastgroup
        text = m.group()
        line += text.count("\n")
        i = m.end()
        if kind in ("ws", "lc", "bc"):
            continue
        if kind == "str":
            toks.append(Tok("str", bytes(text[1:-1], "utf-8").decode("unicode_escape"), line))
        elif kind == "num":
            toks.append(Tok("num", text, line))
        elif kind == "arrow":
            toks.append(Tok("arrow", text, line))
        elif kind == "punct":
            toks.append(Tok(text, text, line))
        else:  # ident
            toks.append(Tok("ident", text, line))
    toks.append(Tok("eof", "", line))
    return toks


# --------------------------------------------------------------------------- #
# AST
# --------------------------------------------------------------------------- #

@dataclass
class Node:
    name: str
    text: str | None = None
    attrs: dict[str, Any] = field(default_factory=dict)
    flags: set[str] = field(default_factory=set)
    bind: str | None = None
    source: str | None = None
    event: str | None = None
    children: list["Node"] = field(default_factory=list)
    line: int = 0


@dataclass
class Stmt:
    op: str            # emit | call | assign
    target: str        # channel name or state field
    expr: Any = None   # literal or dotted string


@dataclass
class Program:
    roots: list[Node] = field(default_factory=list)
    state: dict[str, Any] = field(default_factory=dict)
    handlers: dict[str, list[Stmt]] = field(default_factory=dict)


class ParseError(Exception):
    pass


# --------------------------------------------------------------------------- #
# Parser
# --------------------------------------------------------------------------- #

class Parser:
    def __init__(self, toks: list[Tok]):
        self.toks = toks
        self.p = 0

    def peek(self) -> Tok:
        return self.toks[self.p]

    def next(self) -> Tok:
        t = self.toks[self.p]
        self.p += 1
        return t

    def expect(self, kind: str) -> Tok:
        t = self.next()
        if t.kind != kind:
            raise ParseError(f"line {t.line}: expected {kind!r}, got {t.kind!r} ({t.value!r})")
        return t

    def parse(self) -> Program:
        prog = Program()
        while self.peek().kind != "eof":
            t = self.peek()
            if t.kind == ";":
                self.next()
                continue
            if t.kind == "ident" and t.value == "state":
                self._state(prog)
            elif t.kind == "ident" and t.value == "on":
                self._handler(prog)
            elif t.kind == "ident":
                prog.roots.append(self._node())
            else:
                raise ParseError(f"line {t.line}: unexpected {t.value!r} at top level")
        return prog

    def _literal(self) -> Any:
        t = self.next()
        if t.kind == "str":
            return t.value
        if t.kind == "num":
            return float(t.value) if "." in t.value else int(t.value)
        if t.kind == "ident" and t.value in ("true", "false"):
            return t.value == "true"
        if t.kind == "[":
            self.expect("]")
            return []
        if t.kind == "{":
            self.expect("}")
            return {}
        if t.kind == "ident":
            return {"$ref": self._dotted_from(t.value)}
        raise ParseError(f"line {t.line}: expected a literal, got {t.value!r}")

    def _dotted_from(self, first: str) -> str:
        parts = [first]
        while self.peek().kind == ".":
            self.next()
            parts.append(self.expect("ident").value)
        return ".".join(parts)

    def _state(self, prog: Program) -> None:
        self.next()  # 'state'
        self.expect("{")
        while self.peek().kind != "}":
            name = self.expect("ident").value
            self.expect("=")
            prog.state[name] = self._literal()
            if self.peek().kind == ";":
                self.next()
        self.expect("}")

    def _handler(self, prog: Program) -> None:
        self.next()  # 'on'
        name = self._dotted_from(self.expect("ident").value)
        self.expect("{")
        stmts: list[Stmt] = []
        while self.peek().kind != "}":
            stmts.append(self._stmt())
        self.expect("}")
        prog.handlers.setdefault(name, []).extend(stmts)

    def _stmt(self) -> Stmt:
        t = self.next()
        if t.kind == "ident" and t.value in ("emit", "call"):
            self.expect("(")
            chan = self.expect("str").value
            expr = None
            if self.peek().kind == ",":
                self.next()
                expr = self._literal()
            self.expect(")")
            if self.peek().kind == ";":
                self.next()
            return Stmt(t.value, chan, expr)
        if t.kind == "ident":
            self.expect("=")
            expr = self._literal()
            if self.peek().kind == ";":
                self.next()
            return Stmt("assign", t.value, expr)
        raise ParseError(f"line {t.line}: bad statement starting at {t.value!r}")

    def _node(self) -> Node:
        name_t = self.expect("ident")
        node = Node(name=name_t.value, line=name_t.line)
        hdr_line = name_t.line
        if self.peek().kind == "str" and self.peek().line == hdr_line:
            node.text = self.next().value
        # Attributes must sit on the same line as the node keyword; a token on a
        # later line begins the next sibling node (newlines are otherwise
        # insignificant, so this is the one place they matter).
        while True:
            t = self.peek()
            if t.line != hdr_line or t.kind in ("}", "eof", ";"):
                break
            if t.kind == "arrow":
                self.next()
                node.event = self._dotted_from(self.expect("ident").value)
                continue
            if t.kind == "ident" and t.value == "bind" and self.toks[self.p + 1].kind == ":":
                self.next(); self.next()
                node.bind = self.expect("ident").value
                continue
            if t.kind == "ident" and self.toks[self.p + 1].kind == "=":
                key = self.next().value
                self.next()  # '='
                val = self._literal()
                if isinstance(val, dict) and "$ref" in val:
                    # bare ident: a ref for `source`, a plain string elsewhere
                    if key == "source":
                        node.source = val["$ref"]
                        continue
                    val = val["$ref"]
                node.attrs[key] = val
                continue
            if t.kind == "ident" and self.toks[self.p + 1].kind not in ("=", ":", "str"):
                # bare flag, but not another node start on same line context:
                # treat lowercase single idents before '{' or next attr as flags
                node.flags.add(self.next().value)
                continue
            break
        if self.peek().kind == "{":
            self.next()
            while self.peek().kind != "}":
                if self.peek().kind == ";":
                    self.next()
                    continue
                node.children.append(self._node())
            self.expect("}")
        return node


# --------------------------------------------------------------------------- #
# ZML - the XML-style front end (same AST as the brace parser above)
# --------------------------------------------------------------------------- #

_IDENT_RE = re.compile(r"[A-Za-z_][\w.-]*\Z")
_NUM_RE = re.compile(r"-?\d+(?:\.\d+)?\Z")


def _looks_like_zml(src: str) -> bool:
    """A source is ZML if its first significant character is '<'."""
    s = re.sub(r"<!--.*?-->", "", src, flags=re.DOTALL)
    s = re.sub(r"(?m)^[ \t]*//[^\n]*$", "", s)
    return s.lstrip().startswith("<")


def _coerce_literal(v):
    if v is None:
        return True
    if v in ("true", "false"):
        return v == "true"
    if v == "[]":
        return []
    if v == "{}":
        return {}
    if _NUM_RE.match(v):
        return float(v) if "." in v else int(v)
    return v


def _coerce_expr(v):
    """Attribute value used as an expression (<emit value=...>, <set value=...>)."""
    if v is None:
        return None
    if v in ("true", "false"):
        return v == "true"
    if _NUM_RE.match(v):
        return float(v) if "." in v else int(v)
    if _IDENT_RE.match(v):
        return {"$ref": v}
    return v


class ZmlParser:
    """A small, permissive XML-ish parser. Not full XML: no namespaces, PIs,
    DOCTYPE, CDATA or entities beyond the common five."""

    _ENT = {"&amp;": "&", "&lt;": "<", "&gt;": ">", "&quot;": '"', "&apos;": "'"}

    def __init__(self, src: str):
        self.s = re.sub(r"<!--.*?-->", "", src, flags=re.DOTALL)
        self.i = 0
        self.n = len(self.s)
        self.line = 1

    # -- low-level ------------------------------------------------------- #
    def _err(self, msg):
        raise ParseError(f"line {self.line}: {msg}")

    def _ws(self):
        while self.i < self.n:
            c = self.s[self.i]
            if c == "\n":
                self.line += 1
                self.i += 1
            elif c in " \t\r":
                self.i += 1
            elif self.s.startswith("//", self.i):
                nl = self.s.find("\n", self.i)
                self.i = self.n if nl == -1 else nl
            else:
                break

    def _name(self):
        m = re.compile(r"[A-Za-z_][\w:.-]*").match(self.s, self.i)
        if not m:
            self._err("expected a name")
        self.i = m.end()
        return m.group()

    def _string(self):
        q = self.s[self.i]
        self.i += 1
        start = self.i
        while self.i < self.n and self.s[self.i] != q:
            if self.s[self.i] == "\n":
                self.line += 1
            self.i += 1
        val = self.s[start:self.i]
        self.i += 1  # closing quote
        for k, v in self._ENT.items():
            val = val.replace(k, v)
        return val

    def _unescape(self, text):
        for k, v in self._ENT.items():
            text = text.replace(k, v)
        return text

    # -- structure ----------------------------------------------------- #
    def parse(self) -> Program:
        prog = Program()
        self._ws()
        while self.i < self.n:
            if not self.s.startswith("<", self.i):
                self._err("expected an element")
            self._route(self._element(), prog)
            self._ws()
        return prog

    def _element(self) -> Node:
        assert self.s[self.i] == "<"
        self.i += 1
        self._ws()
        node = Node(name=self._name(), line=self.line)

        # attributes
        while True:
            self._ws()
            if self.s.startswith("/>", self.i):
                self.i += 2
                return node
            if self.s.startswith(">", self.i):
                self.i += 1
                break
            key = self._name()
            self._ws()
            if self.s.startswith("=", self.i):
                self.i += 1
                self._ws()
                val = self._string()
            else:
                val = None
            self._apply_attr(node, key, val)

        # content
        text_parts = []
        while True:
            if self.i >= self.n:
                self._err(f"unclosed <{node.name}>")
            if self.s.startswith("</", self.i):
                self.i += 2
                self._ws()
                self._name()          # closing name (not verified strictly)
                self._ws()
                if self.s.startswith(">", self.i):
                    self.i += 1
                break
            if self.s.startswith("<", self.i):
                node.children.append(self._element())
                continue
            nxt = self.s.find("<", self.i)
            chunk = self.s[self.i:(self.n if nxt == -1 else nxt)]
            self.line += chunk.count("\n")
            text_parts.append(chunk)
            self.i = self.n if nxt == -1 else nxt

        text = self._unescape("".join(text_parts)).strip()
        if text and node.text is None:
            node.text = text
        return node

    def _apply_attr(self, node: Node, key: str, val):
        if val is None or val == "true":
            node.flags.add(key)
            return
        if val == "false":
            return
        if key == "bind":
            node.bind = val
        elif key == "source":
            node.source = val
        elif key == "on":
            node.event = val
        elif key in ("title", "label") and node.text is None:
            node.text = val
        else:
            node.attrs[key] = val

    def _route(self, node: Node, prog: Program) -> None:
        if node.name == "state":
            for c in node.children:
                if c.name in ("var", "field"):
                    prog.state[c.attrs.get("name", c.text or "")] = _coerce_literal(c.attrs.get("value"))
            return
        if node.name == "on":
            ev = node.event or node.attrs.get("event") or node.text or ""
            stmts = []
            for c in node.children:
                if c.name in ("emit", "call"):
                    stmts.append(Stmt(c.name, c.attrs.get("channel") or c.attrs.get("name") or c.text or "",
                                      _coerce_expr(c.attrs.get("value"))))
                elif c.name in ("set", "assign"):
                    stmts.append(Stmt("assign", c.attrs.get("field") or c.attrs.get("name") or "",
                                      _coerce_expr(c.attrs.get("value"))))
            prog.handlers.setdefault(ev, []).extend(stmts)
            return
        prog.roots.append(node)


# --------------------------------------------------------------------------- #
# HTML backend
# --------------------------------------------------------------------------- #

# node name -> (tag, class, wrap-child-class)
HTML_MAP = {
    "window":        ("div", "zui-window", None),
    "titlebar":      ("div", "zui-titlebar", None),
    "menubar":       ("div", "zui-menubar", None),
    "nav":           ("div", "zui-nav", None),
    "workspace":     ("div", "zui-workspace", None),
    "sidebar":       ("nav", "zui-sidebar", None),
    "section-label": ("div", "zui-section-label", None),
    "panel":         ("div", "zui-panel", None),
    "panel-body":    ("div", "zui-panel__body", None),
    "row":           ("div", "zui-row zui-gap-2", None),
    "col":           ("div", "zui-col zui-gap-2", None),
    "fill":          ("div", "zui-fill", None),
    "grid":          ("div", "zui-grid", None),
    "statusbar":     ("div", "zui-statusbar", None),
    "contextbar":    ("div", "zui-contextbar", None),
    "spinner":       ("div", "zui-spinner", None),
    "tabs":          ("div", "zui-tabs", None),
    "tabpanel":      ("div", "zui-tabpanel", None),
    "empty":         ("div", "zui-empty", None),
    "drop":          ("div", "zui-drop", None),
}


class HtmlGen:
    def __init__(self, prog: Program, asset_base: str = "../core"):
        self.prog = prog
        self.asset_base = asset_base.rstrip("/")
        self.out: list[str] = []
        self.glue_events: dict[str, str] = {}   # dom-id -> handler name / channel
        self.binds: list[tuple[str, str, str]] = []  # (id, field, kind)
        self._id = 0

    def uid(self, prefix: str = "z") -> str:
        self._id += 1
        return f"{prefix}{self._id}"

    def esc(self, s: str) -> str:
        return html.escape(str(s), quote=True)

    def _zid(self, n: Node) -> str:
        """ data-zui-id="..." for a node that opts into the runtime IO layer via
        export="name" (or, implicitly, a bind:name binding)."""
        name = n.attrs.get("export") or n.bind
        return f' data-zui-id="{self.esc(name)}"' if name else ""

    def gen(self) -> str:
        body = "".join(self.render(n) for n in self.prog.roots)
        return DOC_TEMPLATE.format(
            base=self.asset_base,
            body=body,
            glue=self.glue(),
            state=json.dumps(self.prog.state, default=lambda o: None),
        )

    def render(self, n: Node) -> str:
        m = getattr(self, f"_n_{n.name.replace('-', '_')}", None)
        if m:
            return m(n)
        if n.name in HTML_MAP:
            tag, cls, _ = HTML_MAP[n.name]
            return self._wrap(n, tag, cls)
        # unknown -> div carrying its name as a data attribute
        return self._wrap(n, "div", f"zui-{self.esc(n.name)}")

    def _wrap(self, n: Node, tag: str, cls: str, extra: str = "", inner: str = "") -> str:
        attrs = f' class="{cls}"'
        if "id" in n.attrs:
            attrs += f' id="{self.esc(n.attrs["id"])}"'
        if n.event:
            eid = n.attrs.get("id") or self.uid()
            if "id" not in n.attrs:
                attrs += f' id="{eid}"'
            self.glue_events[eid] = n.event
        if "disabled" in n.flags:
            attrs += " disabled"
        attrs += self._zid(n)
        attrs += extra
        kids = inner or "".join(self.render(c) for c in n.children)
        text = self.esc(n.text) if n.text else ""
        return f"<{tag}{attrs}>{text}{kids}</{tag}>"

    # ---- specific nodes ------------------------------------------------- #

    def _n_window(self, n: Node) -> str:
        bar = ""
        if n.text:
            bar = (f'<div class="zui-titlebar"><span>{self.esc(n.text)}</span></div>')
        kids = "".join(self.render(c) for c in n.children)
        return f'<div class="zui-window">{bar}{kids}</div>'

    def _n_heading(self, n: Node) -> str:
        return f"<h1>{self.esc(n.text or '')}</h1>"

    def _n_statusbar(self, n: Node) -> str:
        # <statusbar device> renders the design.txt device-bar scaffold; actions
        # are <button ... action="eject">.
        dev = " data-zui-device" if "device" in n.flags else ""
        conn = " zui-statusbar--connected" if "connected" in n.flags else ""
        parts = ['<span class="zui-statusbar__dot"></span>']
        if n.text:
            parts.append(f'<span class="zui-statusbar__name">{self.esc(n.text)}</span>')
        acts = []
        for c in n.children:
            if c.name == "button":
                act = c.attrs.get("action", (c.text or "").lower())
                acts.append(f'<button class="zui-btn" data-device-action="{self.esc(act)}">{self.esc(c.text)}</button>')
            elif c.name == "text":
                parts.append(f'<span class="zui-statusbar__when-connected">{self.esc(c.text)}</span>')
        if acts:
            parts.append('<span class="zui-statusbar__actions zui-statusbar__when-connected">' + "".join(acts) + "</span>")
        elif not any('__actions' in p for p in parts):
            parts.append('<span class="zui-statusbar__spacer"></span>')
        return f'<div class="zui-statusbar{conn}"{dev}>{"".join(parts)}</div>'

    def _n_text(self, n: Node) -> str:
        if n.bind:
            i = n.attrs.get("id") or self.uid("t")
            self.binds.append((i, n.bind, "text"))
            return f'<span id="{i}"{self._zid(n)}></span>'
        return f"<span{self._zid(n)}>{self.esc(n.text or '')}</span>"

    def _n_button(self, n: Node) -> str:
        cls = "zui-btn"
        if n.attrs.get("kind") == "primary":
            cls += " zui-btn--primary"
        if "icon" in n.flags:
            cls += " zui-btn--icon"
        return self._wrap(n, "button", cls)

    def _n_input(self, n: Node) -> str:
        i = n.attrs.get("id") or self.uid("i")
        ph = f' placeholder="{self.esc(n.attrs["placeholder"])}"' if "placeholder" in n.attrs else ""
        if n.bind:
            self.binds.append((i, n.bind, "input"))
        return f'<input id="{i}" class="zui-input"{ph}{self._zid(n)}>'

    def _n_textarea(self, n: Node) -> str:
        i = n.attrs.get("id") or self.uid("i")
        if n.bind:
            self.binds.append((i, n.bind, "input"))
        return f'<textarea id="{i}" class="zui-textarea"{self._zid(n)}></textarea>'

    def _n_check(self, n: Node) -> str:
        i = n.attrs.get("id") or self.uid("c")
        if n.bind:
            self.binds.append((i, n.bind, "check"))
        return f'<label class="zui-check"><input type="checkbox" id="{i}"{self._zid(n)}> {self.esc(n.text or "")}</label>'

    def _n_field(self, n: Node) -> str:
        label = f"<label>{self.esc(n.text)}</label>" if n.text else ""
        kids = "".join(self.render(c) for c in n.children)
        return f'<div class="zui-field">{label}{kids}</div>'

    def _n_select(self, n: Node) -> str:
        i = n.attrs.get("id") or self.uid("s")
        opts = [c.text for c in n.children if c.name == "option"]
        if n.bind:
            self.binds.append((i, n.bind, "select"))
        return (f'<div class="zui-select" data-zui="select" id="{i}" data-name="{self.esc(n.bind or i)}"'
                f'{self._zid(n)} data-value="{self.esc(opts[0] if opts else "")}" '
                f"data-options='{json.dumps(opts)}'><span class=\"zui-select__value\">"
                f'{self.esc(opts[0] if opts else "")}</span></div>')

    def _n_slider(self, n: Node) -> str:
        i = n.attrs.get("id") or self.uid("sl")
        a = "".join(f' {k}="{self.esc(n.attrs[k])}"' for k in ("min", "max", "step", "value") if k in n.attrs)
        if n.bind:
            self.binds.append((i, n.bind, "input"))
        out = '<output></output>' if "labelled" in n.flags else ""
        cls = "zui-slider-row" if out else ""
        inner = f'<input type="range" id="{i}" class="zui-slider"{a}{self._zid(n)}>{out}'
        return f'<span class="{cls}">{inner}</span>' if out else inner

    def _n_progress(self, n: Node) -> str:
        i = n.attrs.get("id") or self.uid("p")
        if n.bind:
            self.binds.append((i, n.bind, "progress"))
        return (f'<div class="zui-progress"><div class="zui-progress__bar" id="{i}"'
                f'{self._zid(n)} style="width:0%"></div></div>')

    def _n_menubar(self, n: Node) -> str:
        items = []
        for menu in n.children:
            spec = []
            for it in menu.children:
                if it.name == "sep":
                    spec.append("-")
                else:
                    e = {"label": it.text}
                    if "shortcut" in it.attrs:
                        e["shortcut"] = it.attrs["shortcut"]
                    if it.event:
                        e["channel"] = it.event
                    spec.append(e)
            items.append(f'<div class="zui-menubar__item" data-menu=\'{json.dumps(spec)}\'>'
                         f'{self.esc(menu.text or "")}</div>')
        return f'<div class="zui-menubar" data-zui="menubar">{"".join(items)}</div>'

    def _n_nav(self, n: Node) -> str:
        its = []
        for it in n.children:
            v = it.attrs.get("value", it.text)
            active = " zui-active" if it.flags & {"active"} else ""
            its.append(f'<div class="zui-nav__item{active}" data-zui-tab="{self.esc(v)}">{self.esc(it.text)}</div>')
        return f'<div class="zui-nav" data-zui="tabs">{"".join(its)}</div>'

    def _n_tab(self, n: Node) -> str:
        v = n.attrs.get("value", n.text)
        active = " zui-active" if "active" in n.flags else ""
        return f'<div class="zui-tab{active}" data-zui-tab="{self.esc(v)}">{self.esc(n.text)}</div>'

    def _n_sidebar(self, n: Node) -> str:
        parts = []
        for c in n.children:
            if c.name == "section-label":
                parts.append(f'<div class="zui-section-label">{self.esc(c.text)}</div>')
            else:
                active = " zui-active" if "active" in c.flags else ""
                parts.append(f'<div class="zui-sidebar__item{active}">{self.esc(c.text)}</div>')
        return f'<nav class="zui-sidebar">{"".join(parts)}</nav>'

    def _n_tree(self, n: Node) -> str:
        out = []

        def walk(node, depth):
            for c in node.children:
                if c.name == "section":
                    out.append(f'<div class="zui-tree__section">{self.esc(c.text)}</div>')
                    continue
                gid = c.attrs.get("id") or (c.text or "").lower().replace(" ", "-")
                has_kids = any(k.name == "treeitem" for k in c.children)
                exp = ' aria-expanded="true"' if has_kids and "collapsed" not in c.flags else (
                    ' aria-expanded="false"' if has_kids else "")
                sel = " zui-selected" if "selected" in c.flags else ""
                rn = f' data-zui-rename="{self.esc(c.event)}"' if c.event else ""
                out.append(
                    f'<div class="zui-tree__item{sel}" data-id="{self.esc(gid)}" data-depth="{depth}"{exp}{rn}>'
                    f'<span class="zui-tree__label">{self.esc(c.text)}</span></div>')
                if has_kids:
                    walk(c, depth + 1)

        walk(n, 0)
        name = n.attrs.get("export") or n.bind or n.attrs.get("id") or ""
        nm = f' data-name="{self.esc(name)}"' if name else ""
        return f'<div class="zui-tree" data-zui="tree"{nm}{self._zid(n)}>{"".join(out)}</div>'

    def _n_table(self, n: Node) -> str:
        cols = [c for c in n.children if c.name == "column"]
        sortable = "sortable" in n.flags or "nosort" not in n.flags
        head = "".join(
            f'<th{(" data-field=" + chr(34) + self.esc(c.attrs.get("field", c.text or "")) + chr(34)) if sortable else ""}'
            f'{" class=" + chr(34) + "zui-num" + chr(34) if "num" in c.flags else ""}>{self.esc(c.text)}</th>'
            for c in cols)
        sel = ' data-zui="selectable"' if "selectable" in n.flags else ""
        i = n.attrs.get("id") or self.uid("tbl")
        if n.source:
            self.binds.append((i, n.source, "table:" + json.dumps([c.attrs.get("field", "") for c in cols])))
        return (f'<table class="zui-table" id="{i}"{sel} data-zui-table{self._zid(n)}>'
                f'<thead><tr>{head}</tr></thead><tbody></tbody></table>')

    def _n_panel(self, n: Node) -> str:
        header = f'<div class="zui-panel__header">{self.esc(n.text)}</div>' if n.text else ""
        kids = "".join(self.render(c) for c in n.children)
        return f'<div class="zui-panel">{header}<div class="zui-panel__body zui-panel__body--flush">{kids}</div></div>'

    # ---- generated glue JS ------------------------------------------------ #

    def glue(self) -> str:
        lines = ["(function(){", "  var state = window.__zslState;"]
        # events
        for eid, ev in self.glue_events.items():
            stmts = self.prog.handlers.get(ev)
            lines.append(f"  var _el_{eid} = document.getElementById({json.dumps(eid)});")
            lines.append(f"  if (_el_{eid}) _el_{eid}.addEventListener('click', function(){{")
            if stmts:
                for s in stmts:
                    lines.append("    " + self._emit_stmt(s))
            else:
                lines.append(f"    zui.send({json.dumps(ev)});")
            lines.append("    render();")
            lines.append("  });")
        # binds: change -> state + send
        for i, fld, kind in self.binds:
            if kind in ("input", "select", "check"):
                prop = "checked" if kind == "check" else "value"
                sel = f"document.getElementById({json.dumps(i)})"
                ev = "change"
                lines.append(f"  {{ var _b = {sel}; if (_b) _b.addEventListener({json.dumps(ev)}, function(){{")
                lines.append(f"    state[{json.dumps(fld)}] = _b.{prop};")
                lines.append(f"    zui.send('state', state);")
                lines.append("  }); }")
        # render() applies state -> DOM
        lines.append("  function render(){")
        for i, fld, kind in self.binds:
            j = json.dumps(i); f = json.dumps(fld)
            if kind == "text":
                lines.append(f"    var e{i}=document.getElementById({j}); if(e{i}) e{i}.textContent = state[{f}];")
            elif kind in ("input", "select"):
                lines.append(f"    var e{i}=document.getElementById({j}); if(e{i}&&'value'in e{i}) e{i}.value = state[{f}];")
            elif kind == "check":
                lines.append(f"    var e{i}=document.getElementById({j}); if(e{i}) e{i}.checked = !!state[{f}];")
            elif kind == "progress":
                lines.append(f"    var e{i}=document.getElementById({j}); if(e{i}) e{i}.style.width = (state[{f}]||0)+'%';")
            elif kind.startswith("table:"):
                fields = json.loads(kind[6:])
                lines.append(f"    var e{i}=document.getElementById({j}); if(e{i}){{")
                lines.append(f"      var tb=e{i}.querySelector('tbody'); tb.innerHTML='';")
                lines.append(f"      (state[{f}]||[]).forEach(function(rowdata,ix){{")
                lines.append("        var tr=document.createElement('tr'); tr.setAttribute('data-zui-row', ix);")
                lines.append(f"        {json.dumps(fields)}.forEach(function(k){{")
                lines.append("          var td=document.createElement('td'); td.textContent = rowdata[k]; tr.appendChild(td); });")
                lines.append("        tb.appendChild(tr); }); }")
        lines.append("  }")
        lines.append("  zui.receive('state', function(p){ Object.assign(state, p||{}); render(); });")
        lines.append("  document.addEventListener('DOMContentLoaded', function(){ zui.wire(document); render(); });")
        lines.append("})();")
        return "\n".join(lines)

    def _emit_stmt(self, s: Stmt) -> str:
        if s.op in ("emit", "call"):
            payload = self._expr(s.expr)
            return f"zui.send({json.dumps(s.target)}{', ' + payload if payload else ''});"
        if s.op == "assign":
            return f"state[{json.dumps(s.target)}] = {self._expr(s.expr)};"
        return ""

    def _expr(self, e: Any) -> str:
        if e is None:
            return ""
        if isinstance(e, dict) and "$ref" in e:
            ref = e["$ref"]
            # tiny built-in helpers: x.plus1 / x.minus1
            if ref.endswith(".plus1"):
                base = ref[:-6]
                return f"((state[{json.dumps(base)}]||0)+1)"
            if ref.endswith(".minus1"):
                base = ref[:-7]
                return f"((state[{json.dumps(base)}]||0)-1)"
            return f"state[{json.dumps(ref)}]"
        return json.dumps(e)


DOC_TEMPLATE = """<!DOCTYPE html>
<html lang="en" data-zui-theme="holo">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>zUI</title>
<link rel="stylesheet" href="{base}/css/zui.css">
<link rel="stylesheet" href="{base}/css/themes/holo.css">
<link rel="stylesheet" href="{base}/css/themes/clean.css">
</head>
<body>
{body}
<script>window.__zslState = {state};</script>
<script src="{base}/icons/sprite.js"></script>
<script src="{base}/js/zui.js"></script>
<script>
{glue}
</script>
</body>
</html>
"""


# --------------------------------------------------------------------------- #
# C# backend  (emits native code that builds the HTML in a ZuiHost)
# --------------------------------------------------------------------------- #

def gen_csharp(prog: Program, class_name: str, namespace: str, asset_base: str = "zui") -> str:
    hg = HtmlGen(prog, asset_base=asset_base)   # resolves against the host's virtual root
    doc = hg.gen()
    lit = doc.replace('"', '""')
    handlers = sorted({e for e in hg.glue_events.values()})
    hooks = "\n".join(
        f'        partial void On_{h.replace(".", "_")}(System.Text.Json.JsonElement payload);' for h in handlers
    )
    wires = "\n".join(
        f'            host.On("{h}", p => On_{h.replace(".", "_")}(p));' for h in handlers
    )
    return f"""// <auto-generated> compiled from ZSL by zslc.py - do not edit. </auto-generated>
namespace {namespace}
{{
    public partial class {class_name}
    {{
        public const string Document = @"{lit}";

        /// <summary>Render the compiled UI in the host and wire generated hooks.
        /// Call after host.InitializeAsync().</summary>
        public void Attach(ZUI.ZuiHost host)
        {{
{wires}
            host.LoadDocument(Document);
        }}

{hooks}
    }}
}}
"""


# --------------------------------------------------------------------------- #
# C++ backend
# --------------------------------------------------------------------------- #

def gen_cpp(prog: Program, func: str, asset_base: str = "zui") -> str:
    hg = HtmlGen(prog, asset_base=asset_base)
    doc = hg.gen()
    handlers = sorted({e for e in hg.glue_events.values()})
    raw = 'R"ZSL(' + doc + ')ZSL"'
    hooks = "\n".join(
        f"void on_{h.replace('.', '_')}(const std::string& payload);  // implement in host" for h in handlers
    )
    wires = "\n".join(
        f'    host.on("{h}", [](const std::string& p){{ on_{h.replace(".", "_")}(p); }});' for h in handlers
    )
    return f"""// generated from ZSL by zslc.py - do not edit.
#include "zui.h"
#include <string>

namespace {{
const char* kZslDocument = {raw};
}}

{hooks}

// Call after constructing the host. Wires generated hooks, then renders.
void {func}(zui::Host& host) {{
{wires}
    host.load_document(kZslDocument);
}}
"""


# --------------------------------------------------------------------------- #
# CLI
# --------------------------------------------------------------------------- #

def compile_source(src: str) -> Program:
    """Parse ZML (angle-bracket) or brace ZSL - auto-detected - to one AST."""
    if _looks_like_zml(src):
        return ZmlParser(src).parse()
    return Parser(lex(src)).parse()


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(prog="zslc")
    ap.add_argument("input")
    ap.add_argument("--backend", choices=["html", "csharp", "cpp"], default="html")
    ap.add_argument("-o", "--output")
    ap.add_argument("--class", dest="cls", default="CompiledUi")
    ap.add_argument("--namespace", default="ZUI.Generated")
    ap.add_argument("--func", default="build_ui")
    ap.add_argument("--asset-base", dest="asset_base", default=None,
                    help="URL/path prefix for zui/ assets (html: default ../core; csharp/cpp: default zui)")
    args = ap.parse_args(argv)

    try:
        with open(args.input, "r", encoding="utf-8") as fh:
            src = fh.read()
        prog = compile_source(src)
    except (OSError, LexError, ParseError) as e:
        print(f"zslc: {e}", file=sys.stderr)
        return 1

    if args.backend == "html":
        out = HtmlGen(prog, args.asset_base or "../core").gen()
    elif args.backend == "csharp":
        out = gen_csharp(prog, args.cls, args.namespace, args.asset_base or "zui")
    else:
        out = gen_cpp(prog, args.func, args.asset_base or "zui")

    if args.output:
        with open(args.output, "w", encoding="utf-8") as fh:
            fh.write(out)
        print(f"zslc: wrote {args.output} ({args.backend})")
    else:
        sys.stdout.write(out)
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
