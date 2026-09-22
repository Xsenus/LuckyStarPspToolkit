#!/usr/bin/env python3
from __future__ import annotations

import hashlib
import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

from pygments import lex
from pygments.lexers.dotnet import CSharpLexer
from pygments.token import Comment, Literal

ROOT = Path(__file__).resolve().parents[1]
errors: list[str] = []
checks: list[str] = []

BINARY_SUFFIXES = {
    '.bin', '.cpk', '.dat', '.dll', '.elf', '.exe', '.iso', '.jpg', '.jpeg',
    '.pmf', '.png', '.prx', '.pyc', '.rgba', '.sfo', '.so', '.zip', '.bundle',
}
SEMANTIC_WHITESPACE_FILES = {
    Path('tests/LuckyStarPspToolkit.Formats.SelfTests/Fixtures/glyph-map.txt'),
}


def fail(message: str) -> None:
    errors.append(message)


def run_check(label: str, operation) -> None:
    before = len(errors)
    try:
        operation()
    except Exception as exc:
        fail(f'{label}: {type(exc).__name__}: {exc}')
    if len(errors) == before:
        checks.append(label)


def validate_utf8() -> None:
    for path in ROOT.rglob('*'):
        if not path.is_file() or any(part in {'bin', 'obj', '.git', 'artifacts', '__pycache__'} for part in path.parts):
            continue
        if path.suffix.lower() in BINARY_SUFFIXES:
            continue
        relative = path.relative_to(ROOT)
        try:
            text = path.read_text(encoding='utf-8')
        except UnicodeDecodeError as exc:
            fail(f'{relative} is not UTF-8: {exc}')
            continue
        if '\r' in text:
            fail(f'{relative} contains CR characters')
        if relative not in SEMANTIC_WHITESPACE_FILES:
            if any(line.rstrip(' \t') != line for line in text.splitlines()):
                fail(f'{relative} contains trailing whitespace')


def validate_json_xml() -> None:
    for path in ROOT.rglob('*.json'):
        if any(part in {'.git', 'bin', 'obj', 'artifacts'} for part in path.parts):
            continue
        json.loads(path.read_text(encoding='utf-8'))
    for path in list(ROOT.rglob('*.csproj')) + list(ROOT.rglob('*.props')) + [ROOT / 'NuGet.Config']:
        ET.parse(path)


def validate_project_references() -> None:
    projects = list(ROOT.rglob('*.csproj'))
    for project in projects:
        tree = ET.parse(project)
        for node in tree.findall('.//ProjectReference'):
            include = node.attrib.get('Include')
            if include is None:
                fail(f'{project.relative_to(ROOT)} has ProjectReference without Include')
                continue
            target = (project.parent / include.replace('\\', '/')).resolve()
            if not target.exists():
                fail(f'{project.relative_to(ROOT)} references missing {target}')

    solution = ROOT / 'LuckyStarPspToolkit.sln'
    if not solution.is_file():
        fail('canonical LuckyStarPspToolkit.sln is missing')
        return
    solution_text = solution.read_text(encoding='utf-8')
    for project in projects:
        relative = str(project.relative_to(ROOT)).replace('/', '\\')
        if relative not in solution_text:
            fail(f'{relative} is not included in LuckyStarPspToolkit.sln')
    stale = [
        ROOT / 'src/LuckyStarPspToolkit.NextCli',
        ROOT / '.legacy-cli-path',
        ROOT / 'LuckyStarPspToolkit.0.3.0.sln',
    ]
    for path in stale:
        if path.exists():
            fail(f'stale split-CLI artifact still exists: {path.relative_to(ROOT)}')


def validate_csharp_delimiters() -> None:
    pairs = {'}': '{', ')': '(', ']': '['}
    for path in ROOT.rglob('*.cs'):
        if {'bin', 'obj', '.git', 'artifacts'} & set(path.relative_to(ROOT).parts):
            continue
        text = path.read_text(encoding='utf-8')
        stack: list[tuple[str, int]] = []
        offset = 0
        for token_type, value in lex(text, CSharpLexer()):
            token_start = offset
            offset += len(value)
            if token_type in Comment or token_type in Literal.String or token_type in Literal.Char:
                continue
            for index, ch in enumerate(value):
                if ch in '{([':
                    stack.append((ch, token_start + index))
                elif ch in '})]':
                    if not stack or stack[-1][0] != pairs[ch]:
                        fail(f'{path.relative_to(ROOT)} unmatched {ch} at {token_start + index}')
                        break
                    stack.pop()
        if stack:
            fail(f'{path.relative_to(ROOT)} has unclosed delimiters: {stack[-5:]}')



def validate_csharp_documentation() -> None:
    """Require complete XML summaries, parameter tags, and return tags."""
    scripts = ROOT / 'scripts'
    if str(scripts) not in sys.path:
        sys.path.insert(0, str(scripts))
    from document_csharp import documentation_problems

    paths = [p for area in ('src', 'tests', 'tools') for p in (ROOT / area).rglob('*.cs') if not {'bin', 'obj'} & set(p.parts)]
    for problem in documentation_problems(paths):
        fail(problem)


def validate_source_policy() -> None:
    forbidden = [
        'NotImplementedException',
        'Process.Start(',
        'lsptool-legacy',
        'LuckyStarPspToolkit.NextCli',
    ]
    source_files = [p for p in ROOT.rglob('*.cs') if not {'bin', 'obj', 'artifacts'} & set(p.relative_to(ROOT).parts)]
    all_source = '\n'.join(path.read_text(encoding='utf-8') for path in source_files)
    for marker in forbidden:
        if marker in all_source:
            fail(f'forbidden marker in C# source: {marker}')
    network_allowlist = {
        'src/LuckyStarPspToolkit.Licensing/LicenseTransport.cs',
        'src/LuckyStarPspToolkit.LicenseAdmin/Program.cs',
        'tests/LuckyStarPspToolkit.Licensing.SelfTests/Program.cs',
    }
    for path in source_files:
        if 'HttpClient(' in path.read_text(encoding='utf-8') and path.relative_to(ROOT).as_posix() not in network_allowlist:
            fail(f'HTTP client outside the reviewed licensing boundary: {path.relative_to(ROOT)}')
    project_text = '\n'.join(path.read_text(encoding='utf-8') for path in ROOT.rglob('*.csproj'))
    if '<PackageReference' in project_text:
        fail('NuGet PackageReference found; runtime must remain dependency-free')
    # Existing ignored build outputs are permitted in a developer checkout.
    # Distribution and tracked-file audits separately forbid shipping them.



def validate_repository_readiness() -> None:
    """Require the publication, governance, workflow, and documentation surface."""
    required = [
        'README.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md', 'CONTRIBUTING.md',
        'CODE_OF_CONDUCT.md', 'SECURITY.md', 'SUPPORT.md', 'NuGet.Config',
        '.github/workflows/ci.yml', '.github/workflows/release.yml',
        '.github/dependabot.yml', '.github/PULL_REQUEST_TEMPLATE.md',
        '.github/ISSUE_TEMPLATE/bug_report.yml',
        '.github/ISSUE_TEMPLATE/feature_request.yml',
        '.github/ISSUE_TEMPLATE/config.yml',
        'docs/ARCHITECTURE.md', 'docs/FORMATS.md', 'docs/CLI_REFERENCE.md',
        'docs/DEVELOPMENT.md', 'docs/TESTING.md', 'docs/RELEASE.md',
        'docs/TROUBLESHOOTING.md', 'docs/LICENSING.md', 'docs/API_REFERENCE.md',
        'docs/USAGE_RU.md', 'docs/GITHUB_PUBLISH_RU.md',
        'examples/README.md', 'examples/iso-patch-manifest.example.json',
        'examples/translation-entry.example.json', 'examples/glyph-map.example.txt',
        'scripts/csharp_symbols.py', 'scripts/document_csharp.py',
        'scripts/generate_api_reference.py', 'scripts/package_release.py', 'validation/validate_csharp_docs.py',
        'validation/audit_repository.py', 'validation/validate_customer_baseline.py',
    ]
    for relative in required:
        path = ROOT / relative
        if not path.is_file() or path.stat().st_size == 0:
            fail(f'missing or empty publication file: {relative}')

    version = (ROOT / 'VERSION').read_text(encoding='utf-8').strip()
    readme = (ROOT / 'README.md').read_text(encoding='utf-8')
    if f'Версия **{version}**' not in readme:
        fail('README does not identify the current VERSION')
    for marker in ('docs/USAGE_RU.md', 'docs/CLI_REFERENCE.md', 'SECURITY.md', 'CONTRIBUTING.md'):
        if marker not in readme:
            fail(f'README does not link {marker}')

    ci = (ROOT / '.github/workflows/ci.yml').read_text(encoding='utf-8')
    release = (ROOT / '.github/workflows/release.yml').read_text(encoding='utf-8')
    for name, workflow in (('ci', ci), ('release', release)):
        for marker in ('ubuntu-latest', 'windows-latest', 'actions/setup-dotnet@v4',
                       'python scripts/build_release.py --rids', 'if-no-files-found: error'):
            if marker not in workflow:
                fail(f'{name} workflow is missing {marker!r}')
    for marker in ('tags:', 'VERSION', 'sha256sum -c', 'gh release create',
                   '--repo "$GITHUB_REPOSITORY"', '--draft --prerelease'):
        if marker not in release:
            fail(f'release workflow is missing safety marker {marker!r}')
    driver = (ROOT / 'scripts/build_release.py').read_text(encoding='utf-8')
    for marker in ('document_csharp.py', 'document_members.py', 'reference_oracle.py',
                   'static_validate.py', 'audit_repository.py', 'validate_customer_baseline.py',
                   'LuckyStarPspToolkit.SelfTests', 'LuckyStarPspToolkit.Formats.SelfTests',
                   'LuckyStarPspToolkit.Documentation', 'license_integration.py', 'check_locked_cli.py',
                   'LuckyStarPspToolkit.Licensing.SelfTests', 'validate_public_profile', 'native_rid()',
                   'promote_directory', 'compiled', 'gameRuntimeVerified'):
        if marker not in driver:
            fail(f'shared release driver is missing gate {marker!r}')
    if 'scripts/demo_customer.py' not in (ROOT / 'scripts/license_integration.py').read_text(encoding='utf-8'):
        fail('Licensed integration must still run the original synthetic game-tool demonstration')
    nuget = ET.parse(ROOT / 'NuGet.Config')
    sources = nuget.findall('.//packageSources/add')
    if nuget.find('.//packageSources/clear') is None or len(sources) != 1:
        fail('NuGet.Config must clear defaults and contain exactly the official source')
    elif sources[0].get('value') != 'https://api.nuget.org/v3/index.json':
        fail('RID restoration is permitted only from the official NuGet source')

    api_reference = (ROOT / 'docs/API_REFERENCE.md').read_text(encoding='utf-8')
    if '# C# API and method catalog' not in api_reference:
        fail('API reference header is missing')
    if 'Named methods, constructors, and local functions:' not in api_reference:
        fail('API reference coverage footer is missing')

    notices = (ROOT / 'THIRD_PARTY_NOTICES.md').read_text(encoding='utf-8')
    for marker in ('rights review', 'synthetic fixtures', 'PPSSPP', 'RyououGakuenToolkit'):
        if marker.lower() not in notices.lower():
            fail(f'THIRD_PARTY_NOTICES.md is missing {marker!r}')


def validate_optimization_contracts() -> None:
    """Keep the release-hardening optimizations and their regression test present."""
    binary = (ROOT / 'src/LuckyStarPspToolkit.Formats/Common/BinaryUtilities.cs').read_text(encoding='utf-8')
    for marker in ('NormalizeProtectedPath', 'ReadExactly', 'FILE_CHANGED', 'originalLength'):
        if marker not in binary:
            fail(f'bounded file I/O hardening is missing marker {marker}')

    cpk = (ROOT / 'src/LuckyStarPspToolkit.Formats/Cri/CriCpkArchive.cs').read_text(encoding='utf-8')
    if '.Except(low)' in cpk or '_entries.Values.Sum' in cpk:
        fail('CPK build regressed to repeated LINQ traversals')
    for marker in ('packedSumSigned', 'extractSumSigned', 'low.Add(entry)', 'high.Add(entry)'):
        if marker not in cpk:
            fail(f'CPK single-pass partition is missing marker {marker}')

    iso = (ROOT / 'src/LuckyStarPspToolkit.Formats/Iso/Iso9660Rebuilder.cs').read_text(encoding='utf-8')
    if 'byte[] originalBuffer,\n        byte[] stagedBuffer' not in iso:
        fail('ISO unchanged-range comparator does not accept reusable pooled buffers')
    verify_start = iso.find('private static void VerifySourcePrefixUnchanged')
    verify_end = iso.find('private static ByteRange[] NormalizeMutableRanges', verify_start)
    verify_source = iso[verify_start:verify_end]
    if verify_source.count('ArrayPool<byte>.Shared.Rent(1024 * 1024)') != 2:
        fail('ISO source-prefix verification must rent exactly two reusable buffers')

    glyph = (ROOT / 'src/LuckyStarPspToolkit.Formats/Text/GlyphMap.cs').read_text(encoding='utf-8')
    analyze_start = glyph.find('public GlyphMapAnalysis Analyze()')
    analyze_end = glyph.find('public static GlyphMap Load', analyze_start)
    analyze = glyph[analyze_start:analyze_end]
    if '.GroupBy(' in analyze or '.Select(static (text, index)' in analyze:
        fail('glyph-map analysis regressed to multi-pass indexed LINQ')
    for marker in ('indicesByText', 'encodable++', '_lowestIndices.ContainsKey(token)'):
        if marker not in analyze:
            fail(f'glyph-map single-pass analysis is missing marker {marker}')

    tests = (ROOT / 'tests/LuckyStarPspToolkit.Formats.SelfTests/Program.cs').read_text(encoding='utf-8')
    for marker in ('TestBoundedFileIo', 'FILE_TOO_LARGE', 'Sha256HexFile'):
        if marker not in tests:
            fail(f'bounded file I/O regression test is missing marker {marker}')


def validate_versions() -> None:
    version = (ROOT / 'VERSION').read_text(encoding='utf-8').strip()
    if not re.fullmatch(r'\d+\.\d+\.\d+', version):
        fail(f'VERSION is not semantic major.minor.patch: {version}')
        return
    props_tree = ET.parse(ROOT / 'Directory.Build.props')
    property_values = {
        child.tag: child.text
        for group in props_tree.findall('PropertyGroup')
        for child in group
    }
    expected = {
        'Version': version,
        'AssemblyVersion': version + '.0',
        'FileVersion': version + '.0',
        'InformationalVersion': version,
    }
    for name, value in expected.items():
        if property_values.get(name) != value:
            fail(f'{name} does not match VERSION: {property_values.get(name)!r} != {value!r}')
    for project in ROOT.rglob('*.csproj'):
        tree = ET.parse(project)
        for node_name in expected:
            if tree.find(f'.//{node_name}') is not None:
                fail(f'{project.relative_to(ROOT)} overrides centrally managed {node_name}')
    diagnostics = [
        ROOT / 'src/LuckyStarPspToolkit.Core/SelfDiagnostics.cs',
        ROOT / 'src/LuckyStarPspToolkit.Formats/Common/ToolkitBuildInfo.cs',
    ]
    for path in diagnostics:
        text = path.read_text(encoding='utf-8')
        if 'Assembly.GetName().Version' not in text:
            fail(f'{path.relative_to(ROOT)} does not derive its version from assembly metadata')
    cli = (ROOT / 'src/LuckyStarPspToolkit.Cli/CommandApplication.cs').read_text(encoding='utf-8')
    if 'ToolkitBuildInfo.Version' not in cli:
        fail('unified CLI does not use centrally managed build version')


def validate_profile_constants() -> None:
    source = (ROOT / 'src/LuckyStarPspToolkit.Core/RgoProfile.cs').read_text(encoding='utf-8')
    report = json.loads((ROOT / 'validation/customer-validation.json').read_text(encoding='utf-8'))
    encrypted = report['eboot']['encrypted_sha256']
    decrypted = report['eboot']['decrypted_sha256']
    if encrypted not in source:
        fail('known encrypted hash missing from RgoProfile.cs')
    if decrypted not in source:
        fail('known decrypted hash missing from RgoProfile.cs')
    for point in report['eboot']['patch_points']:
        off = int(point['offset'], 16)
        expected = int(point['expected'], 16)
        pattern = rf'0x{off:06X}[^\n]*0x{expected:08X}'
        if not re.search(pattern, source, re.IGNORECASE):
            fail(f'patch profile mismatch at 0x{off:08X}')


def validate_vwf_profile_contracts() -> None:
    profile_path = ROOT / 'profiles/rgo-uljm05752-vwf-profile.json'
    schema_path = ROOT / 'schemas/rgo-eboot-vwf-profile.schema.json'
    source_path = ROOT / 'src/LuckyStarPspToolkit.Core/RgoVwfPatchProfile.cs'
    oracle_path = ROOT / 'validation/validate_vwf_patch.py'
    for path in (profile_path, schema_path, source_path, oracle_path):
        if not path.is_file():
            fail(f'missing RGO VWF contract file: {path.relative_to(ROOT)}')
    if not all(path.is_file() for path in (profile_path, schema_path, source_path, oracle_path)):
        return

    profile = json.loads(profile_path.read_text(encoding='utf-8'))
    schema = json.loads(schema_path.read_text(encoding='utf-8'))
    if profile.get('schema') != 'lucky-star-psp.rgo-eboot-vwf-profile.v1':
        fail('RGO VWF profile schema marker mismatch')
    if schema.get('additionalProperties') is not False:
        fail('RGO VWF JSON Schema must reject unknown root properties')
    expected_required = {
        'schema', 'profileId', 'game', 'discId', 'input', 'fullOutput',
        'scriptSizeTable', 'scriptHeap', 'groups', 'patches',
    }
    if set(schema.get('required', [])) != expected_required:
        fail('RGO VWF JSON Schema required fields mismatch')

    patches = profile.get('patches')
    groups = profile.get('groups')
    if not isinstance(patches, list) or len(patches) != 133:
        fail(f'RGO VWF profile must contain 133 patch words, got {len(patches) if isinstance(patches, list) else "invalid"}')
        return
    if not isinstance(groups, list) or len(groups) != 5:
        fail('RGO VWF profile must contain five patch groups')
        return
    expected_group_counts = {
        'vwf-core': 66,
        'message-layout': 7,
        'speaker-layout': 50,
        'choice-layout': 3,
        'name-layout': 7,
    }
    actual_counts: dict[str, int] = {}
    names: set[str] = set()
    offsets: set[int] = set()
    for patch in patches:
        group = patch.get('group')
        name = patch.get('name')
        offset = patch.get('offset')
        expected = patch.get('expected')
        replacement = patch.get('replacement')
        if not isinstance(group, str):
            fail('RGO VWF patch has invalid group')
            continue
        actual_counts[group] = actual_counts.get(group, 0) + 1
        if not isinstance(name, str) or not name or name in names:
            fail(f'RGO VWF patch has invalid or duplicate name: {name!r}')
        else:
            names.add(name)
        if not isinstance(offset, int) or offset < 0 or offset % 4 or offset in offsets:
            fail(f'RGO VWF patch has invalid or duplicate offset: {offset!r}')
        else:
            offsets.add(offset)
        if not isinstance(expected, int) or not isinstance(replacement, int) or expected == replacement:
            fail(f'RGO VWF patch {name!r} has invalid values')
        if isinstance(offset, int) and patch.get('offsetHex') != f'0x{offset:08X}':
            fail(f'RGO VWF patch {name!r} offset hex mismatch')
        if isinstance(expected, int) and patch.get('expectedHex') != f'0x{expected:08X}':
            fail(f'RGO VWF patch {name!r} expected hex mismatch')
        if isinstance(replacement, int) and patch.get('replacementHex') != f'0x{replacement:08X}':
            fail(f'RGO VWF patch {name!r} replacement hex mismatch')
    if actual_counts != expected_group_counts:
        fail(f'RGO VWF group counts mismatch: {actual_counts}')

    source = source_path.read_text(encoding='utf-8')
    pattern = re.compile(
        r'new\("(?P<group>[^"]+)",\s*"(?P<name>[^"]+)",\s*'
        r'0x(?P<offset>[0-9A-Fa-f]+),\s*0x(?P<expected>[0-9A-Fa-f]+),\s*'
        r'0x(?P<replacement>[0-9A-Fa-f]+)\),'
    )
    source_patches = [
        {
            'group': match.group('group'),
            'name': match.group('name'),
            'offset': int(match.group('offset'), 16),
            'expected': int(match.group('expected'), 16),
            'replacement': int(match.group('replacement'), 16),
        }
        for match in pattern.finditer(source)
    ]
    profile_patches = [
        {key: patch[key] for key in ('group', 'name', 'offset', 'expected', 'replacement')}
        for patch in patches
    ]
    if source_patches != profile_patches:
        fail('embedded C# RGO VWF words do not match the declarative profile')

    script_heap = profile.get('scriptHeap')
    if not isinstance(script_heap, dict):
        fail('RGO VWF profile does not define scriptHeap metadata')
    else:
        expected_heap = {
            'luiOffset': 0x15B54,
            'luiOffsetHex': '0x00015B54',
            'addiuOffset': 0x15B58,
            'addiuOffsetHex': '0x00015B58',
            'originalCapacityBytes': 0x236000,
            'originalCapacityHex': '0x00236000',
            'originalLuiInstruction': 0x3C040023,
            'originalLuiInstructionHex': '0x3C040023',
            'originalAddiuInstruction': 0x24846000,
            'originalAddiuInstructionHex': '0x24846000',
            'policy': 'max-original-or-largest-script',
        }
        for key, value in expected_heap.items():
            if script_heap.get(key) != value:
                fail(f'RGO VWF scriptHeap {key} mismatch: {script_heap.get(key)!r} != {value!r}')

        core_profile = (ROOT / 'src/LuckyStarPspToolkit.Core/RgoProfile.cs').read_text(encoding='utf-8')
        format_plan = (ROOT / 'src/LuckyStarPspToolkit.Formats/Workspace/EbootSizePatchPlan.cs').read_text(encoding='utf-8')
        required_core_markers = (
            'ScriptHeapLuiOffset = 0x15B54',
            'ScriptHeapAddiuOffset = 0x15B58',
            'KnownScriptHeapBytes = 0x236000',
            'KnownScriptHeapLuiInstruction = 0x3C040023',
            'KnownScriptHeapAddiuInstruction = 0x24846000',
        )
        required_format_markers = (
            'LuiOffset = 0x15B54',
            'AddiuOffset = 0x15B58',
            'OriginalCapacityBytes = 0x236000',
            'OriginalLuiInstruction = 0x3C040023',
            'OriginalAddiuInstruction = 0x24846000',
        )
        for marker in required_core_markers:
            if marker not in core_profile:
                fail(f'RgoProfile.cs is missing script-heap marker {marker}')
        for marker in required_format_markers:
            if marker not in format_plan:
                fail(f'EbootSizePatchPlan.cs is missing script-heap marker {marker}')

    input_hash = profile.get('input', {}).get('sha256')
    output_hash = profile.get('fullOutput', {}).get('sha256')
    rgo_source = (ROOT / 'src/LuckyStarPspToolkit.Core/RgoProfile.cs').read_text(encoding='utf-8')
    if not isinstance(input_hash, str) or input_hash not in rgo_source:
        fail('RGO VWF input hash does not match RgoProfile.cs')
    if not isinstance(output_hash, str) or output_hash not in source:
        fail('RGO VWF output hash does not match RgoVwfPatchProfile.cs')

    cli = (ROOT / 'src/LuckyStarPspToolkit.Cli/CommandApplication.cs').read_text(encoding='utf-8')
    for command in ('eboot-vwf-groups', 'eboot-vwf-inspect', 'eboot-vwf-apply'):
        if command not in cli:
            fail(f'unified CLI is missing {command}')


def validate_font_contracts() -> None:
    required = [
        ROOT / 'src/LuckyStarPspToolkit.Formats/Fonts/LtFont.cs',
        ROOT / 'src/LuckyStarPspToolkit.Formats/Fonts/BdfFont.cs',
        ROOT / 'src/LuckyStarPspToolkit.Formats/Fonts/LtFontPatcher.cs',
        ROOT / 'src/LuckyStarPspToolkit.Formats/Fonts/PngWriter.cs',
        ROOT / 'scripts/reference_oracle.py',
        ROOT / 'validation/validate_font_fixtures.py',
    ]
    for path in required:
        if not path.is_file():
            fail(f'missing font pipeline file: {path.relative_to(ROOT)}')

    cli = (ROOT / 'src/LuckyStarPspToolkit.Cli/CommandApplication.cs').read_text(encoding='utf-8')
    for command in ('font-inspect', 'font-import-bdf'):
        if command not in cli:
            fail(f'unified CLI is missing {command}')

    oracle_path = ROOT / 'tests/LuckyStarPspToolkit.Formats.SelfTests/Fixtures/oracle.json'
    base = oracle_path.parent
    generated_names = [
        'reference-lt.bin',
        'reference-lt-russian.bin',
        'reference-lt-russian-atlas.rgba',
        'reference-font.bdf',
    ]
    generated_present = [(base / name).is_file() for name in generated_names]
    if any(generated_present) and not all(generated_present):
        fail('font oracle output is only partially generated; rerun scripts/reference_oracle.py')
        return
    if not all(generated_present):
        return
    oracle = json.loads(oracle_path.read_text(encoding='utf-8'))
    fixture_pairs = {
        'fontSha256': 'reference-lt.bin',
        'patchedFontSha256': 'reference-lt-russian.bin',
        'atlasRgbaSha256': 'reference-lt-russian-atlas.rgba',
        'bdfSha256': 'reference-font.bdf',
        'glyphMapSha256': 'glyph-map.txt',
    }
    for key, name in fixture_pairs.items():
        path = base / name
        if not path.is_file():
            continue
        actual = hashlib.sha256(path.read_bytes()).hexdigest()
        if oracle.get(key) != actual:
            fail(f'font oracle hash mismatch for {name}: {actual}')

    glyphs = (base / 'glyph-map.txt').read_text(encoding='utf-8').splitlines()
    required_russian = (
        'АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯ'
        'абвгдеёжзийклмнопрстуфхцчшщъыьэюя'
    )
    missing = sorted(set(required_russian) - set(glyphs))
    if missing:
        fail(f'synthetic glyph map misses Russian characters: {"".join(missing)}')


def validate_iso_contracts() -> None:
    required = [
        ROOT / 'src/LuckyStarPspToolkit.Formats/Iso/Iso9660Models.cs',
        ROOT / 'src/LuckyStarPspToolkit.Formats/Iso/SeekableDataSource.cs',
        ROOT / 'src/LuckyStarPspToolkit.Formats/Iso/Iso9660Image.cs',
        ROOT / 'src/LuckyStarPspToolkit.Formats/Iso/PspAssetCollector.cs',
        ROOT / 'src/LuckyStarPspToolkit.Formats/Iso/Iso9660Rebuilder.cs',
        ROOT / 'tests/LuckyStarPspToolkit.Formats.SelfTests/Fixtures/reference.iso',
        ROOT / 'tests/LuckyStarPspToolkit.Formats.SelfTests/Fixtures/reference-patched.iso',
        ROOT / 'tests/LuckyStarPspToolkit.Formats.SelfTests/Fixtures/iso-patch-manifest.json',
        ROOT / 'validation/validate_iso_fixture.py',
        ROOT / 'validation/validate_iso_rebuild.py',
        ROOT / 'profiles/iso-patch-manifest.example.json',
        ROOT / 'schemas/iso-patch-manifest.schema.json',
        ROOT / 'docs/ISO_REBUILD_RU.md',
        ROOT / 'scripts/rebuild-game-iso.ps1',
        ROOT / 'scripts/rebuild-game-iso.sh',
    ]
    for path in required:
        if not path.is_file():
            fail(f'missing ISO pipeline file: {path.relative_to(ROOT)}')

    cli = (ROOT / 'src/LuckyStarPspToolkit.Cli/CommandApplication.cs').read_text(encoding='utf-8')
    for command in ('iso-list', 'iso-extract', 'iso-replace', 'iso-apply-manifest', 'collect-assets'):
        if command not in cli:
            fail(f'unified CLI is missing {command}')

    rebuilder = (ROOT / 'src/LuckyStarPspToolkit.Formats/Iso/Iso9660Rebuilder.cs').read_text(encoding='utf-8')
    for marker in (
        'lucky-star-psp.iso-patch-manifest.v1',
        'ApplyPreparedPatch',
        'ManifestSha256',
        'ISO_SOURCE_CHANGED',
        'ISO_REPLACEMENT_CHANGED',
        'ISO_REPLACEMENT_TOTAL_TOO_LARGE',
        'ISO_REPLACEMENT_SIZE',
        'ISO_REPLACEMENT_SHA256',
        'ExpectedReplacementSha256',
        'VerifyStagedImage',
        'VerifySourcePrefixUnchanged',
        'ISO_REBUILD_VERIFY_PREFIX',
        'public required string Schema',
        'public required List<Iso9660PatchManifestEntry> Replacements',
    ):
        if marker not in rebuilder:
            fail(f'ISO rebuild source is missing safety marker {marker}')

    oracle_path = ROOT / 'tests/LuckyStarPspToolkit.Formats.SelfTests/Fixtures/oracle.json'
    iso_path = oracle_path.parent / 'reference.iso'
    if not oracle_path.is_file() or not iso_path.is_file():
        return
    oracle = json.loads(oracle_path.read_text(encoding='utf-8'))
    actual = hashlib.sha256(iso_path.read_bytes()).hexdigest()
    if oracle.get('isoSha256') != actual:
        fail(f'ISO oracle hash mismatch for reference.iso: {actual}')
    if oracle.get('isoLength') != iso_path.stat().st_size:
        fail('ISO oracle length mismatch for reference.iso')
    if oracle.get('isoVolumeIdentifier') != 'LSPTOOL_ORACLE':
        fail('ISO oracle volume identifier mismatch')
    iso_files = oracle.get('isoFiles')
    if not isinstance(iso_files, dict):
        fail('ISO oracle does not contain isoFiles metadata')
        return
    required_paths = (
        'PSP_GAME/PARAM.SFO',
        'PSP_GAME/SYSDIR/EBOOT.BIN',
        'PSP_GAME/USRDIR/DATA/sc.cpk',
        'PSP_GAME/USRDIR/DATA/lt.bin',
        'PSP_GAME/USRDIR/DATA/MULTI.BIN',
    )
    for required_path in required_paths:
        if required_path not in iso_files:
            fail(f'ISO oracle misses {required_path}')
    selected = (
        'PSP_GAME/PARAM.SFO',
        'PSP_GAME/SYSDIR/EBOOT.BIN',
        'PSP_GAME/USRDIR/DATA/sc.cpk',
        'PSP_GAME/USRDIR/DATA/lt.bin',
    )
    if all(path in iso_files for path in selected):
        total = sum(int(iso_files[path]['size']) for path in selected)
        if total != 139772:
            fail(f'ISO oracle required asset byte total mismatch: {total}')

    patched_path = oracle_path.parent / 'reference-patched.iso'
    manifest_path = oracle_path.parent / 'iso-patch-manifest.json'
    if patched_path.is_file():
        patched_hash = hashlib.sha256(patched_path.read_bytes()).hexdigest()
        if oracle.get('patchedIsoSha256') != patched_hash:
            fail(f'ISO oracle hash mismatch for reference-patched.iso: {patched_hash}')
        if oracle.get('patchedIsoLength') != patched_path.stat().st_size:
            fail('ISO oracle length mismatch for reference-patched.iso')
        replacements = oracle.get('patchedIsoReplacements')
        if not isinstance(replacements, dict) or len(replacements) != 2:
            fail('ISO rebuild oracle must contain exactly two replacement records')
    schema_path = ROOT / 'schemas/iso-patch-manifest.schema.json'
    if schema_path.is_file():
        schema = json.loads(schema_path.read_text(encoding='utf-8'))
        if schema.get('additionalProperties') is not False:
            fail('ISO patch JSON Schema must reject unknown root properties')
        if set(schema.get('required', [])) != {'schema', 'replacements'}:
            fail('ISO patch JSON Schema required fields mismatch')
    if manifest_path.is_file():
        manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
        if manifest.get('schema') != 'lucky-star-psp.iso-patch-manifest.v1':
            fail('ISO patch manifest schema mismatch')
        replacements = manifest.get('replacements')
        if not isinstance(replacements, list) or len(replacements) != 2:
            fail('ISO patch manifest must contain exactly two fixture replacements')
        else:
            for item in replacements:
                source_file = item.get('sourceFile')
                replacement_path = manifest_path.parent / source_file if isinstance(source_file, str) else None
                if replacement_path is None or not replacement_path.is_file():
                    fail(f'ISO patch manifest replacement is missing: {source_file!r}')
                    continue
                payload = replacement_path.read_bytes()
                if item.get('expectedReplacementSize') != len(payload):
                    fail(f'ISO patch manifest replacement size mismatch: {source_file}')
                if item.get('expectedReplacementSha256') != hashlib.sha256(payload).hexdigest():
                    fail(f'ISO patch manifest replacement SHA-256 mismatch: {source_file}')
        actual_manifest_hash = hashlib.sha256(manifest_path.read_bytes()).hexdigest()
        if oracle.get('isoPatchManifestSha256') != actual_manifest_hash:
            fail('ISO patch manifest hash mismatch')


def validate_prx_constants() -> None:
    source = (ROOT / 'src/LuckyStarPspToolkit.Core/PspPrx.cs').read_text(encoding='utf-8')
    expected_arrays = {
        'TagKeyD91613F0': 'ebff40d8b41ae166913b8f64b6fcb712',
        'Kirk7Key5D': '115a5d20d53a8dd39cc5af410f0f186f',
        'Kirk1Key': '98c940975c1d10e87fe60ea3fd03a8ba',
    }
    for name, expected in expected_arrays.items():
        match = re.search(rf'{name}\s*=\s*\[(.*?)\];', source, re.DOTALL)
        if not match:
            fail(f'cannot find PRX key array {name}')
            continue
        actual = ''.join(re.findall(r'0x([0-9A-Fa-f]{2})', match.group(1))).lower()
        if actual != expected:
            fail(f'PRX key {name} mismatch: {actual}')
    if 'SupportedRgoTag = 0xD91613F0' not in source:
        fail('supported PRX tag mismatch')


def main() -> int:
    run_check('text files are UTF-8/LF and policy-clean', validate_utf8)
    run_check('JSON and MSBuild XML files parse', validate_json_xml)
    run_check('projects and canonical solution are consistent', validate_project_references)
    run_check('C# delimiter balance passes token-aware scan', validate_csharp_delimiters)
    run_check('recognized named C# types and methods have XML documentation (offline check)', validate_csharp_documentation)
    run_check('source has no sidecar/process/NuGet dependencies; networking is confined to licensing', validate_source_policy)
    run_check('repository governance files and checked workflow entry points are present (not executed)', validate_repository_readiness)
    run_check('release-hardening optimizations and tests remain present', validate_optimization_contracts)
    run_check('version metadata is centrally consistent', validate_versions)
    run_check('revision profile agrees with preserved customer report (no fresh raw-data run)', validate_profile_constants)
    run_check('RGO VWF profile, schema, C# source, and CLI remain synchronized', validate_vwf_profile_contracts)
    run_check('PRX tag and AES constants match the independent oracle', validate_prx_constants)
    run_check('font pipeline source and fixture contracts are consistent', validate_font_contracts)
    run_check('ISO parser, asset collector, and fixture contracts are consistent', validate_iso_contracts)

    report = {
        'schema': 'lucky-star-psp.static-validation.v2',
        'passed': not errors,
        'checks': checks,
        'errors': errors,
    }
    output = ROOT / 'validation/static-validation.json'
    output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    for check in checks:
        print(f'PASS {check}')
    for error in errors:
        print(f'FAIL {error}', file=sys.stderr)
    return 0 if not errors else 1


if __name__ == '__main__':
    raise SystemExit(main())
