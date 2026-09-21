namespace LuckyStarPspToolkit.Formats.Common;

/// <summary>
/// Represents the toolkit's guard model or service.
/// </summary>
internal static class Guard
{
    /// <summary>
    /// Validates a nonnegative offset and length using subtraction to avoid overflow in a bounds check.
    /// </summary>
    /// <param name="condition">The condition that must be true.</param>
    /// <param name="code">The code value.</param>
    /// <param name="message">The diagnostic message used when validation fails.</param>
    public static void Range(bool condition, string code, string message)
    {
        if (!condition)
        {
            throw new ToolkitException(code, message);
        }
    }

    /// <summary>
    /// Checks whether a numeric value fits an Int32 and reports a caller-specific validation error otherwise.
    /// </summary>
    /// <param name="value">The value to process.</param>
    /// <param name="code">The code value.</param>
    /// <param name="name">The logical name used for lookup or diagnostics.</param>
    /// <returns>The validated operation result.</returns>
    public static int CheckedInt(long value, string code, string name)
    {
        if (value is < 0 or > int.MaxValue)
        {
            throw new ToolkitException(code, $"{name} does not fit into Int32: {value}.");
        }

        return (int)value;
    }

    /// <summary>
    /// Checks whether a numeric value fits a UInt16 and reports a caller-specific validation error otherwise.
    /// </summary>
    /// <param name="value">The value to process.</param>
    /// <param name="code">The code value.</param>
    /// <param name="name">The logical name used for lookup or diagnostics.</param>
    /// <returns>The validated operation result.</returns>
    public static ushort CheckedUInt16(long value, string code, string name)
    {
        if (value is < 0 or > ushort.MaxValue)
        {
            throw new ToolkitException(code, $"{name} does not fit into UInt16: {value}.");
        }

        return (ushort)value;
    }

    /// <summary>
    /// Checks whether a numeric value fits a UInt32 and reports a caller-specific validation error otherwise.
    /// </summary>
    /// <param name="value">The value to process.</param>
    /// <param name="code">The code value.</param>
    /// <param name="name">The logical name used for lookup or diagnostics.</param>
    /// <returns>The validated operation result.</returns>
    public static uint CheckedUInt32(long value, string code, string name)
    {
        if (value is < 0 or > uint.MaxValue)
        {
            throw new ToolkitException(code, $"{name} does not fit into UInt32: {value}.");
        }

        return (uint)value;
    }
}
