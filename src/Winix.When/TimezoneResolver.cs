namespace Winix.When;

/// <summary>
/// Resolves timezone identifiers (IANA or Windows) to <see cref="TimeZoneInfo"/> instances,
/// and provides display helpers for abbreviations and labels.
/// </summary>
public static class TimezoneResolver
{
    /// <summary>
    /// Attempts to resolve a timezone by IANA ID (e.g. <c>Asia/Tokyo</c>) or Windows ID
    /// (e.g. <c>Tokyo Standard Time</c>). On .NET 6+ with ICU enabled both forms are accepted
    /// by <see cref="TimeZoneInfo.FindSystemTimeZoneById"/>.
    /// </summary>
    /// <param name="id">The timezone identifier to look up.</param>
    /// <param name="zone">Receives the resolved <see cref="TimeZoneInfo"/> on success, or <see langword="null"/> on failure.</param>
    /// <param name="error">Receives a human-readable error message on failure, or <see langword="null"/> on success.</param>
    /// <returns><see langword="true"/> if the timezone was resolved successfully; otherwise <see langword="false"/>.</returns>
    public static bool TryResolve(string id, out TimeZoneInfo? zone, out string? error)
    {
        zone = null;
        error = null;

        if (string.IsNullOrWhiteSpace(id))
        {
            error = "Timezone ID cannot be empty.";
            return false;
        }

        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            error = $"Unknown timezone '{id}'. Use an IANA ID (e.g. Asia/Tokyo) or Windows ID (e.g. Tokyo Standard Time).";
            return false;
        }
        catch (InvalidTimeZoneException ex)
        {
            error = $"Invalid timezone data for '{id}': {ex.Message}";
            return false;
        }
    }

    // On Windows with ICU, StandardName/DaylightName return Windows display names like
    // "Tokyo Standard Time" rather than the IANA abbreviations (JST, EST, etc.). This
    // table maps IANA IDs to their canonical abbreviations so display output is correct.
    // Fallback to initials extraction handles any ID not listed here.
    private static readonly System.Collections.Generic.Dictionary<string, (string Std, string Dst)> s_ianaAbbreviations
        = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Africa/Abidjan"]          = ("GMT",   "GMT"),
            ["Africa/Cairo"]            = ("EET",   "EEST"),
            ["Africa/Johannesburg"]     = ("SAST",  "SAST"),
            ["Africa/Nairobi"]          = ("EAT",   "EAT"),
            ["America/Anchorage"]       = ("AKST",  "AKDT"),
            ["America/Chicago"]         = ("CST",   "CDT"),
            ["America/Denver"]          = ("MST",   "MDT"),
            ["America/Los_Angeles"]     = ("PST",   "PDT"),
            ["America/New_York"]        = ("EST",   "EDT"),
            ["America/Phoenix"]         = ("MST",   "MST"),
            ["America/Sao_Paulo"]       = ("BRT",   "BRST"),
            ["America/Toronto"]         = ("EST",   "EDT"),
            ["America/Vancouver"]       = ("PST",   "PDT"),
            ["Asia/Bangkok"]            = ("ICT",   "ICT"),
            ["Asia/Colombo"]            = ("IST",   "IST"),
            ["Asia/Dubai"]              = ("GST",   "GST"),
            ["Asia/Ho_Chi_Minh"]        = ("ICT",   "ICT"),
            ["Asia/Hong_Kong"]          = ("HKT",   "HKT"),
            ["Asia/Jakarta"]            = ("WIB",   "WIB"),
            ["Asia/Jerusalem"]          = ("IST",   "IDT"),
            ["Asia/Karachi"]            = ("PKT",   "PKT"),
            ["Asia/Kolkata"]            = ("IST",   "IST"),
            ["Asia/Kuala_Lumpur"]       = ("MYT",   "MYT"),
            ["Asia/Riyadh"]             = ("AST",   "AST"),
            ["Asia/Seoul"]              = ("KST",   "KST"),
            ["Asia/Shanghai"]           = ("CST",   "CST"),
            ["Asia/Singapore"]          = ("SGT",   "SGT"),
            ["Asia/Taipei"]             = ("CST",   "CST"),
            ["Asia/Tehran"]             = ("IRST",  "IRDT"),
            ["Asia/Tokyo"]              = ("JST",   "JST"),
            ["Asia/Vladivostok"]        = ("VLAT",  "VLAT"),
            ["Atlantic/Azores"]         = ("AZOT",  "AZOST"),
            ["Australia/Adelaide"]      = ("ACST",  "ACDT"),
            ["Australia/Brisbane"]      = ("AEST",  "AEST"),
            ["Australia/Melbourne"]     = ("AEST",  "AEDT"),
            ["Australia/Perth"]         = ("AWST",  "AWST"),
            ["Australia/Sydney"]        = ("AEST",  "AEDT"),
            ["Europe/Amsterdam"]        = ("CET",   "CEST"),
            ["Europe/Athens"]           = ("EET",   "EEST"),
            ["Europe/Berlin"]           = ("CET",   "CEST"),
            ["Europe/Brussels"]         = ("CET",   "CEST"),
            ["Europe/Bucharest"]        = ("EET",   "EEST"),
            ["Europe/Dublin"]           = ("GMT",   "IST"),
            ["Europe/Helsinki"]         = ("EET",   "EEST"),
            ["Europe/Istanbul"]         = ("TRT",   "TRT"),
            ["Europe/Lisbon"]           = ("WET",   "WEST"),
            ["Europe/London"]           = ("GMT",   "BST"),
            ["Europe/Madrid"]           = ("CET",   "CEST"),
            ["Europe/Moscow"]           = ("MSK",   "MSK"),
            ["Europe/Paris"]            = ("CET",   "CEST"),
            ["Europe/Prague"]           = ("CET",   "CEST"),
            ["Europe/Rome"]             = ("CET",   "CEST"),
            ["Europe/Stockholm"]        = ("CET",   "CEST"),
            ["Europe/Warsaw"]           = ("CET",   "CEST"),
            ["Europe/Zurich"]           = ("CET",   "CEST"),
            ["Pacific/Auckland"]        = ("NZST",  "NZDT"),
            ["Pacific/Honolulu"]        = ("HST",   "HST"),
        };

    /// <summary>
    /// Returns the timezone abbreviation (e.g. <c>JST</c>, <c>NZST</c>) for the given point in time,
    /// respecting DST. Checks a built-in IANA abbreviation table first; if the zone ID is not listed,
    /// falls back to extracting uppercase initials from the .NET display name. If the display name is
    /// already short (≤5 chars, no spaces) it is returned as-is.
    /// </summary>
    /// <param name="zone">The timezone to abbreviate.</param>
    /// <param name="timestamp">The point in time used to determine whether DST is in effect.</param>
    /// <returns>A short abbreviation string.</returns>
    public static string GetAbbreviation(TimeZoneInfo zone, DateTimeOffset timestamp)
    {
        bool isDst = zone.IsDaylightSavingTime(timestamp);

        // Prefer the curated IANA abbreviation table — .NET's StandardName/DaylightName on Windows
        // returns Windows display names (e.g. "Tokyo Standard Time") rather than IANA abbreviations.
        if (s_ianaAbbreviations.TryGetValue(zone.Id, out var abbrs))
        {
            return isDst ? abbrs.Dst : abbrs.Std;
        }

        string fullName = isDst ? zone.DaylightName : zone.StandardName;

        if (fullName.Length <= 5 && !fullName.Contains(' '))
        {
            return fullName;
        }

        var sb = new System.Text.StringBuilder(6);
        foreach (char c in fullName)
        {
            if (char.IsUpper(c))
            {
                sb.Append(c);
            }
        }

        return sb.Length > 0 ? sb.ToString() : fullName;
    }

    /// <summary>
    /// Returns a human-readable display label for a timezone. For IANA IDs the city portion
    /// (after the last <c>/</c>) is returned with underscores replaced by spaces. For UTC and
    /// non-IANA IDs the raw ID is returned, with <c>UTC</c> normalised to uppercase.
    /// </summary>
    /// <param name="zone">The timezone to label.</param>
    /// <returns>A short, readable label suitable for column headers or output lines.</returns>
    public static string GetDisplayLabel(TimeZoneInfo zone)
    {
        string id = zone.Id;

        int lastSlash = id.LastIndexOf('/');
        if (lastSlash >= 0 && lastSlash < id.Length - 1)
        {
            return id.Substring(lastSlash + 1).Replace('_', ' ');
        }

        if (zone.Equals(TimeZoneInfo.Utc) || id.Equals("UTC", StringComparison.OrdinalIgnoreCase))
        {
            return "UTC";
        }

        return id;
    }
}
