namespace Game.Server.Tests;

internal static class TestChecksum
{
    public static string NormalizeFullHex(string checksum)
    {
        if (string.IsNullOrWhiteSpace(checksum))
        {
            throw new ArgumentException("Checksum cannot be null or empty.", nameof(checksum));
        }

        string normalized = checksum.Trim().ToLowerInvariant();
        if (normalized.Contains("...", StringComparison.Ordinal))
        {
            throw new ArgumentException("Checksum must be full hex, not truncated.", nameof(checksum));
        }

        if (normalized.Length != 64)
        {
            throw new ArgumentException($"Checksum must be 64 hex chars, got {normalized.Length}.", nameof(checksum));
        }

        return normalized;
    }
}
