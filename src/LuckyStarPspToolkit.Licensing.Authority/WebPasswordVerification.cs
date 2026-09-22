using System.Security.Cryptography;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("LuckyStarPspToolkit.Licensing.SelfTests")]

namespace LuckyStarPspToolkit.Licensing.Authority;

/// <summary>Production owner password verification; independent of mutable MFA/session state and never cached as a permission.</summary>
internal static class WebPasswordVerification
{
    /// <summary>Derives the full PBKDF2-SHA256 value and compares it in constant time; temporary key bytes are always cleared.</summary>
    /// <param name="password">Bounded submitted password.</param>
    /// <param name="salt">Validated 32-byte private account salt.</param>
    /// <param name="expected">Validated 32-byte stored hash.</param>
    /// <returns>True only for a matching derived value; this alone never authorizes a session without MFA.</returns>
    internal static bool Verify(string password, byte[] salt, byte[] expected)
    {
        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, 600000, HashAlgorithmName.SHA256, 32);
        try { return CryptographicOperations.FixedTimeEquals(actual, expected); }
        finally { CryptographicOperations.ZeroMemory(actual); }
    }
}
