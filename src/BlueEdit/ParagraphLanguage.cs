using System.Globalization;

/// <summary>The scripts a paragraph's letters can be in, as far as choosing its language needs them.</summary>
enum Script { Latin, Cyrillic, Greek, Arabic, Hebrew, Han, Kana, Hangul, Devanagari, Thai }

/// <summary>
/// Which language a paragraph is in, so that every checker gets the same answer: among the user's languages, first by
/// the script of its letters (Cyrillic, Latin, Greek...), then, for languages sharing a script (English and German),
/// by fastText's language identification model (<see cref="FastText"/>, <c>lid.176.ftz</c>, built into the exe). A
/// short line (a heading) is not guessed from but takes the document's main language. The same rules as the Obsidian
/// plugin BlueProof (its src/prose/language.ts, detect.ts), measured there: docs/tech/proofing-internals.md, "The
/// language of a paragraph".
/// </summary>
static class ParagraphLanguage
{
    /// <summary>Text with fewer letters than this (a heading, a line of two words) is not guessed from.</summary>
    public const int FewLetters = 25;

    /// <summary>What is guessed among when the user lists no languages: those LibreOffice has dictionaries for, all of
    /// which fastText knows (the plugin measured them against all languages: 95% of paragraphs against 93%).</summary>
    public static readonly string[] GuessAmong =
    [
        "af", "ar", "be", "bg", "bn", "bs", "ca", "cs", "da", "de", "el", "en", "eo", "es", "et", "fa", "fr", "gl", "gu", "he",
        "hi", "hr", "hu", "id", "is", "it", "kn", "ko", "lt", "lv", "mn", "mr", "nb", "ne", "nl", "nn", "pl", "pt", "ro", "ru",
        "sk", "sl", "sq", "sr", "sv", "sw", "ta", "te", "th", "tr", "uk", "vi",
    ];

    static readonly object Gate = new();
    static FastText? _model;
    static bool _loading;

    /// <summary>Whether the model is loaded; until then languages sharing a script are not told apart (the first
    /// listed is taken). <see cref="Load"/> loads it, in the background at start (about 0.1 s).</summary>
    public static bool Ready => Volatile.Read(ref _model) != null;

    /// <summary>Loads the model once (a call while it is loading or loaded does nothing); false when it is not there.</summary>
    public static bool Load()
    {
        lock (Gate)
        {
            if (_model != null) return true;
            if (_loading) return false;
            _loading = true;
        }
        try
        {
            using var s = typeof(FastText).Assembly.GetManifestResourceStream("lid.176.ftz");
            if (s == null) return false;
            Volatile.Write(ref _model, new FastText(s));
            return true;
        }
        catch (InvalidDataException) { return false; }
        catch (EndOfStreamException) { return false; }
    }

    static string Primary(string tag) => tag.Split('-', '_')[0].ToLowerInvariant();

    // The Cyrillic letters each language does not use: a text with one of them is not in it. fastText alone called a
    // short Russian paragraph Bulgarian (the plugin, 2026-10-09), while "ё", "ы" and "э" are not Bulgarian at all.
    static readonly Dictionary<string, string> NotIn = new()
    {
        ["ru"] = "ґєіїјљњћџђѓќѕў",
        ["bg"] = "ёыэґєіїјљњћџђѓќѕў",
        ["uk"] = "ёъыэјљњћџђѓќѕў",
        ["be"] = "ищъґєїјљњћџђѓќѕ",
        ["sr"] = "йщъыьэюяёґєіїѓќѕў",
        ["mk"] = "йщъыьэюяёґєіїђћў",
    };

    // In Russian "ъ" comes only before "е", "ё", "ю", "я" ("объём"); in Bulgarian it is a vowel ("път", "във")
    static readonly System.Text.RegularExpressions.Regex RussianHardSign = new("ъ(?![еёюя])", System.Text.RegularExpressions.RegexOptions.Compiled);

    static bool HasForeignLetter(string lower, string language) =>
        (language == "ru" && RussianHardSign.IsMatch(lower)) || (NotIn.TryGetValue(language, out var foreign) && lower.AsSpan().IndexOfAny(foreign) >= 0);

    /// <summary>Which of <paramref name="candidates"/> (spelled as listed: "de-DE") the text is in by the model, or null
    /// when it cannot tell: the model is not loaded, the text has no letters, or the model names none of them. A
    /// candidate whose alphabet lacks a letter of the text is left out first, unless that leaves none.</summary>
    public static string? DetectAmong(string text, IReadOnlyList<string> candidates)
    {
        var lower = text.ToLowerInvariant();
        var fitting = candidates.Where(c => !HasForeignLetter(lower, Primary(c))).ToList();
        if (fitting.Count > 0 && fitting.Count < candidates.Count) candidates = fitting;
        if (Volatile.Read(ref _model) is not { } model || candidates.Count == 0 || !text.Any(char.IsLetter)) return null;
        // one line: fastText reads up to the end of a line
        var line = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        foreach (var (label, _) in model.Predict(line))
            if (candidates.FirstOrDefault(c => Primary(c) == label) is { } found) return found;
        return null;
    }

    /// <summary>The script of a letter, or null for anything else (digits, signs, letters of scripts not listed).</summary>
    public static Script? ScriptOfLetter(char c) => !char.IsLetter(c) ? null : c switch
    {
        <= 'ɏ' or >= 'Ḁ' and <= 'ỿ' => Script.Latin,
        >= 'Ͱ' and <= 'Ͽ' or >= 'ἀ' and <= '῿' => Script.Greek,
        >= 'Ѐ' and <= 'ԯ' or >= 'ᲀ' and <= '᲏' or >= 'ⷠ' and <= 'ⷿ' or >= 'Ꙁ' and <= 'ꚟ' => Script.Cyrillic,
        >= '֐' and <= '׿' or >= 'יִ' and <= 'ﭏ' => Script.Hebrew,
        >= '؀' and <= 'ۿ' or >= 'ݐ' and <= 'ݿ' or >= 'ࢠ' and <= 'ࣿ' or >= 'ﭐ' and <= '﷿' or >= 'ﹰ' and <= '﻿' => Script.Arabic,
        >= 'ऀ' and <= 'ॿ' => Script.Devanagari,
        >= '฀' and <= '๿' => Script.Thai,
        >= '぀' and <= 'ヿ' or >= 'ㇰ' and <= 'ㇿ' or >= 'ｦ' and <= 'ﾟ' => Script.Kana,
        >= 'ᄀ' and <= 'ᇿ' or >= '㄰' and <= '㆏' or >= '가' and <= '힯' => Script.Hangul,
        >= '㐀' and <= '䶿' or >= '一' and <= '鿿' or >= '豈' and <= '﫿' => Script.Han,
        _ => null,
    };

    /// <summary>The script most of the text's letters are in, or null when it has none. Ties go to the first in
    /// <see cref="Script"/>'s order.</summary>
    public static Script? DominantScript(string text)
    {
        Span<int> counts = stackalloc int[Enum.GetValues<Script>().Length];
        foreach (char c in text)
            if (ScriptOfLetter(c) is { } s) counts[(int)s]++;
        int best = -1;
        for (int i = 0; i < counts.Length; i++)
            if (counts[i] > 0 && (best < 0 || counts[i] > counts[best])) best = i;
        return best < 0 ? null : (Script)best;
    }

    static readonly HashSet<string> CyrillicLanguages = ["ru", "uk", "be", "bg", "sr", "mk", "kk", "ky", "tg", "mn", "tt", "ba", "cv", "ce", "os"];
    static readonly Dictionary<string, Script> OtherScripts = new()
    {
        ["el"] = Script.Greek, ["ar"] = Script.Arabic, ["fa"] = Script.Arabic, ["ur"] = Script.Arabic, ["he"] = Script.Hebrew,
        ["zh"] = Script.Han, ["ja"] = Script.Kana, ["ko"] = Script.Hangul, ["hi"] = Script.Devanagari,
        ["mr"] = Script.Devanagari, ["ne"] = Script.Devanagari, ["th"] = Script.Thai,
    };

    /// <summary>The script a language is written in, by its tag; anything not listed is Latin. A language written in
    /// two scripts is listed under its usual one: the user can name the variant ("sr-Latn").</summary>
    public static Script ScriptOf(string language)
    {
        var parts = language.ToLowerInvariant().Split('-', '_');
        if (parts.Contains("latn")) return Script.Latin;
        if (parts.Contains("cyrl")) return Script.Cyrillic;
        if (CyrillicLanguages.Contains(parts[0])) return Script.Cyrillic;
        return OtherScripts.TryGetValue(parts[0], out var s) ? s : Script.Latin;
    }

    static int Letters(string text) => text.Count(char.IsLetter);

    /// <summary>
    /// The language to check <paramref name="text"/> as (its formulas blanked out, or their variables would count as
    /// Latin letters). <paramref name="candidates"/> are the user's languages (empty: guess); <paramref name="document"/>
    /// the languages of the document the text is in, most used first, when known.
    /// <list type="bullet">
    /// <item>none listed: the document's languages stand for the list; with none known, the model among
    /// <see cref="GuessAmong"/> written in the text's script, answering a bare code ("de");</item>
    /// <item>one: that one, unless the text is in another script (Russian text with only English listed): then guessed;</item>
    /// <item>several: those written in the text's script; one left is the answer; of several (English and German) the
    /// model's choice, else the document's main language, else the first listed;</item>
    /// <item>a short text (fewer than <see cref="FewLetters"/> letters) takes the document's main language when that
    /// is one of the choices;</item>
    /// <item>none in that script, or nothing found: "auto", the checkers decide.</item>
    /// </list>
    /// </summary>
    public static string Choose(string text, IReadOnlyList<string> candidates, IReadOnlyList<string>? document = null)
    {
        document ??= Array.Empty<string>();
        bool shortText = Letters(text) < FewLetters;
        var script = DominantScript(text);
        if (candidates.Count == 0)
        {
            if (document.Count > 0) return Choose(text, document, document);
            if (shortText) return "auto";
            var among = GuessAmong.Where(l => ScriptOf(l) == script).ToList();
            return DetectAmong(text, among.Count > 0 ? among : GuessAmong) ?? "auto";
        }
        var main = document.FirstOrDefault(l => candidates.Contains(l));
        if (script == null) return main ?? candidates[0];
        if (candidates.Count == 1)
            return ScriptOf(candidates[0]) == script ? candidates[0] : Choose(text, Array.Empty<string>());
        var same = candidates.Where(c => ScriptOf(c) == script).ToList();
        if (same.Count <= 1) return same.Count == 1 ? same[0] : Choose(text, Array.Empty<string>());
        var fromDocument = main != null && same.Contains(main) ? main : null;
        if (shortText && fromDocument != null) return fromDocument;
        return DetectAmong(text, same) ?? fromDocument ?? same[0];
    }

    /// <summary>Whether choosing among <paramref name="candidates"/> can need the document's languages: none listed,
    /// or two of one script (English and German). Russian and English alone never do: the script decides.</summary>
    public static bool NeedsDocument(IReadOnlyList<string> candidates) =>
        candidates.Count == 0 || candidates.GroupBy(ScriptOf).Any(g => g.Count() > 1);

    /// <summary>
    /// A document's languages, most used first, from the languages chosen for its longer paragraphs: those that make up
    /// a fifth of them and at least two paragraphs (or the only paragraph), so that a stray guess (one paragraph taken
    /// for Portuguese in a Spanish document) is not one. <paramref name="previous"/>, found before in the same document,
    /// stay while they make up a tenth: one edited paragraph does not take a language in and out at each pause.
    /// </summary>
    public static List<string> DocumentLanguages(IEnumerable<(string Text, string Language)> paragraphs, IReadOnlyList<string>? previous = null)
    {
        var counts = new Dictionary<string, int>();
        int total = 0;
        foreach (var (text, language) in paragraphs)
        {
            if (language == "auto" || Letters(text) < FewLetters) continue;
            counts[language] = counts.GetValueOrDefault(language) + 1;
            total++;
        }
        return counts.Where(c => total == 1 || (c.Value >= 2 && c.Value >= total / 5.0) || (previous?.Contains(c.Key) == true && c.Value >= total / 10.0))
            .OrderByDescending(c => c.Value).Select(c => c.Key).ToList();
    }

    /// <summary>The languages a Markdown document states in its front matter, as Obsidian notes do: "language: de",
    /// "lang: ru, en" or a list "[ru, en]". Empty when it states none.</summary>
    public static List<string> Stated(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0 || lines[0].TrimEnd() != "---") return new();
        for (int i = 1; i < lines.Count && i < 100; i++)
        {
            var l = lines[i].TrimEnd();
            if (l is "---" or "...") break;
            int colon = l.IndexOf(':', StringComparison.Ordinal);
            if (colon < 0 || l[..colon].Trim().ToLowerInvariant() is not ("language" or "lang")) continue;
            var value = l[(colon + 1)..];
            // a YAML list on the following lines ("- de")
            if (value.Trim().Length == 0)
                value = string.Join(',', lines.Skip(i + 1).TakeWhile(x => x.TrimStart().StartsWith("- ", StringComparison.Ordinal)).Select(x => x.Trim()[2..]));
            return value.Split([',', ';', ' ', '[', ']', '"', '\''], StringSplitOptions.RemoveEmptyEntries).ToList();
        }
        return new();
    }

    /// <summary>The language's English name ("German", "Brazilian Portuguese"...), or the tag itself when unknown.</summary>
    public static string NameOf(string tag)
    {
        try
        {
            var c = CultureInfo.GetCultureInfo(tag);
            return c.EnglishName.Contains("Unknown", StringComparison.Ordinal) || c.EnglishName.StartsWith("Language", StringComparison.Ordinal) ? tag : c.EnglishName;
        }
        catch (CultureNotFoundException) { return tag; }
    }

    /// <summary>The paragraph's language as a model's instructions name it, null for "auto" (not found): English with its
    /// spelling ("British English (en-GB spelling)", plain "en" British as measured; "American English", "English
    /// (Australia)"), any other language by its own name, its region only when it is not the language's home
    /// ("Russian", "German", but "Portuguese (Brazil)").</summary>
    public static string? PromptName(string tag)
    {
        if (tag.Length == 0 || tag.Equals("auto", StringComparison.OrdinalIgnoreCase)) return null;
        var parts = tag.Replace('_', '-').Split('-');
        var language = parts[0].ToLowerInvariant();
        var region = parts.Length > 1 ? parts[^1].ToUpperInvariant() : "";
        if (language == "en")
            return region switch
            {
                "" or "GB" => "British English (en-GB spelling)",
                "US" => "American English (en-US spelling)",
                _ => $"{NameOf("en-" + region)} (en-{region} spelling)",
            };
        return region.Length == 0 || region.Equals(language, StringComparison.OrdinalIgnoreCase) ?NameOf(language) : NameOf(tag);
    }
}
