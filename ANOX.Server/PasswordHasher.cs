using System;
using System.Security.Cryptography;
using System.Text;

namespace ANOX.Server;

/// <summary>
/// PBKDF2-SHA256 password hashes in the form "pbkdf2-sha256$iterations$salt$hash" (salt and hash in base64).
/// Only the hash is stored in server.json, never the password.
/// </summary>
public static class PasswordHasher
{
    private const string Prefix = "pbkdf2-sha256";
    private const int Iterations = 210_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public static string Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, HashSize);

        return $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string storedHash)
    {
        string[] parts = storedHash.Split('$');

        if (parts.Length != 4 || parts[0] != Prefix || !int.TryParse(parts[1], out int iterations) || iterations <= 0)
            return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, expected.Length);

        // Constant-time comparison: doesn't reveal how many bytes matched.
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>True if the stored value looks like a hash this class can verify.</summary>
    public static bool IsValidHash(string storedHash) =>
        storedHash.Split('$') is { Length: 4 } parts && parts[0] == Prefix;
}
