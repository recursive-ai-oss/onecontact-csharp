namespace RecursiveAI.OneContact.Keys;

/// <summary>Helpers around <see cref="KeyVersion"/>.</summary>
public static class KeyVersions
{
    /// <summary>Gets the newest key version, the one <c>Key</c> properties use.</summary>
    public static KeyVersion Latest => KeyVersion.V1;

    /// <summary>Throws when <paramref name="version"/> is not a defined version.</summary>
    /// <param name="version">The version to check.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is not a defined <see cref="KeyVersion"/>.</exception>
    public static void ThrowIfUndefined(KeyVersion version)
    {
        if (version != KeyVersion.V1)
        {
            throw new ArgumentOutOfRangeException(nameof(version), version, "Unknown key version.");
        }
    }
}
