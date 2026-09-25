using System.Security.Cryptography;

namespace Umbraco.Cli.Infrastructure;

/// <summary>
/// Generates a random password that satisfies Umbraco's default member/user password complexity
/// policy: a minimum length plus at least one lowercase letter, one uppercase letter, one digit,
/// and one non-alphanumeric character. Used when <c>member create</c> is not given an explicit
/// <c>--password</c>, so a member can be provisioned non-interactively (#136) instead of failing
/// with "The password did not meet the complexity requirements." The generated value is not
/// returned by the API, so pass <c>--password</c> to set a password you need to know.
/// </summary>
internal static class PasswordGenerator
{
    // Ambiguous characters (l/1/I, o/0/O) are omitted so a generated password is easier to
    // transcribe if it ever is surfaced.
    private const string Lower = "abcdefghijkmnpqrstuvwxyz";
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Digits = "23456789";
    private const string Symbols = "!@#$%^&*-_=+?";
    private const string All = Lower + Upper + Digits + Symbols;

    /// <summary>
    /// Generates a policy-compliant random password using a cryptographic RNG.
    /// </summary>
    /// <param name="length">Total length; values below 12 are raised to 12.</param>
    /// <returns>A password containing at least one character from each required class.</returns>
    public static string Generate(int length = 16)
    {
        if (length < 12)
            length = 12;

        // Seed one character of each required class, fill the remainder from the full set, then
        // shuffle so the guaranteed characters are not always in the first four positions.
        var chars = new char[length];
        chars[0] = Pick(Lower);
        chars[1] = Pick(Upper);
        chars[2] = Pick(Digits);
        chars[3] = Pick(Symbols);
        for (var i = 4; i < length; i++)
            chars[i] = Pick(All);
        Shuffle(chars);
        return new string(chars);
    }

    /// <summary>Picks one random character from a set with a cryptographic RNG.</summary>
    /// <param name="set">The characters to choose from.</param>
    /// <returns>One randomly chosen character.</returns>
    private static char Pick(string set) => set[RandomNumberGenerator.GetInt32(set.Length)];

    /// <summary>Fisher-Yates shuffle backed by a cryptographic RNG.</summary>
    /// <param name="chars">The buffer to shuffle in place.</param>
    private static void Shuffle(char[] chars)
    {
        for (var i = chars.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
    }
}
