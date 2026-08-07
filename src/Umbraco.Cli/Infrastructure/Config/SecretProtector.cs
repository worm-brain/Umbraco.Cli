using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Umbraco.Cli.Infrastructure.Config;

/// <summary>
/// Protects the stored client secret at rest (issue #45). On Windows the secret is
/// encrypted with DPAPI under the current user, so it is not readable as plaintext by
/// other users or off a copied config file. On other platforms encryption is not applied
/// (there is no dependency-free equivalent), and <see cref="ConfigStore"/> restricts the
/// file permissions to the owner instead.
///
/// Stored values are prefixed with a scheme tag (<c>dpapi:</c>) so <see cref="Unprotect"/>
/// can tell an encrypted value from a legacy plaintext one and stays backward compatible
/// with config files written before this change.
/// </summary>
internal static class SecretProtector
{
    /// <summary>Prefix marking a DPAPI-encrypted, base64-encoded value.</summary>
    private const string DpapiPrefix = "dpapi:";

    /// <summary>
    /// Whether a stored value is already in an encrypted (non-plaintext) form. Used to
    /// detect legacy plaintext config files that should be migrated (issue #45).
    /// </summary>
    /// <param name="stored">The value read from the config file.</param>
    /// <returns>True if the value carries a recognised encryption prefix.</returns>
    public static bool IsProtected(string? stored) =>
        !string.IsNullOrEmpty(stored) && stored.StartsWith(DpapiPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Whether protection would actually transform a plaintext value on this platform
    /// (i.e. encryption is available). False on non-Windows, where <see cref="Protect"/>
    /// returns the plaintext unchanged and file permissions provide protection instead.
    /// </summary>
    public static bool CanEncrypt => OperatingSystem.IsWindows();

    /// <summary>
    /// Encrypts a secret for storage. On Windows returns a <c>dpapi:</c>-prefixed base64
    /// ciphertext; on other platforms returns the plaintext unchanged (file permissions
    /// provide the protection there).
    /// </summary>
    /// <param name="plaintext">The secret to protect. Null/empty is returned as-is.</param>
    /// <returns>The value to persist.</returns>
    public static string? Protect(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
            return plaintext;

        // Guard against double-encryption: an already-protected value is returned as-is.
        if (IsProtected(plaintext))
            return plaintext;

        if (OperatingSystem.IsWindows())
            return DpapiPrefix + Convert.ToBase64String(ProtectWindows(plaintext));

        return plaintext;
    }

    /// <summary>
    /// Reverses <see cref="Protect"/>. A <c>dpapi:</c>-prefixed value is decrypted (on
    /// Windows); any other value is treated as legacy plaintext and returned unchanged.
    /// A <c>dpapi:</c> value that cannot be decrypted here yields null (a missing secret)
    /// rather than the raw ciphertext.
    /// </summary>
    /// <param name="stored">The value read from the config file.</param>
    /// <returns>
    /// The plaintext secret, or null when a <c>dpapi:</c> value cannot be decrypted on this
    /// platform.
    /// </returns>
    public static string? Unprotect(string? stored)
    {
        if (
            string.IsNullOrEmpty(stored)
            || !stored.StartsWith(DpapiPrefix, StringComparison.Ordinal)
        )
            return stored; // legacy plaintext or empty

        // A dpapi: blob is Windows-only ciphertext. Off Windows it can never be decrypted --
        // this happens whenever a config file written on Windows is copied to macOS/Linux
        // (a shared dotfiles repo, a synced home directory). Returning `stored` here would
        // hand the literal "dpapi:<base64>" string out as the secret, which then gets sent
        // as the OAuth client secret and fails authentication with a baffling error. Report
        // it as missing instead, so the caller takes the normal "not authenticated" path.
        // ConfigStore.Load relies on this: it only catches a *throwing* Unprotect, which is
        // how the equivalent Windows case (an undecryptable blob from another user/machine)
        // already degrades.
        if (!OperatingSystem.IsWindows())
            return null;

        var cipher = Convert.FromBase64String(stored[DpapiPrefix.Length..]);
        return UnprotectWindows(cipher);
    }

    /// <summary>Encrypts under the current Windows user via DPAPI.</summary>
    /// <param name="plaintext">The secret.</param>
    /// <returns>The ciphertext bytes.</returns>
    [SupportedOSPlatform("windows")]
    private static byte[] ProtectWindows(string plaintext) =>
        ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plaintext),
            optionalEntropy: null,
            DataProtectionScope.CurrentUser
        );

    /// <summary>Decrypts DPAPI ciphertext produced by <see cref="ProtectWindows"/>.</summary>
    /// <param name="cipher">The ciphertext bytes.</param>
    /// <returns>The plaintext secret.</returns>
    [SupportedOSPlatform("windows")]
    private static string UnprotectWindows(byte[] cipher) =>
        Encoding.UTF8.GetString(
            ProtectedData.Unprotect(cipher, optionalEntropy: null, DataProtectionScope.CurrentUser)
        );
}
