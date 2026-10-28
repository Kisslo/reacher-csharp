using System.Text;

namespace Reacher.WebsiteAnalysis.Matching;

/// <summary>
/// Scores how well a domain matches a company name. Pure: no options and no network.
/// </summary>
public static class WebsiteScorer
{
    /// <summary>
    /// Returns a score from 0 to 1.
    /// </summary>
    /// <remarks>
    /// Assumption until T2-07 measures it. Normalize by lowercasing, folding å/ä to a and
    /// ö to o, and dropping the words ab, aktiebolag, and publ, plus punctuation and spaces.
    /// The domain counts only when it starts with the company's first remaining word, so a
    /// short accidental prefix such as "by" does not match "Bygg". The base score is the
    /// average of how much of the name the shared prefix covers and how much of the domain
    /// that prefix covers. A shortened name (kvarnbynsror / kvarnbynsrorteknik) stays high
    /// because the domain is fully explained. A name plus an extra city
    /// (byggmaleri / byggmaleristockholm) stays lower because most of the domain is not the
    /// name. When the normalized name appears as whole words in the title,
    /// <paramref name="titleBonus"/> is added and the result is clamped to 1.
    /// </remarks>
    public static double Score(
        string companyName,
        string domainLabel,
        string? title,
        double titleBonus)
    {
        ArgumentNullException.ThrowIfNull(companyName);
        ArgumentNullException.ThrowIfNull(domainLabel);

        var namePhrase = ToPhrase(companyName);
        if (namePhrase.Length == 0)
        {
            return 0;
        }

        var firstWord = namePhrase.Split(' ')[0];
        var compactName = Compact(namePhrase);
        var compactDomain = Compact(ToPhrase(domainLabel));
        if (compactDomain.Length == 0
            || !compactDomain.StartsWith(firstWord, StringComparison.Ordinal))
        {
            return 0;
        }

        var prefix = CommonPrefixLength(compactName, compactDomain);
        var baseScore = ((prefix / (double)compactName.Length)
            + (prefix / (double)compactDomain.Length)) / 2;

        if (titleBonus > 0
            && !string.IsNullOrWhiteSpace(title)
            && ContainsPhrase(ToPhrase(title), namePhrase))
        {
            baseScore = Math.Min(1d, baseScore + titleBonus);
        }

        return baseScore;
    }

    /// <summary>
    /// Registrable label: strip a leading www, drop the TLD, and keep the rightmost
    /// remaining label. <c>www.kvarnbynsror.se</c> and <c>shop.kvarnbynsror.se</c> both
    /// become <c>kvarnbynsror</c>. A multi-part TLD such as <c>.co.uk</c> is not special-cased.
    /// </summary>
    public static string ExtractLabel(string host)
    {
        ArgumentNullException.ThrowIfNull(host);

        var normalized = host.Trim().TrimEnd('.').ToLowerInvariant();
        if (normalized.StartsWith("www.", StringComparison.Ordinal))
        {
            normalized = normalized["www.".Length..];
        }

        var labels = normalized.Split(
            '.',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (labels.Length == 0)
        {
            return string.Empty;
        }

        if (labels.Length == 1)
        {
            return labels[0];
        }

        return labels[^2];
    }

    private static string ToPhrase(string value)
    {
        var buffer = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsPunctuation(character) || char.IsWhiteSpace(character))
            {
                buffer.Append(' ');
                continue;
            }

            buffer.Append(Fold(char.ToLowerInvariant(character)));
        }

        var words = buffer.ToString().Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var kept = new List<string>(words.Length);
        foreach (var word in words)
        {
            if (word is "ab" or "aktiebolag" or "publ")
            {
                continue;
            }

            kept.Add(word);
        }

        return string.Join(' ', kept);
    }

    private static string Compact(string phrase) =>
        phrase.Replace(" ", string.Empty, StringComparison.Ordinal);

    private static int CommonPrefixLength(string left, string right)
    {
        var length = Math.Min(left.Length, right.Length);
        var index = 0;
        while (index < length && left[index] == right[index])
        {
            index++;
        }

        return index;
    }

    private static bool ContainsPhrase(string titlePhrase, string namePhrase)
    {
        if (titlePhrase.Length == 0 || namePhrase.Length == 0)
        {
            return false;
        }

        var titleWords = titlePhrase.Split(' ');
        var nameWords = namePhrase.Split(' ');
        if (titleWords.Length < nameWords.Length)
        {
            return false;
        }

        for (var start = 0; start <= titleWords.Length - nameWords.Length; start++)
        {
            var matches = true;
            for (var index = 0; index < nameWords.Length; index++)
            {
                if (!string.Equals(
                        titleWords[start + index],
                        nameWords[index],
                        StringComparison.Ordinal))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return true;
            }
        }

        return false;
    }

    private static char Fold(char value) => value switch
    {
        'å' or 'ä' => 'a',
        'ö' => 'o',
        _ => value,
    };
}
