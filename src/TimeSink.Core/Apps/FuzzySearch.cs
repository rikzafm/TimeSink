using System.Globalization;
using System.Text;

namespace TimeSink.Core.Apps;

/// <summary>Fuzzy matching for the app search boxes: every word of the query must hit somewhere, as a substring, a typo-tolerant word match or an in-order subsequence.</summary>
public static class FuzzySearch
{
    /// <summary>Match score (higher is better), or null when the query does not match. Names outrank the publisher; a blank query matches everything with 0.</summary>
    public static int? Score(string? query, IEnumerable<string> names, string? publisher)
    {
        var tokens = Fold(query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return 0;
        var nameFields = names.Select(Fold).ToList();
        var pub = publisher is { Length: > 0 } ? Fold(publisher) : null;
        var total = 0;
        foreach (var t in tokens)
        {
            var best = -1;
            foreach (var n in nameFields) best = Math.Max(best, TokenScore(t, n));
            if (pub is not null && TokenScore(t, pub) is >= 0 and var p) best = Math.Max(best, p / 2);
            if (best < 0) return null;
            total += best;
        }
        return total;
    }

    public static int? Score(string? query, string name, string? publisher = null) => Score(query, [name], publisher);

    private static int TokenScore(string token, string field)
    {
        var at = field.IndexOf(token, StringComparison.Ordinal);
        if (at >= 0)
        {
            var wordStart = at == 0 || !char.IsLetterOrDigit(field[at - 1]);
            return (at == 0 ? 1000 : wordStart ? 900 : 700) - Math.Min(field.Length, 99);
        }

        var best = -1;
        if (token.Length >= 4)
        {
            var allowed = token.Length >= 8 ? 2 : 1;
            foreach (var word in field.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var d = Math.Min(Distance(token, word), Distance(token, word[..Math.Min(word.Length, token.Length)]));
                if (d <= allowed) best = Math.Max(best, 500 - d * 50);
            }
        }

        if (token.Length >= 2 && best < 0 && IsInitials(token, field)) best = 400; // "vsc" for Visual Studio Code

        if (token.Length >= 2 && best < 0)
        {
            var span = SubsequenceSpan(token, field);
            if (span > 0 && span <= token.Length * 3) best = 300 - (span - token.Length);
        }
        return best;
    }

    /// <summary>True when the token's letters are the first letters of successive words of the field.</summary>
    private static bool IsInitials(string token, string field)
    {
        var i = 0;
        foreach (var word in field.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (i < token.Length && word[0] == token[i]) i++;
        return i == token.Length;
    }

    /// <summary>Length of the shortest window that holds the token's letters in order (greedy from each start), or 0 when there is none.</summary>
    private static int SubsequenceSpan(string token, string field)
    {
        var best = 0;
        for (var start = field.IndexOf(token[0]); start >= 0; start = field.IndexOf(token[0], start + 1))
        {
            var pos = start;
            var ok = true;
            for (var i = 1; i < token.Length; i++)
            {
                pos = field.IndexOf(token[i], pos + 1);
                if (pos < 0) { ok = false; break; }
            }
            if (!ok) break; // later starts have less text left, so they cannot match either
            var span = pos - start + 1;
            if (best == 0 || span < best) best = span;
        }
        return best;
    }

    /// <summary>Damerau-Levenshtein distance (adjacent swaps count as one edit).</summary>
    private static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        return d[a.Length, b.Length];
    }

    /// <summary>Lower-case with diacritics removed and punctuation turned into spaces.</summary>
    private static string Fold(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
