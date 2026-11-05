namespace RecursiveAI.OneContact.Web;

/// <summary>What a parsed <see cref="WebAddress"/> keeps besides its canonical text: the components it was built from.</summary>
internal sealed record WebParts(
    string Scheme,
    string Host,
    HostKind HostKind,
    int Port,
    string Path,
    string? OpaquePath,
    string? Query,
    string? PublicSuffix,
    string? RegistrableDomain);
