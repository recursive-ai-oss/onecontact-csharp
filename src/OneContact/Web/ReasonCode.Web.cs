namespace RecursiveAI.OneContact;

public readonly partial struct ReasonCode
{
    /// <summary>The address has no usable host (for example <c>https://</c> with nothing after it).</summary>
    public static ReasonCode WebHostMissing { get; } = new(ContactKind.Web, 1, "host-missing", "a web address needs a host");

    /// <summary>The host is not a valid domain name, IPv4 address or IPv6 address.</summary>
    public static ReasonCode WebInvalidHost { get; } = new(ContactKind.Web, 2, "invalid-host", "the host is not a valid domain name or IP address");

    /// <summary>The port is not a number from 0 to 65535.</summary>
    public static ReasonCode WebInvalidPort { get; } = new(ContactKind.Web, 3, "invalid-port", "the port must be a number from 0 to 65535");

    /// <summary>The text is not a URL by the WHATWG URL Standard's rules.</summary>
    public static ReasonCode WebInvalidUrl { get; } = new(ContactKind.Web, 4, "invalid-url", "the text is not a valid web address");
}
