using System.Text.RegularExpressions;

namespace SilverScreen.Core.Player.Comments;

/// <summary>
///     Determines whether a YouTube comment likely needs translation into the configured host language (<c>hl</c>).
/// </summary>
public static partial class CommentTranslationDetector
{
    private static readonly HashSet<string> NonLatinLanguages =
    [
        with(StringComparer.OrdinalIgnoreCase),
        "ar", "be", "bg", "bn", "el", "fa", "gu", "he", "hi", "hy", "iw",
        "ja", "ka", "kk", "km", "kn", "ko", "ky", "lo", "mk", "ml", "mn",
        "mr", "my", "ne", "or", "pa", "ru", "si", "sr", "ta", "te", "tg",
        "th", "ti", "uk", "ur", "yi", "zh"
    ];

    private static readonly Dictionary<string, HashSet<string>> LatinStopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] =
        [
            with(StringComparer.OrdinalIgnoreCase),
            "the", "and", "that", "have", "for", "not", "with", "you", "this", "but",
            "his", "from", "they", "she", "will", "would", "there", "their", "what", "about",
            "which", "when", "make", "like", "just", "know", "people", "into", "your", "some",
            "could", "them", "than", "then", "now", "only", "come", "its", "over", "also",
            "was", "were", "are", "been", "being", "who", "here", "how", "why"
        ],
        ["es"] =
        [
            with(StringComparer.OrdinalIgnoreCase),
            "que", "los", "las", "una", "uno", "por", "para", "con", "como", "pero",
            "más", "mas", "este", "esta", "esto", "estos", "estas", "todo", "todos", "toda",
            "muy", "sin", "sobre", "entre", "cuando", "donde", "quien", "quién", "desde", "hasta",
            "hola", "gracias", "bueno", "buena", "alguien", "aqui", "aquí", "ahora", "siempre", "nunca",
            "cancion", "canción", "año", "años", "hace", "soy", "eres", "somos", "tiene", "tienen"
        ],
        ["fr"] =
        [
            with(StringComparer.OrdinalIgnoreCase),
            "les", "des", "une", "que", "qui", "dans", "pour", "pas", "sur", "avec",
            "plus", "tout", "tous", "mais", "comme", "cette", "vous", "nous", "ils", "elles",
            "sont", "être", "avoir", "fait", "bien", "très", "merci", "bonjour", "salut", "toujours",
            "jamais", "quand", "où", "chez", "depuis", "aussi", "même", "encore", "alors", "donc"
        ],
        ["de"] =
        [
            with(StringComparer.OrdinalIgnoreCase),
            "der", "die", "das", "und", "ist", "ich", "nicht", "sie", "ein", "eine",
            "mit", "auf", "für", "von", "den", "des", "dem", "sich", "auch", "noch",
            "wie", "aber", "wenn", "oder", "wir", "ihr", "haben", "wird", "sind", "war",
            "danke", "hallo", "sehr", "gut", "immer", "schon", "jetzt", "hier", "warum", "wer"
        ],
        ["pt"] =
        [
            with(StringComparer.OrdinalIgnoreCase),
            "que", "não", "nao", "uma", "para", "com", "por", "mais", "como", "mas",
            "muito", "isso", "esse", "essa", "este", "esta", "tudo", "todos", "todas", "você",
            "voce", "obrigado", "obrigada", "olá", "ola", "aqui", "agora", "sempre", "nunca", "quando",
            "quem", "onde", "porque", "anos", "música", "musica", "alguém", "alguem", "bom", "boa"
        ],
        ["it"] =
        [
            with(StringComparer.OrdinalIgnoreCase),
            "che", "non", "una", "per", "con", "sono", "come", "questo", "questa", "quello",
            "quella", "tutto", "tutti", "molto", "anche", "quando", "dove", "chi", "grazie", "ciao",
            "sempre", "mai", "adesso", "ora", "qui", "bella", "bello", "canzone", "anni", "bene"
        ]
    };

    public static bool NeedsTranslation(string? text, string? targetLanguage)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var cleaned = StripNonLinguisticTokens(text);
        var letterCount = 0;
        var hasNonLatinLetter = false;
        var hasLatinLetter = false;
        var hasNonAsciiLatinDiacritic = false;

        foreach (var ch in cleaned)
        {
            if (!char.IsLetter(ch))
                continue;

            letterCount++;
            if (ch <= 0x024F)
            {
                hasLatinLetter = true;
                if (ch > 0x007F)
                    hasNonAsciiLatinDiacritic = true;
            }
            else
            {
                hasNonLatinLetter = true;
            }
        }

        if (letterCount < 2)
            return false;

        var targetBase = NormalizeBaseLanguage(targetLanguage);
        if (NonLatinLanguages.Contains(targetBase))
        {
            if (hasLatinLetter)
                return true;

            return !MatchesExpectedNonLatinScript(cleaned, targetBase);
        }

        if (hasNonLatinLetter)
            return true;

        if (!hasLatinLetter)
            return false;

        if (string.Equals(targetBase, "en", StringComparison.OrdinalIgnoreCase) && hasNonAsciiLatinDiacritic)
            return true;

        return MatchesForeignLatinStopwords(cleaned, targetBase);
    }

    internal static string NormalizeBaseLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return "en";

        var trimmed = language.Trim();
        var sepIndex = trimmed.IndexOfAny(['-', '_']);
        return (sepIndex > 0 ? trimmed[..sepIndex] : trimmed).ToLowerInvariant();
    }

    private static string StripNonLinguisticTokens(string text)
    {
        var withoutUrls = UrlRegex().Replace(text, " ");
        var withoutMentions = MentionRegex().Replace(withoutUrls, " ");
        return TimestampRegex().Replace(withoutMentions, " ");
    }

    private static bool MatchesExpectedNonLatinScript(string text, string targetBase)
    {
        foreach (var ch in text)
        {
            if (!char.IsLetter(ch))
                continue;

            var matches = targetBase switch
            {
                "ja" => IsJapaneseChar(ch),
                "zh" => IsCjkChar(ch),
                "ko" => IsHangulChar(ch),
                "ru" or "uk" or "bg" or "sr" or "be" or "mk" or "kk" or "ky" or "tg" or "mn" => IsCyrillicChar(ch),
                "ar" or "fa" or "ur" => IsArabicChar(ch),
                "he" or "iw" or "yi" => IsHebrewChar(ch),
                "hi" or "mr" or "ne" => IsDevanagariChar(ch),
                "th" => IsThaiChar(ch),
                "el" => IsGreekChar(ch),
                _ => ch > 0x024F
            };

            if (!matches)
                return false;
        }

        return true;
    }

    private static bool MatchesForeignLatinStopwords(string text, string targetBase)
    {
        var words = WordRegex().Matches(text);
        if (words.Count == 0)
            return false;

        var targetHits = 0;
        var bestOtherHits = 0;

        foreach (Match match in words)
        {
            var word = match.Value;
            if (LatinStopwords.TryGetValue(targetBase, out var targetSet) && targetSet.Contains(word))
                targetHits++;

            foreach (var (lang, set) in LatinStopwords)
            {
                if (string.Equals(lang, targetBase, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (set.Contains(word))
                    bestOtherHits++;
            }
        }

        return bestOtherHits > targetHits;
    }

    private static bool IsJapaneseChar(char ch) =>
        ch is (>= '\u3040' and <= '\u30FF') or (>= '\u4E00' and <= '\u9FFF') or (>= '\u3400' and <= '\u4DBF');

    private static bool IsCjkChar(char ch) =>
        ch is (>= '\u4E00' and <= '\u9FFF') or (>= '\u3400' and <= '\u4DBF');

    private static bool IsHangulChar(char ch) =>
        ch is (>= '\uAC00' and <= '\uD7AF') or (>= '\u1100' and <= '\u11FF') or (>= '\u3130' and <= '\u318F');

    private static bool IsCyrillicChar(char ch) => ch is >= '\u0400' and <= '\u04FF';
    private static bool IsArabicChar(char ch) => ch is >= '\u0600' and <= '\u06FF';
    private static bool IsHebrewChar(char ch) => ch is >= '\u0590' and <= '\u05FF';
    private static bool IsDevanagariChar(char ch) => ch is >= '\u0900' and <= '\u097F';
    private static bool IsThaiChar(char ch) => ch is >= '\u0E00' and <= '\u0E7F';
    private static bool IsGreekChar(char ch) => ch is >= '\u0370' and <= '\u03FF';

    [GeneratedRegex(@"https?://\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"@\S+", RegexOptions.CultureInvariant)]
    private static partial Regex MentionRegex();

    [GeneratedRegex(@"\b\d{1,2}(?::\d{2}){1,2}\b", RegexOptions.CultureInvariant)]
    private static partial Regex TimestampRegex();

    [GeneratedRegex(@"[\p{L}']+", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();
}
