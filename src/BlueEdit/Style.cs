using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

/// <summary>One suggested wording change: where in the paragraph, the new text, and why.</summary>
sealed record StyleChange(int Offset, int Length, string Replacement, string Reason);

/// <summary>
/// Style suggestions (Ctrl+F7): a language model proposes a better wording of a paragraph — less officialese,
/// wordiness and repetition, clearer sentences — keeping its meaning, facts and language. Like grammar, the model
/// returns the whole improved text plus short reasons; the changes are found by comparing the texts (TextDiff), nearby
/// ones joined into one, so the user can take some and leave others. Unlike grammar, a big change is not dropped:
/// rewording is what was asked for. Nothing is applied without the user's choice.
/// </summary>
static partial class StyleAdvisor
{
    internal const int MaxParagraph = 3000;

    /// <summary>What a suggestion may be about (setting "styleFocus", Tools > Style options): key, menu label, and
    /// the words the model is given.</summary>
    public static readonly (string Key, string Label, string Prompt)[] Focuses =
    {
        // the Russian word only in the model's instruction for a Russian paragraph, where it names the thing exactly;
        // the labels are shown through the interface's table (lang/*.json)
        ("officialese", "Officialese, bureaucratic phrasing", "officialese and bureaucratic phrasing (канцелярит)"),
        ("wordiness", "Wordiness, filler words", "wordiness and filler words"),
        ("repetition", "Needless repetition", "needless repetition of words"),
        ("clarity", "Overlong or tangled sentences", "overlong or tangled sentences (make them clearer, split them if needed)"),
        ("active", "Passive voice where active reads better", "the passive voice where the active reads better"),
        ("simple", "Rare or bookish words where plain ones will do", "rare or bookish words where plain ones will do"),
        // for essays and papers, off by default: elsewhere "a lot of" or "doesn't" is fine (#87)
        ("informal", "Informal words in formal writing (academic)", "informal or conversational words and contractions in formal writing (\"a lot of\", \"really\", \"got\", \"kind of\", \"doesn't\")"),
    };

    public static IReadOnlyList<string> AllFocuses => Focuses.Select(f => f.Key).ToArray();

    /// <summary>What is looked for when nothing is chosen: all but the academic register.</summary>
    public static IReadOnlyList<string> DefaultFocuses => AllFocuses.Where(k => k != "informal").ToArray();

    /// <summary>The model's instructions for the chosen kinds of improvement and the paragraph's language.</summary>
    internal static string Instructions(IReadOnlyCollection<string> focus, string language)
    {
        var name = LanguageName(language);
        var what = Focuses.Where(f => focus.Contains(f.Key)).Select(f => f.Prompt).ToList();
        if (what.Count == 0) what = Focuses.Where(f => DefaultFocuses.Contains(f.Key)).Select(f => f.Prompt).ToList();
        if (name != "Russian") what = what.Select(w => w.Replace(" (канцелярит)", "", StringComparison.Ordinal)).ToList();
        // until October 2026 Russian and English paragraphs were told "an editor of Russian and British English prose"
        // (American ones too): the paragraph's own language is named now
        return (language == "auto" ? "You are a careful editor of prose. " : $"You are a careful editor of {name} prose. ") +
            "Improve the wording of the user's paragraph, " +
            "looking only for: " + string.Join("; ", what) + ". " +
            "Keep the meaning, every fact, name, number and term, the author's voice and the language of the text; " +
            "do not translate, do not add information, do not summarise, do not change what already reads well. " +
            // measured (#87): without these the models put "can't" into formal text and American spellings into British
            "Keep the register: do not make formal text informal (no contractions where the text has none). " +
            (name == "British English"
                ? "Keep British spelling and usage: -ise, -our, -re, travelled, learnt, whilst, programme, and a plural verb " +
                  "after a collective noun (\"the committee have decided\"). "
                : "") +
            "Keep Markdown markup, links, addresses and code exactly as they are, and keep line breaks. " +
            "Answer only with JSON: {\"improved\": \"<the whole paragraph, improved>\", \"changes\": [{\"original\": " +
            "\"<exact fragment of the original>\", \"replacement\": \"<its new wording>\", \"reason\": \"<a few words why>\"}]}. " +
            "If the paragraph reads well, return it unchanged with an empty changes list. " +
            // without this the models tried rewrote Russian paragraphs in English despite "do not translate"
            $"The paragraph is in {name}: write the improved paragraph and the reasons in {name}.";
    }

    static readonly JsonNode Schema = JsonNode.Parse("""
        {"type":"object","properties":{"improved":{"type":"string"},"changes":{"type":"array","items":{"type":"object",
        "properties":{"original":{"type":"string"},"replacement":{"type":"string"},"reason":{"type":"string"}},
        "required":["original","replacement","reason"]}}},"required":["improved","changes"]}
        """)!;

    /// <summary>Asks the model; blocks until it answers (about a second, more when the server is still starting).
    /// <paramref name="language"/> is the paragraph's (ru-RU, en-GB...), said to the model outright; <paramref name="focus"/>
    /// the kinds of improvement (<see cref="Focuses"/> keys; null or empty: all).</summary>
    public static List<StyleChange> Suggest(LlmServer server, string paragraph, string language, IReadOnlyCollection<string>? focus = null) =>
        ChangesFrom(paragraph, server.Chat(Instructions(focus ?? AllFocuses.ToArray(), language), paragraph, "style", Schema,
            Math.Min(4000, 300 + paragraph.Length * 3)), language);

    // the instructions' name of the language without its spelling in brackets: "British English", "Russian"
    static string LanguageName(string tag) =>
        ParagraphLanguage.PromptName(tag) is not { } name ? "the same language as the text"
        : name.EndsWith(" spelling)", StringComparison.Ordinal) ? name[..name.LastIndexOf(" (", StringComparison.Ordinal)] : name;

    [GeneratedRegex(@"\w+")]
    private static partial Regex WordRx();

    // the end of a sentence: . ! ? or … then a space (or the end); "2.5" and "e.g.," are not
    [GeneratedRegex(@"[.!?…](?:\s|$)")]
    private static partial Regex SentenceEndRx();

    // what a wording change must leave as it is: emphasis, code, links, web addresses; in LaTeX its commands
    // (\emph, \cite{…}…, #78) — a model that drops one would drop the reference or the emphasis with it
    [GeneratedRegex(@"\*\*|__|`|\]\(|https?://\S+|\$|\\[A-Za-z]+")]
    private static partial Regex MarkupRx();

    static string Markup(string s) => string.Join(" ", MarkupRx().Matches(s).Select(m => m.Value));

    /// <summary>Turns the model's answer into changes with positions in <paramref name="text"/>.</summary>
    internal static List<StyleChange> ChangesFrom(string text, string answer, string? language = null)
    {
        using var doc = JsonDocument.Parse(answer);
        var improved = (doc.RootElement.GetProperty("improved").GetString() ?? text).TrimEnd();
        // a translation is not a better wording: the check of the alphabet that chooses the paragraph's language first
        if (language is not (null or "auto") && ParagraphLanguage.DominantScript(improved) is { } script && script != ParagraphLanguage.ScriptOf(language))
            throw new InvalidOperationException("the model answered in another language; try again or pick another model (Tools > Grammar model)");
        var notes = doc.RootElement.TryGetProperty("changes", out var cs) && cs.ValueKind == JsonValueKind.Array
            ? cs.EnumerateArray().Select(c => (Original: Str(c, "original"), Reason: Str(c, "reason"))).ToList()
            : new();
        var original = text.TrimEnd();
        // changes in spaces only are not wording; changes a word or less apart read as one edit
        // British text: a change that only spells it the American way is not a better wording (YandexGPT did, #87);
        // dropped before nearby changes are joined, or it would ride along inside a real one
        bool british = language != null && LanguageName(language) == "British English";
        var diff = TextDiff.Changes(original, improved).Where(c => c.OriginalIn(original).Trim() != c.Replacement.Trim()
            && !(british && Americanised(c.OriginalIn(original), c.Replacement))).ToList();
        var res = new List<StyleChange>();
        foreach (var group in Join(original, diff, bySentence: true))
        {
            if (Offer(original, group, notes) is { } change) { res.Add(change); continue; }
            // the sentence as one change would lose markup or a link: its pieces that do not, one by one (as before #96)
            if (group.Pieces.Count > 1)
                foreach (var piece in Join(original, group.Pieces, bySentence: false))
                    if (Offer(original, piece, notes) is { } part) res.Add(part);
        }
        return res;
    }

    sealed record Group(int Offset, int Length, string Replacement, List<TextChange> Pieces);

    // Changes a word or less apart read as one edit; with bySentence, so do all changes in one sentence: a reordered
    // sentence ("was substituted by us" → "we substituted") comes as pieces several words apart that only make sense
    // together (#96, seen in the Obsidian plugin).
    static List<Group> Join(string original, IEnumerable<TextChange> changes, bool bySentence)
    {
        var groups = new List<Group>();
        foreach (var c in changes)
        {
            if (groups.Count > 0)
            {
                var last = groups[^1];
                var gap = original[(last.Offset + last.Length)..c.Offset];
                if (!gap.Contains('\n', StringComparison.Ordinal) && (WordRx().Count(gap) <= 1 || (bySentence && !SentenceEndRx().IsMatch(gap))))
                {
                    groups[^1] = new Group(last.Offset, c.Offset + c.Length - last.Offset, last.Replacement + gap + c.Replacement, [.. last.Pieces, c]);
                    continue;
                }
            }
            groups.Add(new Group(c.Offset, c.Length, c.Replacement, [c]));
        }
        return groups;
    }

    // A group as a change to offer, or null when it would lose (or invent) markup or a link.
    static StyleChange? Offer(string original, Group g, List<(string Original, string Reason)> notes)
    {
        // spaces both sides keep at the edges are not part of the change: «данных » → «данные » is «данных» → «данные»
        int offset = g.Offset, length = g.Length; var replacement = g.Replacement;
        while (length > 0 && replacement.Length > 0 && original[offset + length - 1] == ' ' && replacement[^1] == ' ') { length--; replacement = replacement[..^1]; }
        while (length > 0 && replacement.Length > 0 && original[offset] == ' ' && replacement[0] == ' ') { offset++; length--; replacement = replacement[1..]; }
        var wrong = original.Substring(offset, length);
        if (Markup(wrong) != Markup(replacement)) return null;
        var note = notes.FirstOrDefault(n => n.Original.Length > 0 && wrong.Trim().Length > 0
            && (n.Original.Contains(wrong.Trim(), StringComparison.Ordinal) || wrong.Contains(n.Original.Trim(), StringComparison.Ordinal)));
        return new StyleChange(offset, length, replacement, note.Reason ?? "");
    }

    // British endings and their American forms, longest first: organisation/organization, analyse/analyze,
    // behaviour/behavior, centre/center, travelled/traveled, defence/defense
    static readonly (string British, string American)[] Spellings =
    {
        ("isation", "ization"), ("ising", "izing"), ("ised", "ized"), ("ise", "ize"), ("yse", "yze"), ("ysed", "yzed"),
        ("ysing", "yzing"), ("our", "or"), ("tre", "ter"), ("lled", "led"), ("lling", "ling"), ("ence", "ense"),
        ("ogue", "og"), ("mme", "m"),
    };

    [GeneratedRegex(@"\p{L}+")]
    private static partial Regex LettersRx();

    /// <summary>The change only turns British spellings into American ones: every word that differs becomes the same
    /// once British endings are spelt the American way ("organisation analysed" → "organization analyzed").</summary>
    internal static bool Americanised(string from, string to)
    {
        var a = LettersRx().Matches(from).Select(m => m.Value).ToList();
        var b = LettersRx().Matches(to).Select(m => m.Value).ToList();
        if (a.Count == 0 || a.Count != b.Count || a.SequenceEqual(b, StringComparer.Ordinal)) return false;
        return a.Zip(b).All(p => p.First == p.Second || American(p.First).Equals(p.Second, StringComparison.OrdinalIgnoreCase));
    }

    static string American(string word)
    {
        var w = word.ToLowerInvariant();
        foreach (var (british, american) in Spellings)
        {
            int at = w.LastIndexOf(british, StringComparison.Ordinal);
            // the ending, or the ending followed by a plural or past ending: "behaviours", "centres", "organisations"
            if (at > 0 && (w.Length - at - british.Length) is var rest && rest <= 2 && (rest == 0 || w[^rest..] is "s" or "d" or "ed" or "es"))
                return w[..at] + american + w[(at + british.Length)..];
        }
        return w;
    }

    static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    /// <summary>The paragraph with the chosen changes made (they must not overlap).</summary>
    public static string Apply(string text, IEnumerable<StyleChange> changes)
    {
        foreach (var c in changes.OrderByDescending(c => c.Offset))
            text = text[..c.Offset] + c.Replacement + text[(c.Offset + c.Length)..];
        return text;
    }
}
