using LuckyStarPspToolkit.Formats.Common;
using LuckyStarPspToolkit.Formats.Cri;
using LuckyStarPspToolkit.Formats.Fonts;
using LuckyStarPspToolkit.Formats.Scripts;
using LuckyStarPspToolkit.Formats.Text;

namespace LuckyStarPspToolkit.Formats.Diagnostics;

/// <summary>
/// Represents immutable format diagnostic case result data exchanged by the toolkit.
/// </summary>
/// <param name="Name">The name value used by this model or operation.</param>
/// <param name="Passed">The passed value used by this model or operation.</param>
/// <param name="Error">The error value used by this model or operation.</param>
public sealed record FormatDiagnosticCaseResult(string Name, bool Passed, string? Error = null);

/// <summary>
/// Represents immutable format self test report data exchanged by the toolkit.
/// </summary>
/// <param name="Schema">The schema value used by this model or operation.</param>
/// <param name="Version">The version used in CLI reports and release metadata.</param>
/// <param name="Passed">The passed value used by this model or operation.</param>
/// <param name="Cases">The cases value used by this model or operation.</param>
public sealed record FormatSelfTestReport(
    string Schema,
    string Version,
    bool Passed,
    IReadOnlyList<FormatDiagnosticCaseResult> Cases);

/// <summary>
/// Provides the toolkit's format self diagnostics workflow.
/// </summary>
public static class FormatSelfDiagnostics
{
    /// <summary>
    /// Runs the built-in diagnostic cases and returns their individual results and aggregate status.
    /// </summary>
    /// <returns>A diagnostic report containing every executed case and its outcome.</returns>
    public static FormatSelfTestReport Run()
    {
        List<FormatDiagnosticCaseResult> cases = [];
        RunCase(cases, "glyph-map longest match", TestGlyphMap);
        RunCase(cases, "RGO checksum", TestChecksum);
        RunCase(cases, "lt.bin round-trip and PNG encoding", TestLtFont);
        RunCase(cases, "CRI UTF semantic round-trip", TestUtfRoundTrip);
        return new FormatSelfTestReport(
            "lucky-star-psp.formats-self-test.v1",
            ToolkitBuildInfo.Version,
            cases.All(static item => item.Passed),
            cases);
    }

    /// <summary>
    /// Formats the current result as human-readable diagnostic text.
    /// </summary>
    /// <param name="report">The report value.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    public static string ToHumanText(FormatSelfTestReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var output = new System.Text.StringBuilder();
        output.AppendLine($"Format layer {report.Version} self-test");
        foreach (FormatDiagnosticCaseResult item in report.Cases)
        {
            output.AppendLine($"  [{(item.Passed ? "PASS" : "FAIL")}] {item.Name}");
            if (!item.Passed && !string.IsNullOrWhiteSpace(item.Error))
            {
                output.AppendLine($"         {item.Error}");
            }
        }
        output.AppendLine($"Result: {(report.Passed ? "PASS" : "FAIL")}");
        return output.ToString();
    }

    /// <summary>
    /// Verifies glyph map behavior and invariants.
    /// </summary>
    private static void TestGlyphMap()
    {
        GlyphMap map = GlyphMap.FromLines([" ", "А", "Б", "АБ"]);
        ushort[] encoded = map.Encode("АБ А");
        if (!encoded.SequenceEqual(new ushort[] { 3, 0, 1 }))
        {
            throw new InvalidOperationException("Longest-match glyph encoding produced an unexpected sequence.");
        }
        if (!string.Equals(map.Decode(encoded), "АБ А", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Glyph decoding did not restore the source text.");
        }
    }

    /// <summary>
    /// Verifies checksum behavior and invariants.
    /// </summary>
    private static void TestChecksum()
    {
        byte[] script = new byte[4096];
        script[0] = 0x58;
        script[1] = 0xFD;
        script[0x120] = 0xA5;
        RgoChecksum.Apply(script);
        if (!RgoChecksum.Verify(script))
        {
            throw new InvalidOperationException("Checksum verification rejected a freshly signed buffer.");
        }
        script[0x120] ^= 1;
        if (RgoChecksum.Verify(script))
        {
            throw new InvalidOperationException("Checksum verification accepted modified data.");
        }
    }

    /// <summary>
    /// Verifies lt font behavior and invariants.
    /// </summary>
    private static void TestLtFont()
    {
        byte[] data = new byte[2048];
        data[LtFont.AdvanceWidthOffset] = 4;
        int second = LtFont.GlyphRecordSize;
        data[second] = 0x03;
        data[second + LtFont.AdvanceWidthOffset] = 9;
        data[second + 4] = 0xA0;
        RgoChecksum.Apply(data);

        LtFont font = LtFont.Parse(data, 2);
        if (!font.Build().SequenceEqual(data))
        {
            throw new InvalidOperationException("lt.bin round-trip changed preserved bytes.");
        }
        if (font.GetGlyph(1).IsBlank || font.GetGlyph(1).UnusedPixelBits[0] != 0xA0)
        {
            throw new InvalidOperationException("lt.bin glyph decoding lost bitmap or unused row bits.");
        }
        byte[] png = PngWriter.Encode(font.RenderAtlas(columns: 2));
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (png.Length < signature.Length || !png.AsSpan(0, signature.Length).SequenceEqual(signature))
        {
            throw new InvalidOperationException("PNG encoder produced an invalid signature.");
        }
    }

    /// <summary>
    /// Verifies UTF round trip behavior and invariants.
    /// </summary>
    private static void TestUtfRoundTrip()
    {
        CriUtfTable table = new() { Name = "RuntimeSelfTest" };
        table.Columns.Add(new CriUtfColumn
        {
            Name = "ID",
            Type = CriUtfType.UInt16,
            Storage = CriUtfStorage.PerRow
        });
        table.Columns.Add(new CriUtfColumn
        {
            Name = "Name",
            Type = CriUtfType.String,
            Storage = CriUtfStorage.PerRow
        });
        CriUtfRow row = table.CreateEmptyRow();
        table.Rows.Add(row);
        table.SetUnsigned(0, "ID", 7);
        table.SetText(0, "Name", "проверка");

        byte[] encoded = CriUtfCodec.BuildTable(table);
        CriUtfTable decoded = CriUtfCodec.ParseTable(encoded);
        if (decoded.GetUnsigned(0, "ID") != 7
            || !string.Equals(decoded.GetText(0, "Name"), "проверка", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("CRI UTF round-trip changed semantic values.");
        }
    }

    /// <summary>
    /// Runs one deterministic self-test case and records its result.
    /// </summary>
    /// <param name="output">The destination stream, buffer, or model.</param>
    /// <param name="name">The logical name used for lookup or diagnostics.</param>
    /// <param name="test">The test value.</param>
    private static void RunCase(ICollection<FormatDiagnosticCaseResult> output, string name, Action test)
    {
        try
        {
            test();
            output.Add(new FormatDiagnosticCaseResult(name, true));
        }
        catch (Exception ex)
        {
            output.Add(new FormatDiagnosticCaseResult(name, false, ex.Message));
        }
    }
}
