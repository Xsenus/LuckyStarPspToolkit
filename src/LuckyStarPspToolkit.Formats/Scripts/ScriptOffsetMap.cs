using LuckyStarPspToolkit.Formats.Common;

namespace LuckyStarPspToolkit.Formats.Scripts;

/// <summary>A contiguous mapping of original bytes to rebuilt bytes; changed interiors cannot be addressed safely.</summary>
/// <param name="OldStart">Inclusive original start.</param>
/// <param name="OldEnd">Exclusive original end; may equal the start for an insertion.</param>
/// <param name="NewStart">Inclusive rebuilt start.</param>
/// <param name="NewEnd">Exclusive rebuilt end.</param>
/// <param name="Changed">Whether interior offsets are ambiguous due to replacement.</param>
internal readonly record struct ScriptOffsetSegment(int OldStart, int OldEnd, int NewStart, int NewEnd, bool Changed);

/// <summary>
/// Indexes contiguous relocation segments once and answers each jump/metadata query in O(log N).
/// At a shared boundary the first segment wins, preserving the previous mapper's left bias
/// and keeping a choice pointer before an insertion into an originally empty choice.
/// </summary>
internal sealed class ScriptOffsetMap
{
    /// <summary>Validated owned segments in monotonically increasing original-end order.</summary>
    private readonly ScriptOffsetSegment[] _segments;

    /// <summary>Copies and validates the relocation index in a single linear pass.</summary>
    /// <param name="segments">Contiguous original and rebuilt intervals in physical order.</param>
    /// <exception cref="ToolkitException">The map is empty, overlaps, contains gaps or changes an opaque region's length.</exception>
    internal ScriptOffsetMap(IReadOnlyList<ScriptOffsetSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        if (segments.Count == 0)
            throw new ToolkitException("SCRIPT_OFFSET_MAP", "The relocation map cannot be empty.");
        _segments = new ScriptOffsetSegment[segments.Count];
        for (int i = 0; i < segments.Count; i++)
        {
            ScriptOffsetSegment s = segments[i];
            if (s.OldStart < 0 || s.NewStart < 0 || s.OldEnd < s.OldStart || s.NewEnd < s.NewStart
                || (!s.Changed && s.OldEnd - s.OldStart != s.NewEnd - s.NewStart)
                || (i != 0 && (_segments[i - 1].OldEnd != s.OldStart || _segments[i - 1].NewEnd != s.NewStart)))
                throw new ToolkitException("SCRIPT_OFFSET_MAP", $"Invalid relocation segment {i}.");
            _segments[i] = s;
        }
    }

    /// <summary>Translates an opaque-byte offset or a replacement boundary without scanning all earlier records.</summary>
    /// <param name="oldOffset">Absolute original byte offset, including a final end boundary.</param>
    /// <returns>The unambiguous rebuilt offset.</returns>
    /// <exception cref="ToolkitException">The offset is outside the map or strictly inside changed text.</exception>
    internal int Translate(int oldOffset)
    {
        int lo = 0;
        int hi = _segments.Length;
        // Lower bound on OldEnd preserves the first match even at zero-length insertions.
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (_segments[mid].OldEnd < oldOffset) lo = mid + 1;
            else hi = mid;
        }
        if (lo == _segments.Length || oldOffset < _segments[lo].OldStart)
            throw new ToolkitException("SCRIPT_OFFSET_MAP", $"Cannot relocate original offset 0x{oldOffset:X}.");
        ScriptOffsetSegment s = _segments[lo];
        if (oldOffset == s.OldStart) return s.NewStart;
        if (oldOffset == s.OldEnd) return s.NewEnd;
        if (s.Changed)
            throw new ToolkitException("SCRIPT_JUMP_INSIDE_TEXT", $"Jump 0x{oldOffset:X} points inside changed text.");
        return checked(s.NewStart + oldOffset - s.OldStart);
    }
}
