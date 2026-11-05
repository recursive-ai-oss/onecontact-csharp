using System.Text;

namespace RecursiveAI.OneContact.Ingest.LibPhoneNumberGeo;

/// <summary>Names and shared helpers of the four sources.</summary>
internal static class GeoSources
{
    internal const string GeocodingName = "libphonenumber-geocoding";
    internal const string CarrierName = "libphonenumber-carrier";
    internal const string TimeZonesName = "libphonenumber-timezones";
    internal const string TerritoriesName = "cldr-territories";

    internal static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Reads a <c>digits|text</c> data file: comment lines (first non-blank character <c>#</c>) and blank lines are skipped.</summary>
    /// <param name="name">The file name, for messages.</param>
    /// <param name="bytes">The file.</param>
    /// <returns>The entries in file order.</returns>
    /// <exception cref="FormatException">A line is neither a comment nor <c>digits|text</c>, a key repeats, or the file is not UTF-8.</exception>
    internal static List<(string Key, string Value)> ReadDataFile(string name, byte[] bytes)
    {
        string text;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException e)
        {
            throw new FormatException($"{name} is not UTF-8.", e);
        }

        List<(string, string)> entries = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        int lineNumber = 0;
        foreach (string raw in text.Split('\n'))
        {
            lineNumber++;
            string line = raw.TrimEnd('\r');
            string trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed[0] == '#')
            {
                continue;
            }

            int bar = line.IndexOf('|', StringComparison.Ordinal);
            if (bar <= 0 || bar == line.Length - 1)
            {
                throw new FormatException($"{name} line {lineNumber}: expected digits|text.");
            }

            string key = line[..bar];
            foreach (char c in key)
            {
                if (c is < '0' or > '9')
                {
                    throw new FormatException($"{name} line {lineNumber}: the key is not digits.");
                }
            }

            if (!seen.Add(key))
            {
                throw new FormatException($"{name} line {lineNumber}: key {key} appears twice.");
            }

            entries.Add((key, line[(bar + 1)..]));
        }

        return entries;
    }

    internal static string ApacheNotice(string what, string folder) =>
        $"libphonenumber {what}\n" +
        "Copyright (C) The Libphonenumber Authors\n\n" +
        "Licensed under the Apache License, Version 2.0 (the \"License\"); you may not use this file except in compliance with the License.\n" +
        "You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0\n\n" +
        $"The data compiled into this package (resources/{folder}/ of https://github.com/google/libphonenumber, release v9.0.43) is distributed\n" +
        "under the Apache License, Version 2.0. The upstream files carry no separate NOTICE file. OneContact contains no libphonenumber code.\n";
}
