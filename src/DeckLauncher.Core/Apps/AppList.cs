namespace DeckLauncher.Core.Apps;

/// <summary>Small pure helpers behind the Apps tab: website addresses, names and the order the user chose.</summary>
public static class AppList
{
    /// <summary>"youtube.com" -> "https://youtube.com/". Only http and https addresses with a real host are accepted.</summary>
    public static string? NormalizeUrl(string? text)
    {
        var s = text?.Trim();
        if (string.IsNullOrEmpty(s) || s.Any(char.IsWhiteSpace)) return null;
        if (!s.Contains("://", StringComparison.Ordinal)) s = "https://" + s;
        if (!Uri.TryCreate(s, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return null;
        if (!uri.Host.Contains('.') && !uri.IsLoopback) return null; // "abc" alone is not a site
        return uri.AbsoluteUri;
    }

    /// <summary>"https://www.youtube.com/" -> "youtube.com".</summary>
    public static string HostOf(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return url;
        var host = uri.Host;
        return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
    }

    /// <summary>Name shown when the user leaves it empty: "youtube.com" -> "Youtube".</summary>
    public static string SuggestWebName(string url)
    {
        var host = HostOf(url);
        var first = host.Split('.')[0];
        return first.Length == 0 ? host : char.ToUpperInvariant(first[0]) + first[1..];
    }

    /// <summary>Puts the keys in the saved order; keys that were never ordered keep their place at the end.</summary>
    public static List<string> Order(IEnumerable<string> keys, IEnumerable<string> saved)
    {
        var all = keys.ToList();
        var result = new List<string>();
        foreach (var key in saved)
            if (all.Contains(key, StringComparer.Ordinal) && !result.Contains(key, StringComparer.Ordinal)) result.Add(key);
        result.AddRange(all.Where(k => !result.Contains(k, StringComparer.Ordinal)));
        return result;
    }

    /// <summary>Swaps the item with its neighbour; returns false (list unchanged) at the ends or for an unknown key.</summary>
    public static bool Move(List<string> keys, string key, int delta)
    {
        var index = keys.IndexOf(key);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= keys.Count) return false;
        (keys[index], keys[target]) = (keys[target], keys[index]);
        return true;
    }
}
