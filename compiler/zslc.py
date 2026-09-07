#!/usr/bin/env python3
"""Ahead-of-time ZSL/ZML compiler for native C# and C++ Windows controls."""
from __future__ import annotations

import argparse
import re
import sys
import xml.etree.ElementTree as ET
from dataclasses import dataclass, field
from typing import Any

TOKEN_RE = re.compile(r'''(?P<ws>\s+)|(?P<lc>//[^\n]*)|(?P<bc>/\*.*?\*/)|
    (?P<str>"(?:[^"\\]|\\.)*")|(?P<num>-?\d+(?:\.\d+)?)|(?P<arrow>->)|
    (?P<punct>[{}=:.,;()\[\]])|(?P<ident>[A-Za-z_][A-Za-z0-9_-]*)''', re.X | re.S)


@dataclass
class Tok:
    kind: str
    value: str
    line: int


class LexError(Exception): pass
class ParseError(Exception): pass


def lex(source: str) -> list[Tok]:
    result: list[Tok] = []
    offset, line = 0, 1
    while offset < len(source):
        match = TOKEN_RE.match(source, offset)
        if not match:
            raise LexError(f"line {line}: unexpected character {source[offset]!r}")
        kind, text = match.lastgroup, match.group()
        token_line = line
        line += text.count("\n")
        offset = match.end()
        if kind in ("ws", "lc", "bc"): continue
        if kind == "str":
            result.append(Tok("str", bytes(text[1:-1], "utf-8").decode("unicode_escape"), token_line))
        elif kind == "num": result.append(Tok("num", text, token_line))
        elif kind == "arrow": result.append(Tok("arrow", text, token_line))
        elif kind == "punct": result.append(Tok(text, text, token_line))
        else: result.append(Tok("ident", text, token_line))
    result.append(Tok("eof", "", line))
    return result


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
    op: str
    target: str
    expr: Any = None


@dataclass
class Program:
    roots: list[Node] = field(default_factory=list)
    state: dict[str, Any] = field(default_factory=dict)
    handlers: dict[str, list[Stmt]] = field(default_factory=dict)


class Parser:
    def __init__(self, tokens: list[Tok]): self.tokens, self.pos = tokens, 0
    def peek(self) -> Tok: return self.tokens[self.pos]
    def take(self) -> Tok:
        token = self.tokens[self.pos]; self.pos += 1; return token
    def expect(self, kind: str) -> Tok:
        token = self.take()
        if token.kind != kind:
            raise ParseError(f"line {token.line}: expected {kind!r}, got {token.value!r}")
        return token

    def parse(self) -> Program:
        program = Program()
        while self.peek().kind != "eof":
            if self.peek().kind == ";": self.take(); continue
            if self.peek().kind != "ident":
                raise ParseError(f"line {self.peek().line}: unexpected {self.peek().value!r}")
            if self.peek().value == "state": self.parse_state(program)
            elif self.peek().value == "on": self.parse_handler(program)
            else: program.roots.append(self.parse_node())
        return program

    def dotted(self, first: str) -> str:
        parts = [first]
        while self.peek().kind == ".":
            self.take(); parts.append(self.expect("ident").value)
        return ".".join(parts)

    def literal(self) -> Any:
        token = self.take()
        if token.kind == "str": return token.value
        if token.kind == "num": return float(token.value) if "." in token.value else int(token.value)
        if token.kind == "ident" and token.value in ("true", "false"): return token.value == "true"
        if token.kind == "[": self.expect("]"); return []
        if token.kind == "{": self.expect("}"); return {}
        if token.kind == "ident": return {"$ref": self.dotted(token.value)}
        raise ParseError(f"line {token.line}: expected value, got {token.value!r}")

    def parse_state(self, program: Program) -> None:
        self.take(); self.expect("{")
        while self.peek().kind != "}":
            name = self.expect("ident").value; self.expect("="); value = self.literal()
            program.state[name] = value
            if self.peek().kind == ";": self.take()
        self.expect("}")

    def parse_handler(self, program: Program) -> None:
        self.take(); name = self.dotted(self.expect("ident").value); self.expect("{")
        statements = []
        while self.peek().kind != "}": statements.append(self.parse_statement())
        self.expect("}"); program.handlers.setdefault(name, []).extend(statements)

    def parse_statement(self) -> Stmt:
        token = self.expect("ident")
        if token.value in ("emit", "call"):
            self.expect("("); target = self.expect("str").value
            expr = None
            if self.peek().kind == ",": self.take(); expr = self.literal()
            self.expect(")")
            if self.peek().kind == ";": self.take()
            return Stmt(token.value, target, expr)
        self.expect("="); expr = self.literal()
        if self.peek().kind == ";": self.take()
        return Stmt("assign", token.value, expr)

    def parse_node(self) -> Node:
        name = self.expect("ident")
        node = Node(name.value, line=name.line)
        if self.peek().kind == "str" and self.peek().line == name.line: node.text = self.take().value
        while self.peek().line == name.line and self.peek().kind not in ("}", "eof", ";", "{"):
            token = self.peek()
            if token.kind == "arrow":
                self.take(); node.event = self.dotted(self.expect("ident").value); continue
            if token.kind != "ident": break
            if self.tokens[self.pos + 1].kind == "str": break
            key = self.take().value
            if self.peek().kind == ":":
                self.take(); value = self.expect("ident").value
                if key == "bind": node.bind = value
                else: node.attrs[key] = value
            elif self.peek().kind == "=":
                self.take(); value = self.literal()
                if isinstance(value, dict) and "$ref" in value: value = value["$ref"]
                if key == "source": node.source = str(value)
                else: node.attrs[key] = value
            else: node.flags.add(key)
        if self.peek().kind == "{":
            self.take()
            while self.peek().kind != "}":
                if self.peek().kind == ";": self.take(); continue
                node.children.append(self.parse_node())
            self.expect("}")
        return node


_NUM = re.compile(r"-?\d+(?:\.\d+)?$")
_REF = re.compile(r"[A-Za-z_][\w.-]*$")


def _looks_like_zml(source: str) -> bool:
    source = re.sub(r"<!--.*?-->", "", source, flags=re.S)
    source = re.sub(r"(?m)^\s*//[^\n]*$", "", source)
    return source.lstrip().startswith("<")


def _value(value: str | None, expression: bool = False) -> Any:
    if value is None: return True
    if value in ("true", "false"): return value == "true"
    if value == "[]": return []
    if value == "{}": return {}
    if _NUM.fullmatch(value): return float(value) if "." in value else int(value)
    if expression and _REF.fullmatch(value): return {"$ref": value}
    return value


def _xml_node(element: ET.Element) -> Node:
    node = Node(element.tag)
    for key, value in element.attrib.items():
        if key == "bind": node.bind = value
        elif key == "source": node.source = value
        elif key == "on": node.event = value
        elif key in ("title", "label") and node.text is None: node.text = value
        elif value == "true": node.flags.add(key)
        elif value != "false": node.attrs[key] = value
    node.children = [_xml_node(child) for child in element]
    text = (element.text or "").strip()
    if text and node.text is None: node.text = text
    return node


def parse_zml(source: str) -> Program:
    source = re.sub(r"(?m)^\s*//[^\n]*$", "", source)
    try: root = ET.fromstring("<zui-root>" + source + "</zui-root>")
    except ET.ParseError as error: raise ParseError(str(error)) from error
    program = Program()
    for element in root:
        if element.tag == "state":
            for child in element:
                if child.tag in ("var", "field"):
                    program.state[child.attrib.get("name", "")] = _value(child.attrib.get("value"))
        elif element.tag == "on":
            event = element.attrib.get("event", "")
            statements = []
            for child in element:
                raw = child.attrib.get("value")
                expr = _value(raw, True) if raw is not None else None
                if child.tag in ("emit", "call"):
                    statements.append(Stmt(child.tag, child.attrib.get("channel", child.attrib.get("name", "")), expr))
                elif child.tag in ("set", "assign"):
                    statements.append(Stmt("assign", child.attrib.get("field", child.attrib.get("name", "")), expr))
            program.handlers.setdefault(event, []).extend(statements)
        else: program.roots.append(_xml_node(element))
    return program


def compile_source(source: str) -> Program:
    return parse_zml(source) if _looks_like_zml(source) else Parser(lex(source)).parse()


def _quote(value: Any) -> str:
    return '"' + str(value).replace('\\', '\\\\').replace('"', '\\"').replace('\r', '\\r').replace('\n', '\\n') + '"'


def _attrs(node: Node) -> dict[str, Any]:
    result = dict(node.attrs)
    if node.bind: result["bind"] = node.bind
    if node.source: result["source"] = node.source
    if node.event: result["on"] = node.event
    for flag in sorted(node.flags): result[flag] = "true"
    return result


def _events(program: Program) -> list[str]:
    result = set()
    def visit(node: Node):
        if node.event: result.add(node.event)
        for child in node.children: visit(child)
    for root in program.roots: visit(root)
    return sorted(result)


def _bindings(program: Program) -> list[tuple[str, str]]:
    """(state property, control lookup name) for every node that carries `bind`."""
    result: list[tuple[str, str]] = []
    def visit(node: Node):
        if node.bind:
            control = node.attrs.get("export") or node.attrs.get("id") or node.bind
            result.append((node.bind, str(control)))
        for child in node.children: visit(child)
    for root in program.roots: visit(root)
    return result


_MUTATORS = {"plus1", "minus1", "toggle", "not"}


def _state_literal(value: Any) -> str:
    if isinstance(value, bool): return "true" if value else "false"
    if isinstance(value, (int, float, str)): return str(value)
    if isinstance(value, list): return "[]"
    if isinstance(value, dict): return "{}"
    return ""


def _ref_of(expr: Any) -> str | None:
    if isinstance(expr, dict) and "$ref" in expr: return expr["$ref"]
    return None


# Per-backend method spellings: (send, get_string, mutate, assign, set).
_CS_CALLS = ("host.Send", "host.State.GetString", "host.State.Mutate", "host.State.Assign", "host.State.Set")
_CPP_CALLS = ("host.send", "host.state().get_string", "host.state().mutate", "host.state().assign", "host.state().set")


def _compile_statements(statements: list[Stmt], calls: tuple[str, str, str, str, str]) -> list[str]:
    send, get, mutate, assign, setv = calls
    lines: list[str] = []
    for stmt in statements:
        if stmt.op in ("emit", "call"):
            ref = _ref_of(stmt.expr)
            if ref is not None:
                lines.append(f'{send}({_quote(stmt.target)}, {get}({_quote(ref)}));')
            elif stmt.expr is None:
                lines.append(f'{send}({_quote(stmt.target)}, "");')
            else:
                lines.append(f'{send}({_quote(stmt.target)}, {_quote(_state_literal(stmt.expr))});')
        elif stmt.op == "assign":
            ref = _ref_of(stmt.expr)
            if ref is not None:
                parts = ref.split(".")
                if len(parts) == 2 and parts[1] in _MUTATORS:
                    lines.append(f'{mutate}({_quote(stmt.target)}, {_quote(parts[1])});')
                else:
                    lines.append(f'{assign}({_quote(stmt.target)}, {_quote(ref)});')
            else:
                lines.append(f'{setv}({_quote(stmt.target)}, {_quote(_state_literal(stmt.expr))});')
    return lines


def _cs_node(node: Node) -> str:
    attrs = ", ".join(f"[{_quote(k)}] = {_quote(v)}" for k, v in _attrs(node).items())
    children = ", ".join(_cs_node(child) for child in node.children)
    return (f"new ZUI.ZuiNode({_quote(node.name)}, {_quote(node.text or '')}, "
            f"new System.Collections.Generic.Dictionary<string,string> {{ {attrs} }}, "
            f"new ZUI.ZuiNode[] {{ {children} }})")


def gen_csharp(program: Program, class_name: str, namespace: str, asset_base: str = "") -> str:
    roots = ",\n                ".join(_cs_node(node) for node in program.roots)
    events = sorted(set(_events(program)) | set(program.handlers))
    hook = lambda e: "On_" + re.sub(r"\W", "_", e)
    state_init = "\n".join(
        f'            host.State.Init({_quote(name)}, {_quote(_state_literal(value))});'
        for name, value in program.state.items())
    binds = "\n".join(
        f'            host.Bind({_quote(prop)}, {_quote(control)});'
        for prop, control in _bindings(program))
    wires = []
    for event in events:
        body = _compile_statements(program.handlers.get(event, []), _CS_CALLS)
        body.append(f'{hook(event)}(p);')
        joined = "\n                ".join(body)
        wires.append(f'            host.On({_quote(event)}, p => {{\n                {joined}\n            }});')
    hooks = "\n".join(f'        partial void {hook(event)}(string payload);' for event in events)
    return f'''// <auto-generated> compiled from ZSL/ZML to native WinForms controls. </auto-generated>
namespace {namespace}
{{
    public partial class {class_name}
    {{
        public System.Windows.Forms.Control Build(ZUI.ZuiHost host)
        {{
            var __root = host.Build(new ZUI.ZuiNode("root", "", Children: new ZUI.ZuiNode[] {{
                {roots}
            }}));
{state_init}
{binds}
{chr(10).join(wires)}
            host.State.Flush();
            return __root;
        }}

{hooks}
    }}
}}
'''


def _cpp_node(node: Node) -> str:
    attrs = ", ".join("{" + _quote(k) + ", " + _quote(v) + "}" for k, v in _attrs(node).items())
    children = ", ".join(_cpp_node(child) for child in node.children)
    return f"zui::Node{{{_quote(node.name)}, {_quote(node.text or '')}, {{{attrs}}}, {{{children}}}}}"


def gen_cpp(program: Program, func: str, asset_base: str = "") -> str:
    roots = ",\n        ".join(_cpp_node(node) for node in program.roots)
    events = sorted(set(_events(program)) | set(program.handlers))
    state_init = "\n".join(
        f'    host.state().init({_quote(name)}, {_quote(_state_literal(value))});'
        for name, value in program.state.items())
    binds = "\n".join(
        f'    host.bind({_quote(prop)}, {_quote(control)});'
        for prop, control in _bindings(program))
    wires = []
    for event in events:
        stmts = _compile_statements(program.handlers.get(event, []), _CPP_CALLS)
        if stmts:
            body = "\n        ".join(stmts)
            wires.append(
                f'    host.on({_quote(event)}, [&host, &handlers](const std::string& p) {{\n'
                f'        {body}\n'
                f'        if (auto it = handlers.find({_quote(event)}); it != handlers.end()) it->second(p);\n'
                f'    }});')
        else:
            wires.append(f'    if (auto it = handlers.find({_quote(event)}); it != handlers.end()) host.on({_quote(event)}, it->second);')
    return f'''// generated from ZSL/ZML to native Win32 controls - do not edit.
#include "zui.h"
#include <string>
#include <unordered_map>

void {func}(zui::Host& host,
    const std::unordered_map<std::string, zui::MessageHandler>& handlers) {{
    host.build(zui::Node{{"root", "", {{}}, {{
        {roots}
    }}}});
{state_init}
{binds}
{chr(10).join(wires)}
    host.state().flush();
}}
'''


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(prog="zslc")
    parser.add_argument("input")
    parser.add_argument("--backend", choices=["csharp", "cpp"], default="csharp")
    parser.add_argument("-o", "--output")
    parser.add_argument("--class", dest="cls", default="CompiledUi")
    parser.add_argument("--namespace", default="ZUI.Generated")
    parser.add_argument("--func", default="build_ui")
    args = parser.parse_args(argv)
    try:
        with open(args.input, encoding="utf-8") as stream: program = compile_source(stream.read())
        output = gen_csharp(program, args.cls, args.namespace) if args.backend == "csharp" else gen_cpp(program, args.func)
        if args.output:
            with open(args.output, "w", encoding="utf-8") as stream: stream.write(output)
            print(f"zslc: wrote {args.output} ({args.backend})")
        else: sys.stdout.write(output)
        return 0
    except (OSError, LexError, ParseError) as error:
        print(f"zslc: {error}", file=sys.stderr); return 1


if __name__ == "__main__": raise SystemExit(main(sys.argv[1:]))
