#!/usr/bin/env python3
"""Check summaries on explicit C# fields, properties, and enum members.

This conservative line scanner supplements, not replaces, the Roslyn gate.
Use --write only when extending a model; review generated summaries before commit.
Methods and types are covered by document_csharp.py. The compiled Roslyn check
handles declarations that this deliberately narrow offline scanner cannot parse.
"""
from __future__ import annotations
import argparse
from pathlib import Path
import re
import sys
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[1]

MEANING = {
    'SchemaVersion': 'The schema revision; readers reject unsupported revisions.',
    'ToolVersion': 'The toolkit version that created this record.',
    'Version': 'The version used in CLI reports and release metadata.',
    'Game': 'The exact game profile used for interpreting scenario structures.',
    'SourceCpkSha256': 'The hexadecimal SHA-256 of the original, unmodified scenario archive.',
    'RebuiltCpkSha256': 'The hexadecimal SHA-256 of the rebuilt archive associated with this plan.',
    'SourceSha256': 'The hexadecimal SHA-256 of the exact input snapshot used by this object.',
    'GlyphMapSha256': 'The SHA-256 fingerprint binding this workspace to its glyph map.',
    'GlyphMapFile': 'The relative glyph-map filename resolved within the workspace directory.',
    'CreatedUtc': 'The UTC creation time of the manifest; it does not prove runtime compatibility.',
    'SourceLength': 'The expected original payload length, in bytes.',
    'TranslationSpeaker': 'The replacement speaker name; null preserves the original and an empty string clears it.',
    'TranslationMessage': 'The replacement dialogue text; null preserves the original and an empty string clears it.',
    'TranslationText': 'The replacement choice text; null preserves the original and an empty string clears it.',
    'SourceSpeaker': 'The immutable decoded speaker name used to detect workspace tampering.',
    'SourceMessage': 'The immutable decoded dialogue used to detect workspace tampering.',
    'SourceText': 'The immutable decoded choice text used to detect workspace tampering.',
    'MessageTerminator': 'The original scenario terminator; translators must not change this structural code.',
    'ScriptId': 'The numeric scenario ID in the source CPK archive.',
    'JumpId': 'The scenario jump-table ID associated with this choice group.',
    'Index': 'The zero-based position in the corresponding source collection.',
    'Id': 'The resource identifier preserved across extraction and rebuilding.',
    'PackedData': 'The packed payload bytes; callers must preserve or explicitly replace the contents.',
    'ExtractSize': 'The expected decoded payload length, in bytes.',
    'IsCrilayla': 'Whether the packed payload starts with a CRILAYLA frame signature.',
    'Default': 'The default conservative limits for parsing caller-supplied data.',
    'FileSystemComparer': 'The case-aware comparer for normalized host filesystem paths.',
    'FileSystemComparison': 'The host case-sensitivity rule for normalized path prefix checks.',
    'ConstantValue': 'The value stored once when a UTF column uses constant storage.',
    'Flags': 'The encoded flags combining storage mode and value type.',
    'Zero': 'A value represented by implicit zero storage.',
    'Constant': 'A value encoded once for all rows of a UTF column.',
    'PerRow': 'A value encoded separately in every UTF table row.',
    'Unsigned': 'The unsigned representation of an encoded integer field.',
    'Single': 'The single-precision floating-point value of a UTF field.',
    'Text': 'The decoded string value associated with this record.',
    'Data': 'The byte payload associated with this record.',
    'FirstId': 'The inclusive first scenario ID represented by the executable size table.',
    'LastIdInclusive': 'The inclusive last scenario ID represented by the executable size table.',
    'TableOffset': 'The byte offset of the scenario size table within the decrypted executable.',
    'Count': 'The number of logical entries in this collection.',
}


def summary(name: str, declaration: str) -> str:
    """Describe a known model field or derive a readable declaration-specific purpose."""
    if name in MEANING:
        return MEANING[name]
    readable = re.sub(r'(?<=[a-z0-9])(?=[A-Z])', ' ', name.lstrip('_')).lower()
    if name.startswith('Maximum'):
        return 'The upper safety limit for ' + readable.removeprefix('maximum ') + '; input above it is rejected.'
    if 'const ' in declaration:
        return 'The fixed ' + readable + ' value used by this format or revision.'
    if name.startswith('_'):
        return 'Stores the ' + readable + ' state owned by this instance or type.'
    return 'The ' + readable + ' value used by this model or operation.'


def member_lines(text: str) -> list[tuple[int, str]]:
    """Find explicit one-line declaration headers and simple enum value declarations."""
    result: list[tuple[int, str]] = []
    enum_pending = False
    in_enum = False
    for index, line in enumerate(text.splitlines()):
        stripped = line.strip()
        if re.search(r'\b(public|internal|private|protected)\s+enum\s+', stripped):
            enum_pending = True
        elif enum_pending and stripped == '{':
            in_enum, enum_pending = True, False
        elif in_enum:
            if stripped.startswith('}'):
                in_enum = False
            else:
                match = re.match(r'([A-Za-z_]\w*)\s*(?:=|,|$)', stripped)
                if match:
                    result.append((index, match.group(1)))
        if not re.match(r'(public|private|internal|protected)\s+', stripped):
            continue
        if re.search(r'\b(class|record|enum|interface|struct|delegate)\b', stripped):
            continue
        header = re.split(r'=>|=|\{|;', stripped, maxsplit=1)[0].strip()
        if '(' in header:
            continue
        match = re.search(r'\b([A-Za-z_]\w*)\s*$', header)
        if match and match.group(1) not in {'get', 'set', 'init'}:
            result.append((index, match.group(1)))
    return result


def has_summary(lines: list[str], index: int) -> bool:
    """Check the XML comment immediately preceding a declaration, including leading attributes."""
    cursor = index - 1
    while cursor >= 0 and (not lines[cursor].strip() or lines[cursor].lstrip().startswith('[')):
        cursor -= 1
    block: list[str] = []
    while cursor >= 0 and lines[cursor].lstrip().startswith('///'):
        block.append(lines[cursor])
        cursor -= 1
    return '<summary>' in '\n'.join(block) and '</summary>' in '\n'.join(block)


def main() -> int:
    """Report missing member summaries or insert deterministic starters for manual review."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--write', action='store_true')
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    total, missing = 0, []
    paths = [*ROOT.joinpath('src').rglob('*.cs'), *ROOT.joinpath('tests').rglob('*.cs'), *ROOT.joinpath('tools').rglob('*.cs')]
    for path in sorted(paths):
        if any(part in {'bin', 'obj'} for part in path.parts):
            continue
        text = path.read_text(encoding='utf-8-sig'); lines = text.splitlines(); edits = []
        for index, name in member_lines(text):
            total += 1
            if not has_summary(lines, index):
                missing.append(f'{path.relative_to(ROOT)}:{index + 1}: {name}')
                indentation = lines[index][:len(lines[index]) - len(lines[index].lstrip())]
                edits.append((index, indentation + '/// <summary>' + escape(summary(name, lines[index])) + '</summary>'))
        if args.write and edits:
            for index, comment in reversed(edits):
                lines.insert(index, comment)
            path.write_text('\n'.join(lines) + '\n', encoding='utf-8')
    print(f'Explicit fields/properties/enum values: {total}; missing summaries: {len(missing)}')
    if not args.write:
        for problem in missing:
            print(problem)
    return 0 if args.write or not missing else 1


if __name__ == '__main__':
    raise SystemExit(main())
