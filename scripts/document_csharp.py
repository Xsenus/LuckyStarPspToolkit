#!/usr/bin/env python3
"""Insert and validate repository-standard documentation comments in C# sources.

The script is intentionally deterministic.  It uses the lightweight declaration
scanner in :mod:`csharp_symbols`, adds XML documentation to declared types and
methods, and never rewrites executable code.
"""
from __future__ import annotations

import argparse
from pathlib import Path
import re
import sys

from csharp_symbols import MethodSymbol, TypeSymbol, scan_methods, scan_tree, scan_types

ROOT = Path(__file__).resolve().parents[1]

_ACRONYMS = {
    "Api": "API",
    "Ascii": "ASCII",
    "Bdf": "BDF",
    "Cli": "CLI",
    "Cmac": "CMAC",
    "Cpk": "CPK",
    "Crc": "CRC",
    "Cri": "CRI",
    "Eboot": "EBOOT",
    "Elf": "ELF",
    "Iso": "ISO",
    "Json": "JSON",
    "Mips": "MIPS",
    "Nim": "NIM",
    "Png": "PNG",
    "Prx": "PRX",
    "Psp": "PSP",
    "Rgo": "RGO",
    "Sfo": "SFO",
    "Sha": "SHA",
    "Utf": "UTF",
    "Vwf": "VWF",
    "Zip": "ZIP",
}

_PARAM_DESCRIPTIONS = {
    "args": "The command-line arguments to parse and execute.",
    "path": "The file-system path to process.",
    "inputPath": "The source file path.",
    "outputPath": "The destination file path.",
    "sourcePath": "The normalized source path.",
    "source": "The source binary data or object.",
    "input": "The input binary data or object.",
    "data": "The binary data to process.",
    "value": "The value to process.",
    "text": "The text to process.",
    "name": "The logical name used for lookup or diagnostics.",
    "context": "A diagnostic label included in validation errors.",
    "offset": "The zero-based byte or element offset.",
    "length": "The number of bytes or elements to process.",
    "count": "The number of items to process.",
    "alignment": "The required positive alignment in bytes.",
    "limits": "Optional conservative safety limits; defaults are used when omitted.",
    "stream": "The readable and seekable stream to process.",
    "destination": "The destination stream or buffer.",
    "output": "The destination stream, buffer, or model.",
    "writer": "The callback that writes the staged output stream.",
    "hash": "An optional incremental hash updated with copied bytes.",
    "leaveOpen": "Whether ownership of the supplied stream remains with the caller.",
    "id": "The numeric resource identifier.",
    "scriptId": "The numeric scenario identifier.",
    "entry": "The validated archive or ISO entry to process.",
    "profile": "The revision-specific format or patch profile.",
    "options": "Optional operation settings.",
    "manifest": "The validated operation manifest.",
    "mutation": "The requested text or binary mutations.",
    "map": "The glyph map used for text conversion.",
    "font": "The parsed font to inspect or modify.",
    "archive": "The parsed archive to inspect or modify.",
    "requests": "The complete set of transactional write requests.",
    "condition": "The condition that must be true.",
    "message": "The diagnostic message used when validation fails.",
    "innerException": "The exception that caused the current failure.",
    "expected": "The expected value.",
    "actual": "The actual value.",
    "minimum": "The inclusive minimum accepted value.",
    "maximum": "The inclusive maximum accepted value.",
    "defaultValue": "The value used when the option is omitted.",
    "reportPath": "The optional JSON report path.",
    "protectedPaths": "Paths that the report must not overwrite.",
    "protectedDirectories": "Directories inside which the report must not be written.",
}

_EXACT_SUMMARIES = {
    "Run": "Parses and executes a command-line invocation and returns a stable process exit code.",
    "Main": "Runs the process entry point.",
    "Dispose": "Releases owned streams and other disposable resources.",
    "ToString": "Returns the stable diagnostic string representation.",
    "Clone": "Creates a deep copy whose mutable buffers are independent of the source instance.",
    "DecryptVerified": "Authenticates a supported PSP PRX and returns its verified decrypted ELF payload.",
    "CollectFromIso": "Collects the minimum translation assets from a caller-owned PSP ISO into a deterministic ZIP bundle.",
    "ApplyBdf": "Rasterizes selected BDF glyphs into a cloned LT font while preserving untouched binary metadata.",
    "ApplyManifest": "Loads a strict ISO patch manifest and rebuilds a new verified ISO image.",
    "ApplyPreparedPatch": "Applies an already validated ISO patch plan to a new output image.",
    "LoadPatchManifest": "Loads, validates, snapshots, and resolves an ISO patch manifest.",
    "ComputeFileSha256": "Computes SHA-256 over exactly one ISO file payload, including all of its extents in order.",
    "ComputeSourceSha256": "Computes SHA-256 over the complete source image.",
    "ReadAllBytesBounded": "Reads a regular file once while enforcing configured size and reparse-point protections.",
    "WriteAll": "Commits a set of file writes as one best-effort transactional operation with rollback.",
    "WriteStream": "Writes a file through a staged stream and atomically replaces the destination after a durable flush.",
    "VerifySourcePrefixUnchanged": "Verifies that ISO bytes outside explicitly mutable metadata ranges remain byte-for-byte unchanged.",
    "Align": "Rounds a non-negative value up to the requested positive alignment.",
    "AlignUp": "Rounds a non-negative value up to the requested positive alignment.",
    "Parse": "Parses validated input into the current binary-format model.",
    "Build": "Serializes the current validated model into its binary representation.",
    "Apply": "Applies the requested transformation after validating all preconditions.",
    "Inspect": "Inspects the supplied input and returns a structured diagnostic result.",
    "Analyze": "Analyzes the supplied model and returns deterministic diagnostics.",
    "Validate": "Validates the supplied state and rejects violated format invariants.",
    "Load": "Loads and validates the requested data from its source.",
    "Save": "Persists the validated data to its destination.",
    "Serialize": "Serializes a value to stable UTF-8 JSON for reports or workspaces.",
    "Encode": "Encodes validated text or binary state into the target representation.",
    "Decode": "Decodes validated input into the source representation.",
    "Slice": "Returns a bounds-checked view over the requested range.",
    "ToArray": "Copies the current validated sequence into a new array.",
    "ToHumanText": "Formats the current result as human-readable diagnostic text.",
    "YesNo": "Formats a Boolean value as stable human-readable yes/no text.",
    "RangesOverlap": "Determines whether two half-open numeric ranges overlap.",
    "Sha256Hex": "Computes the lowercase SHA-256 hexadecimal digest of the supplied bytes.",
    "Utf8": "Encodes text as strict UTF-8 without a byte-order mark.",
    "LooksLike": "Determines whether the supplied data matches the expected format signature.",
    "Require": "Returns a required value or throws a stable validation error when it is absent.",
    "Ensure": "Checks a required invariant and throws a stable validation error when it fails.",
    "Audit": "Audits the supplied input and returns a structured safety and compatibility report.",
    "RunCase": "Runs one deterministic self-test case and records its result.",
}


def humanize(identifier: str) -> str:
    """Convert a C# identifier to readable words while preserving common acronyms."""
    words = re.findall(r"[A-Z]+(?=[A-Z][a-z]|\d|$)|[A-Z]?[a-z]+|\d+", identifier)
    if not words:
        return identifier
    normalized = [_ACRONYMS.get(word, word.lower()) for word in words]
    return " ".join(normalized)


def summary_for_type(symbol: TypeSymbol) -> str:
    """Create a concise purpose statement for a type declaration."""
    readable = humanize(symbol.name)
    if symbol.name.endswith("Exception"):
        return f"Represents a validated {readable} failure with a stable diagnostic code."
    if symbol.kind == "enum":
        return f"Defines the supported {readable} values."
    if symbol.kind.startswith("record"):
        return f"Represents immutable {readable} data exchanged by the toolkit."
    if symbol.name.endswith(("Codec", "Reader", "Writer", "Parser", "Patcher", "Rebuilder")):
        return f"Provides {readable} operations with strict bounds and format validation."
    if symbol.name.endswith(("Service", "Collector", "Auditor", "Diagnostics", "Inspector", "Profile", "Engine", "Utilities")):
        return f"Provides the toolkit's {readable} workflow."
    return f"Represents the toolkit's {readable} model or service."


def summary_for_method(symbol: MethodSymbol) -> str:
    """Create a method summary from its semantic naming convention."""
    if symbol.name in _EXACT_SUMMARIES:
        return _EXACT_SUMMARIES[symbol.name]
    if symbol.is_constructor:
        return "Initializes a new instance with validated constructor state."
    cli_commands = {
        "Help", "Version", "AuditRgo", "DecryptEboot", "EbootVwfGroups",
        "EbootVwfInspect", "EbootVwfApply", "CpkList", "CpkVerify",
        "CpkExtract", "CpkReplace", "ScriptInspect", "GlyphMapValidate",
        "FontInspect", "FontImportBdf", "WorkspaceExport", "WorkspaceValidate",
        "WorkspaceBuild", "CustomerAudit", "SelfTest", "IsoList", "IsoExtract",
        "IsoReplace", "IsoApplyManifest", "CollectAssets", "EbootBuild",
    }
    if symbol.path.name == "CommandApplication.cs" and symbol.name in cli_commands:
        command = re.sub(r"(?<!^)(?=[A-Z])", "-", symbol.name).lower()
        return f"Executes the <c>{command}</c> CLI command and returns a stable process exit code."
    readable = humanize(symbol.name)
    if symbol.name.startswith("Test"):
        return f"Verifies {humanize(symbol.name[4:])} behavior and invariants."
    prefixes = [
        ("To", "Converts"),
        ("From", "Creates"),
        ("Serialize", "Serializes"),
        ("Deserialize", "Deserializes"),
        ("Decrypt", "Decrypts"),
        ("Encrypt", "Encrypts"),
        ("Audit", "Audits"),
        ("Run", "Runs"),
        ("Save", "Saves"),
        ("Append", "Appends"),
        ("Derive", "Derives"),
        ("Transform", "Transforms"),
        ("Finalize", "Finalizes"),
        ("Detect", "Detects"),
        ("Determine", "Determines"),
        ("Probe", "Probes"),
        ("Find", "Finds"),
        ("Reject", "Rejects"),
        ("Protect", "Protects"),
        ("Commit", "Commits"),
        ("Prepare", "Prepares"),
        ("Update", "Updates"),
        ("Expand", "Expands"),
        ("Encode", "Encodes"),
        ("Decode", "Decodes"),
        ("Try", "Attempts to"),
        ("Parse", "Parses"),
        ("Read", "Reads"),
        ("Write", "Writes"),
        ("Build", "Builds"),
        ("Rebuild", "Rebuilds"),
        ("Validate", "Validates"),
        ("Verify", "Verifies"),
        ("Inspect", "Inspects"),
        ("Analyze", "Analyzes"),
        ("Compute", "Computes"),
        ("Create", "Creates"),
        ("Apply", "Applies"),
        ("Copy", "Copies"),
        ("Resolve", "Resolves"),
        ("Normalize", "Normalizes"),
        ("Load", "Loads"),
        ("Open", "Opens"),
        ("Extract", "Extracts"),
        ("Replace", "Replaces"),
        ("Collect", "Collects"),
        ("Get", "Gets"),
        ("Set", "Sets"),
        ("Add", "Adds"),
        ("Remove", "Removes"),
        ("Format", "Formats"),
        ("Print", "Writes"),
        ("Require", "Requires"),
        ("Ensure", "Ensures"),
        ("ThrowIf", "Throws when"),
        ("Contains", "Determines whether"),
        ("Is", "Determines whether"),
        ("Has", "Determines whether"),
    ]
    for prefix, verb in prefixes:
        if symbol.name.startswith(prefix) and len(symbol.name) > len(prefix):
            remainder = humanize(symbol.name[len(prefix):])
            if verb in {"Determines whether", "Attempts to", "Throws when"}:
                return f"{verb} {remainder}."
            return f"{verb} {remainder} while enforcing the relevant format and safety invariants."
    return f"Executes the {readable} operation."


def parameter_description(name: str, modifier: str | None) -> str:
    """Return a stable parameter description for generated XML documentation."""
    description = _PARAM_DESCRIPTIONS.get(name, f"The {humanize(name)} value.")
    if modifier == "out":
        return f"Receives {description.removeprefix('The ').removesuffix('.')} when the operation succeeds."
    if modifier == "ref":
        return f"The mutable {humanize(name)} value updated by the operation."
    return description


def type_parameter_description(name: str) -> str:
    """Return a stable description for a generic method type parameter."""
    readable = humanize(name)
    return f"The {readable} type used by the operation."


def returns_description(symbol: MethodSymbol) -> str | None:
    """Return an XML documentation sentence for a non-void result."""
    return_type = (symbol.return_type or "").replace(" ", "")
    if not return_type or return_type == "void":
        return None
    if return_type in {"bool", "Boolean"}:
        if symbol.name.startswith("Try"):
            return "<see langword=\"true\"/> when the requested value was produced; otherwise <see langword=\"false\"/>."
        return "<see langword=\"true\"/> when the condition is satisfied; otherwise <see langword=\"false\"/>."
    if symbol.name in {"Run", "Help", "Version"} or symbol.path.name == "CommandApplication.cs" and return_type == "int":
        return "A stable process exit code."
    if return_type in {"string", "string?", "String", "String?"}:
        return "The resulting text, path, identifier, or hexadecimal digest."
    if return_type.endswith("[]") or return_type.startswith("ReadOnlySpan<"):
        return "The resulting binary or typed sequence."
    return "The validated operation result."


def _doc_block_bounds(lines: list[str], start_line: int) -> tuple[int, int] | None:
    """Return the slice bounds of the XML documentation directly attached to a declaration."""
    cursor = start_line - 2
    while cursor >= 0 and not lines[cursor].strip():
        cursor -= 1
    if cursor < 0 or not lines[cursor].lstrip().startswith("///"):
        return None
    end = cursor + 1
    while cursor >= 0 and lines[cursor].lstrip().startswith("///"):
        cursor -= 1
    return cursor + 1, end


def _already_documented(lines: list[str], start_line: int) -> bool:
    """Return whether a declaration has an attached XML summary block."""
    bounds = _doc_block_bounds(lines, start_line)
    if bounds is None:
        return False
    block = "\n".join(lines[bounds[0] : bounds[1]])
    return "<summary>" in block and "</summary>" in block


def _documented_parameter_names(block: list[str]) -> list[str]:
    """Extract parameter names from an XML documentation block."""
    return re.findall(r'<param\s+name="([A-Za-z_][A-Za-z0-9_]*)"', "\n".join(block))


def _repair_method_block(lines: list[str], symbol: MethodSymbol) -> bool:
    """Normalize parameter, type-parameter, and return tags for one method comment."""
    bounds = _doc_block_bounds(lines, symbol.start_line)
    if bounds is None:
        lines[symbol.start_line - 1 : symbol.start_line - 1] = _method_comment(symbol)
        return True

    start, end = bounds
    original = lines[start:end]
    block = list(original)
    expected_summary = summary_for_method(symbol)
    for index, line in enumerate(block):
        stripped = line.strip()
        if stripped.startswith("/// Executes the ") and stripped.endswith(" operation."):
            block[index] = f"{symbol.indent}/// {expected_summary}"
            break
    block = [
        line for line in block
        if "<param " not in line and "<typeparam " not in line and "<returns>" not in line
    ]
    insert_at = next(
        (index + 1 for index, line in enumerate(block) if "</summary>" in line),
        len(block),
    )
    generated: list[str] = []
    for type_parameter in symbol.type_parameters:
        generated.append(
            f'{symbol.indent}/// <typeparam name="{type_parameter}">{type_parameter_description(type_parameter)}</typeparam>'
        )
    for parameter in symbol.parameters:
        generated.append(
            f'{symbol.indent}/// <param name="{parameter.name}">{parameter_description(parameter.name, parameter.modifier)}</param>'
        )
    returns = returns_description(symbol)
    if returns is not None:
        generated.append(f"{symbol.indent}/// <returns>{returns}</returns>")
    block[insert_at:insert_at] = generated
    if block == original:
        return False
    lines[start:end] = block
    return True

def repair_file(path: Path) -> int:
    """Repair incomplete XML documentation without changing executable C# code."""
    text = path.read_text(encoding="utf-8-sig")
    had_bom = path.read_bytes().startswith(b"\xef\xbb\xbf")
    lines = text.splitlines()
    changed = 0
    # Descending order keeps earlier declaration line numbers stable while edits are applied.
    for symbol in sorted(scan_methods(path), key=lambda item: item.start_line, reverse=True):
        if _repair_method_block(lines, symbol):
            changed += 1
    if changed:
        rendered = "\n".join(lines) + "\n"
        path.write_text(rendered, encoding="utf-8-sig" if had_bom else "utf-8", newline="\n")
    return changed


def documentation_problems(paths: list[Path]) -> list[str]:
    """Validate summaries, parameter tags, and return tags for all scanned declarations."""
    problems: list[str] = []
    types, methods = scan_tree(paths)
    by_path = {path: path.read_text(encoding="utf-8-sig").splitlines() for path in paths}
    for symbol in types:
        lines = by_path[symbol.path]
        if not _already_documented(lines, symbol.start_line):
            problems.append(f"{symbol.path}:{symbol.name_line}: type {symbol.name} has no XML summary")
    for symbol in methods:
        lines = by_path[symbol.path]
        bounds = _doc_block_bounds(lines, symbol.start_line)
        if bounds is None:
            problems.append(f"{symbol.path}:{symbol.name_line}: method {symbol.name} has no XML documentation")
            continue
        block = lines[bounds[0] : bounds[1]]
        text = "\n".join(block)
        if "<summary>" not in text or "</summary>" not in text:
            problems.append(f"{symbol.path}:{symbol.name_line}: method {symbol.name} has no XML summary")
        documented_type_parameters = re.findall(
            r'<typeparam\s+name="([A-Za-z_][A-Za-z0-9_]*)"', text
        )
        for type_parameter in symbol.type_parameters:
            occurrences = documented_type_parameters.count(type_parameter)
            if occurrences != 1:
                problems.append(
                    f"{symbol.path}:{symbol.name_line}: method {symbol.name} type parameter {type_parameter} has {occurrences} XML tag(s)"
                )
        unexpected_type_parameters = sorted(
            set(documented_type_parameters) - set(symbol.type_parameters)
        )
        for name in unexpected_type_parameters:
            problems.append(
                f"{symbol.path}:{symbol.name_line}: method {symbol.name} documents unknown type parameter {name}"
            )
        documented = _documented_parameter_names(block)
        for parameter in symbol.parameters:
            occurrences = documented.count(parameter.name)
            if occurrences != 1:
                problems.append(
                    f"{symbol.path}:{symbol.name_line}: method {symbol.name} parameter {parameter.name} has {occurrences} XML tag(s)"
                )
        unexpected = sorted(set(documented) - {parameter.name for parameter in symbol.parameters})
        for name in unexpected:
            problems.append(
                f"{symbol.path}:{symbol.name_line}: method {symbol.name} documents unknown parameter {name}"
            )
        needs_returns = returns_description(symbol) is not None
        returns_count = sum("<returns>" in line for line in block)
        if needs_returns and returns_count != 1:
            problems.append(
                f"{symbol.path}:{symbol.name_line}: method {symbol.name} has {returns_count} XML return tag(s)"
            )
        if not needs_returns and returns_count:
            problems.append(
                f"{symbol.path}:{symbol.name_line}: void/constructor {symbol.name} unexpectedly documents a return value"
            )
    return problems

def _method_comment(symbol: MethodSymbol) -> list[str]:
    indent = symbol.indent
    lines = [
        f"{indent}/// <summary>",
        f"{indent}/// {summary_for_method(symbol)}",
        f"{indent}/// </summary>",
    ]
    for type_parameter in symbol.type_parameters:
        lines.append(
            f'{indent}/// <typeparam name="{type_parameter}">{type_parameter_description(type_parameter)}</typeparam>'
        )
    for parameter in symbol.parameters:
        lines.append(
            f'{indent}/// <param name="{parameter.name}">{parameter_description(parameter.name, parameter.modifier)}</param>'
        )
    returns = returns_description(symbol)
    if returns is not None:
        lines.append(f"{indent}/// <returns>{returns}</returns>")
    if symbol.name.startswith(("Parse", "Build", "Rebuild", "Apply", "Validate", "Verify", "Decrypt")):
        lines.append(
            f"{indent}/// <remarks>Malformed input or violated preconditions are rejected before a persistent output is committed.</remarks>"
        )
    return lines


def _type_comment(symbol: TypeSymbol) -> list[str]:
    return [
        f"{symbol.indent}/// <summary>",
        f"{symbol.indent}/// {summary_for_type(symbol)}",
        f"{symbol.indent}/// </summary>",
    ]


def document_file(path: Path, include_types: bool = True) -> int:
    """Insert missing documentation comments into a single C# source file."""
    text = path.read_text(encoding="utf-8-sig")
    had_bom = path.read_bytes().startswith(b"\xef\xbb\xbf")
    lines = text.splitlines()
    insertions: list[tuple[int, list[str]]] = []
    if include_types:
        from csharp_symbols import scan_types

        for symbol in scan_types(path):
            if not _already_documented(lines, symbol.start_line):
                insertions.append((symbol.start_line - 1, _type_comment(symbol)))
    from csharp_symbols import scan_methods

    for symbol in scan_methods(path):
        if not _already_documented(lines, symbol.start_line):
            insertions.append((symbol.start_line - 1, _method_comment(symbol)))
    if not insertions:
        return 0
    for index, comment in sorted(insertions, key=lambda item: item[0], reverse=True):
        lines[index:index] = comment
    rendered = "\n".join(lines) + "\n"
    path.write_text(rendered, encoding="utf-8-sig" if had_bom else "utf-8", newline="\n")
    return len(insertions)


def undocumented(paths: list[Path]) -> list[str]:
    """Return all XML documentation coverage and completeness problems."""
    return documentation_problems(paths)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="Report missing or incomplete comments without modifying files.")
    parser.add_argument("--repair", action="store_true", help="Repair incomplete XML tags without rewriting executable code.")
    parser.add_argument("--include-tests", action="store_true", help="Process test sources in addition to src/.")
    args = parser.parse_args()

    paths = sorted((ROOT / "src").rglob("*.cs"))
    if args.include_tests:
        paths.extend(sorted((ROOT / "tests").rglob("*.cs")))
    paths = [p for p in paths if not {"bin", "obj"} & set(p.parts)]
    if args.check:
        problems = documentation_problems(paths)
        for problem in problems:
            print(problem, file=sys.stderr)
        print(f"C# documentation coverage: {len(paths)} files, {len(problems)} undocumented declaration(s).")
        return 0 if not problems else 1

    changed = 0
    if args.repair:
        for path in paths:
            changed += repair_file(path)
    else:
        for path in paths:
            changed += document_file(path)
    problems = documentation_problems(paths)
    if problems:
        for problem in problems:
            print(problem, file=sys.stderr)
        return 1
    print(f"Updated {changed} documentation block(s) across {len(paths)} C# files.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
