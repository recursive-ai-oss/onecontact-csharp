using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using RecursiveAI.OneContact.Ingest;

namespace RecursiveAI.OneContact.Tests;

/// <summary>Finds the pinned upstream files the conformance tests read (fetched by tools/fetch) and checks them against their pin.</summary>
internal static class UpstreamFiles
{
    private static string RepositoryRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));

    /// <summary>Returns the full path of a pinned file of <paramref name="source"/>, after verifying its SHA-256 against the pin.</summary>
    internal static string Get(string source, string path)
    {
        string root = RepositoryRoot();
        string cache = Environment.GetEnvironmentVariable("ONECONTACT_UPSTREAM") ?? Path.Combine(root, ".upstream");
        PinFile pin = PinFile.Load(Path.Combine(root, "upstream", source, "PIN"));
        PinnedFile file = pin.Files.Single(f => f.Path == path);
        string full = Path.Combine(cache, source, path);
        Assert.True(File.Exists(full), $"{full} is missing; run tools/fetch/{source}.sh");
        string actual = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(full)));
        Assert.Equal(file.Sha256, actual);
        return full;
    }
}
