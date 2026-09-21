#!/usr/bin/env python3
"""Lightweight C# declaration scanner used by repository documentation checks.

The project intentionally avoids a runtime Roslyn dependency.  This module uses
Pygments only in the validation toolchain and recognizes the declaration styles
used by this repository.  It is not intended to be a general C# parser.
"""
from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
import re
from typing import Iterable, Sequence

from pygments import lex
from pygments.lexers.dotnet import CSharpLexer
from pygments.token import Comment, Text, Token


@dataclass(frozen=True)
class Lexeme:
    """A significant C# token with source coordinates."""

    value: str
    offset: int
    line: int
    column: int
    token_type: object


@dataclass(frozen=True)
class ParameterSymbol:
    """A method parameter extracted from a declaration header."""

    name: str
    modifier: str | None


@dataclass(frozen=True)
class MethodSymbol:
    """A named method, constructor, operator, or local function declaration."""

    path: Path
    name: str
    start_line: int
    name_line: int
    end_line: int
    indent: str
    visibility: str | None
    return_type: str | None
    type_parameters: tuple[str, ...]
    parameters: tuple[ParameterSymbol, ...]
    is_local: bool
    is_constructor: bool


@dataclass(frozen=True)
class TypeSymbol:
    """A declared class, record, struct, interface, enum, or delegate."""

    path: Path
    name: str
    kind: str
    start_line: int
    name_line: int
    indent: str
    visibility: str | None


_CONTROL_NAMES = {
    "if", "for", "foreach", "while", "switch", "catch", "using", "lock",
    "checked", "unchecked", "fixed", "nameof", "typeof", "sizeof", "default",
    "new", "return", "throw", "when", "case", "base", "this",
}
_DISQUALIFYING_PREFIX = {
    "return", "throw", "new", "if", "for", "foreach", "while", "switch",
    "catch", "using", "lock", "case", "goto", "yield", "await",
}
_ACCESS = {"public", "private", "internal", "protected"}
_MODIFIERS = _ACCESS | {
    "static", "abstract", "virtual", "override", "sealed", "async", "unsafe",
    "extern", "new", "partial", "readonly", "required", "file",
}
_PARAMETER_MODIFIERS = {"ref", "out", "in", "params", "this", "scoped"}
_TYPE_KEYWORDS = {
    "void", "bool", "byte", "sbyte", "short", "ushort", "int", "uint", "long",
    "ulong", "nint", "nuint", "char", "float", "double", "decimal", "string",
    "object", "dynamic",
}
_BOUNDARY = {";", "{", "}"}
_NAME_TOKEN_PREFIXES = ("Token.Name", "Token.Keyword.Type")


def _significant_lexemes(text: str) -> list[Lexeme]:
    result: list[Lexeme] = []
    offset = 0
    line = 1
    column = 0
    for token_type, value in lex(text, CSharpLexer()):
        token_offset = offset
        token_line = line
        token_column = column
        offset += len(value)
        parts = value.split("\n")
        if len(parts) == 1:
            column += len(value)
        else:
            line += len(parts) - 1
            column = len(parts[-1])
        if token_type in Comment or token_type in Text or not value.strip():
            continue
        result.append(Lexeme(value, token_offset, token_line, token_column, token_type))
    return result


def _is_name(token: Lexeme) -> bool:
    return str(token.token_type).startswith(_NAME_TOKEN_PREFIXES) or bool(
        re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", token.value)
    )


def _is_identifier(token: Lexeme) -> bool:
    """Return whether a token can be a declared identifier rather than a keyword."""
    return str(token.token_type).startswith("Token.Name") and bool(
        re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", token.value)
    )


def _is_parameter_identifier(token: Lexeme) -> bool:
    """Return whether a token can be the name of a method parameter.

    Pygments classifies contextual identifiers such as ``value`` as
    :class:`Token.Keyword`, even though C# permits them as parameter names.
    Selecting the final identifier-like token after filtering type and modifier
    keywords keeps the scanner deterministic without requiring Roslyn.
    """
    if not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", token.value):
        return False
    if token.value in _PARAMETER_MODIFIERS or token.value in _TYPE_KEYWORDS:
        return False
    if token.value in _MODIFIERS or token.value in _CONTROL_NAMES:
        return False
    return str(token.token_type).startswith("Token.Name") or str(token.token_type) == "Token.Keyword"


def _matching(tokens: Sequence[Lexeme], start: int, opening: str, closing: str) -> int | None:
    depth = 0
    for index in range(start, len(tokens)):
        value = tokens[index].value
        if value == opening:
            depth += 1
        elif value == closing:
            depth -= 1
            if depth == 0:
                return index
    return None


def _method_name_index(tokens: Sequence[Lexeme], open_paren: int) -> tuple[int, int] | None:
    """Return (name index, header-name end index) before an opening parenthesis."""
    cursor = open_paren - 1
    if cursor < 0:
        return None
    if tokens[cursor].value == ">":
        depth = 0
        while cursor >= 0:
            value = tokens[cursor].value
            if value == ">":
                depth += 1
            elif value == "<":
                depth -= 1
                if depth == 0:
                    cursor -= 1
                    break
            cursor -= 1
    if cursor < 0 or not _is_identifier(tokens[cursor]):
        return None
    return cursor, open_paren - 1


def _next_declaration_token(tokens: Sequence[Lexeme], close_paren: int) -> str | None:
    cursor = close_paren + 1
    if cursor >= len(tokens):
        return None
    if tokens[cursor].value == ":":
        cursor += 1
        if cursor < len(tokens) and tokens[cursor].value in {"base", "this"}:
            cursor += 1
            if cursor < len(tokens) and tokens[cursor].value == "(":
                end = _matching(tokens, cursor, "(", ")")
                if end is None:
                    return None
                cursor = end + 1
    while cursor < len(tokens) and tokens[cursor].value == "where":
        cursor += 1
        while cursor < len(tokens) and tokens[cursor].value not in {"{", "=>", ";", "where"}:
            cursor += 1
    return tokens[cursor].value if cursor < len(tokens) else None


def _header_start(tokens: Sequence[Lexeme], name_index: int) -> int:
    cursor = name_index - 1
    while cursor >= 0 and tokens[cursor].value not in _BOUNDARY:
        cursor -= 1
    return cursor + 1


def _extract_parameters(tokens: Sequence[Lexeme], open_paren: int, close_paren: int) -> tuple[ParameterSymbol, ...]:
    groups: list[list[Lexeme]] = []
    current: list[Lexeme] = []
    paren = bracket = brace = angle = 0
    for token in tokens[open_paren + 1 : close_paren]:
        value = token.value
        if value == "(" : paren += 1
        elif value == ")": paren -= 1
        elif value == "[": bracket += 1
        elif value == "]": bracket -= 1
        elif value == "{": brace += 1
        elif value == "}": brace -= 1
        elif value == "<": angle += 1
        elif value == ">" and angle > 0: angle -= 1
        if value == "," and paren == bracket == brace == angle == 0:
            groups.append(current)
            current = []
        else:
            current.append(token)
    if current:
        groups.append(current)

    parameters: list[ParameterSymbol] = []
    for group in groups:
        before_default: list[Lexeme] = []
        nested = 0
        for token in group:
            if token.value in {"(", "[", "{", "<"}:
                nested += 1
            elif token.value in {")", "]", "}", ">"} and nested > 0:
                nested -= 1
            if token.value == "=" and nested == 0:
                break
            before_default.append(token)
        names = [token.value for token in before_default if _is_parameter_identifier(token)]
        if not names:
            continue
        name = names[-1]
        modifier = next((token.value for token in before_default if token.value in _PARAMETER_MODIFIERS), None)
        parameters.append(ParameterSymbol(name, modifier))
    return tuple(parameters)



def _extract_type_parameters(tokens: Sequence[Lexeme], name_index: int, open_paren: int) -> tuple[str, ...]:
    """Extract generic method parameter names between a method name and its parameter list."""
    if name_index + 1 >= open_paren or tokens[name_index + 1].value != "<":
        return ()
    end = _matching(tokens, name_index + 1, "<", ">")
    if end is None or end >= open_paren:
        return ()
    result: list[str] = []
    for token in tokens[name_index + 2 : end]:
        if token.value == ",":
            continue
        if re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", token.value):
            result.append(token.value)
    return tuple(result)

def _return_type(header: Sequence[Lexeme], name_index_relative: int) -> str | None:
    tokens = list(header[:name_index_relative])
    tokens = [token for token in tokens if token.value not in _MODIFIERS]
    if not tokens:
        return None
    # Attributes are outside the header start in the repository style.  Preserve
    # punctuation for generic/array/nullable return types while normalizing spaces.
    raw = "".join(token.value if token.value in {".", "<", ">", "[", "]", "?", ","} else f" {token.value} " for token in tokens)
    return re.sub(r"\s+", " ", raw).strip().replace(" .", ".").replace(". ", ".")


def scan_methods(path: Path) -> list[MethodSymbol]:
    """Scan the named method declarations used by the repository."""
    text = path.read_text(encoding="utf-8-sig")
    lines = text.splitlines()
    tokens = _significant_lexemes(text)
    result: list[MethodSymbol] = []
    seen_offsets: set[int] = set()
    for open_index, token in enumerate(tokens):
        if token.value != "(":
            continue
        close_index = _matching(tokens, open_index, "(", ")")
        if close_index is None:
            continue
        name_info = _method_name_index(tokens, open_index)
        if name_info is None:
            continue
        name_index, _ = name_info
        name = tokens[name_index].value
        if name in _CONTROL_NAMES:
            continue
        before_name = tokens[name_index - 1].value if name_index > 0 else None
        if before_name in {".", "?.", "new", "return", "throw", "nameof", "typeof", "="}:
            continue
        terminator = _next_declaration_token(tokens, close_index)
        if terminator not in {"{", "=>", ";"}:
            continue
        start_index = _header_start(tokens, name_index)
        header = tokens[start_index:name_index]
        if not header:
            continue
        header_values = [item.value for item in header]
        if any(value in {"class", "record", "struct", "interface", "enum", "delegate"} for value in header_values):
            continue
        if header_values[0] in _DISQUALIFYING_PREFIX:
            continue
        if any(value in {"=", "=>"} for value in header_values):
            continue
        has_access = any(value in _ACCESS for value in header_values)
        has_type = any(
            value in _TYPE_KEYWORDS or _is_name(item)
            for value, item in zip(header_values, header)
            if value not in _MODIFIERS
        )
        if terminator == ";" and not has_access:
            continue
        if not has_access and not has_type:
            continue
        if tokens[name_index].offset in seen_offsets:
            continue
        seen_offsets.add(tokens[name_index].offset)

        start_line = tokens[start_index].line
        # Include immediately preceding attributes so documentation is placed
        # before them and remains attached to the declaration.
        probe = start_line - 2
        while probe >= 0:
            stripped = lines[probe].strip()
            if stripped.startswith("[") or stripped.endswith("]"):
                start_line = probe + 1
                probe -= 1
                continue
            break
        indent_match = re.match(r"\s*", lines[start_line - 1] if lines else "")
        indent = indent_match.group(0) if indent_match else ""
        visibility = next((value for value in header_values if value in _ACCESS), None)
        return_type = _return_type(tokens[start_index : name_index + 1], name_index - start_index)
        is_constructor = return_type is None
        result.append(
            MethodSymbol(
                path=path,
                name=name,
                start_line=start_line,
                name_line=tokens[name_index].line,
                end_line=tokens[close_index].line,
                indent=indent,
                visibility=visibility,
                return_type=return_type,
                type_parameters=_extract_type_parameters(tokens, name_index, open_index),
                parameters=_extract_parameters(tokens, open_index, close_index),
                is_local=visibility is None,
                is_constructor=is_constructor,
            )
        )
    return sorted(result, key=lambda item: (item.start_line, item.name_line, item.name))


def scan_types(path: Path) -> list[TypeSymbol]:
    """Scan top-level and nested type declarations."""
    text = path.read_text(encoding="utf-8-sig")
    lines = text.splitlines()
    pattern = re.compile(
        r"^(?P<indent>\s*)(?P<prefix>(?:(?:public|private|internal|protected|static|sealed|abstract|partial|readonly|ref|file)\s+)*)"
        r"(?P<kind>class|record(?:\s+(?:class|struct))?|struct|interface|enum|delegate)\s+"
        r"(?P<name>[A-Za-z_][A-Za-z0-9_]*)"
    )
    result: list[TypeSymbol] = []
    for index, line in enumerate(lines, 1):
        match = pattern.match(line)
        if not match:
            continue
        prefix = match.group("prefix").split()
        visibility = next((item for item in prefix if item in _ACCESS), None)
        start_line = index
        probe = index - 2
        while probe >= 0:
            stripped = lines[probe].strip()
            if stripped.startswith("[") or stripped.endswith("]"):
                start_line = probe + 1
                probe -= 1
                continue
            break
        result.append(
            TypeSymbol(
                path=path,
                name=match.group("name"),
                kind=match.group("kind"),
                start_line=start_line,
                name_line=index,
                indent=match.group("indent"),
                visibility=visibility,
            )
        )
    return result


def scan_tree(paths: Iterable[Path]) -> tuple[list[TypeSymbol], list[MethodSymbol]]:
    """Scan a sequence of C# files and return type and method symbols."""
    types: list[TypeSymbol] = []
    methods: list[MethodSymbol] = []
    for path in paths:
        types.extend(scan_types(path))
        methods.extend(scan_methods(path))
    return types, methods
