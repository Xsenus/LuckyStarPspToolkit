namespace LuckyStarPspToolkit.Formats.Common;

/// <summary>
/// Represents a validated toolkit exception failure with a stable diagnostic code.
/// </summary>
public sealed class ToolkitException : Exception
{
    /// <summary>
    /// Initializes a new instance with validated constructor state.
    /// </summary>
    /// <param name="code">The code value.</param>
    /// <param name="message">The diagnostic message used when validation fails.</param>
    public ToolkitException(string code, string message) : base(message)
    {
        Code = code;
    }

    /// <summary>
    /// Initializes a new instance with validated constructor state.
    /// </summary>
    /// <param name="code">The code value.</param>
    /// <param name="message">The diagnostic message used when validation fails.</param>
    /// <param name="innerException">The exception that caused the current failure.</param>
    public ToolkitException(string code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    /// <summary>The code value used by this model or operation.</summary>
    public string Code { get; }

    /// <summary>
    /// Returns the stable diagnostic string representation.
    /// </summary>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    public override string ToString() => $"{Code}: {Message}";
}
