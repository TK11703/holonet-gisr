using System.Security.Cryptography;

namespace Gisr.Api.Records;

// PBKDF2-SHA256 as "pbkdf2-sha256$iterations$salt$hash" (base64). The code itself is never stored or logged.
public static class OverrideCodeHasher
{
    public const string Algorithm = "pbkdf2-sha256";
    public const int Iterations = 600_000;
    public const int SaltSize = 16;
    public const int HashSize = 32;

    // Stored values come from this class; the bound only stops a corrupted row from stalling a request.
    private const int MaxIterations = 10_000_000;

    public static string Hash(string code) => Hash(code, Iterations);

    public static string Hash(string code, int iterations)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(code, salt, iterations, HashAlgorithmName.SHA256, HashSize);
        return $"{Algorithm}${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string code, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4
            || parts[0] != Algorithm
            || !int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var iterations)
            || iterations is < 1 or > MaxIterations)
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length == 0 || expected.Length == 0)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(code, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
