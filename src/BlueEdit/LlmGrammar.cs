using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// Proofreading by a language model running locally in llama.cpp's server (%LOCALAPPDATA%\BlueEdit\llm: bin\ with
/// llama-server.exe, models\ with .gguf files). The model is asked, as Edge's built-in proofreader does, for the
/// corrected text — here together with a list of the errors and short explanations, as JSON held to a schema.
/// What changed is found by comparing the texts (TextDiff), so positions never depend on the model counting
/// characters; the explanations are matched to the changes by their text. A paragraph the model rewrote rather
/// than corrected is ignored. The server starts on first use, runs on the GPU, and stops with the editor.
/// </summary>
sealed partial class LlmGrammarChecker : IGrammarChecker
{
    public string Name => "Language model";

    readonly LlmServer? _server;
    readonly bool _ownsServer;
    internal int? ServerProcessId => _server?.ProcessId;
    readonly ConcurrentDictionary<(string, string), IReadOnlyList<GrammarIssue>> _done = new();
    readonly ConcurrentDictionary<(string, string), byte> _queued = new();
    readonly BlockingCollection<(string Text, string Lang)> _work = new();
    readonly Thread? _thread;
    int _version;
    volatile string? _status;

    const int MaxParagraph = 3000;           // characters; longer paragraphs are left to the other checkers
    const double MaxChangedShare = 0.5;      // more than this changed: the model rewrote the text, not corrected it

    public static string DefaultDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), "BlueEdit", "llm");
    static string ServerIn(string dir) => Path.Combine(dir, "bin", LlmServer.Exe("llama-server"));

    /// <summary>The preferred models, best first (by tools/proofbench); any other .gguf in models\ is used when none of these is there.</summary>
    static readonly string[] Preferred =
    {
        "YandexGPT-5-Lite-8B-instruct-Q4_K_M.gguf", "google_gemma-3-12b-it-Q4_K_M.gguf", "Qwen3-8B-Q4_K_M.gguf",
        "Qwen_Qwen3-14B-Q4_K_M.gguf", "google_gemma-3-4b-it-Q4_K_M.gguf", "microsoft_Phi-4-mini-instruct-Q4_K_M.gguf",
    };

    /// <summary>A model file's place in <see cref="Preferred"/>; after them all when it is not there.</summary>
    internal static int Rank(string file) => Array.FindIndex(Preferred, p => p.Equals(file, StringComparison.OrdinalIgnoreCase)) is int i and >= 0 ? i : int.MaxValue;

    /// <summary>All model files in models\, the preferred ones first in their order, then the others by name.</summary>
    public static IReadOnlyList<string> ModelsIn(string dir)
    {
        var models = Path.Combine(dir, "models");
        if (!Directory.Exists(models)) return Array.Empty<string>();
        return Directory.EnumerateFiles(models, "*.gguf")
            .OrderBy(f => Rank(Path.GetFileName(f)))
            .ThenBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The model file to use: the one named in the settings (a file in models\ or a full path), otherwise
    /// the best one present; null when there is none.</summary>
    public static string? ModelIn(string dir, string? wanted)
    {
        var models = Path.Combine(dir, "models");
        if (!string.IsNullOrWhiteSpace(wanted))
        {
            var p = Path.IsPathRooted(wanted) ? wanted : Path.Combine(models, wanted);
            return File.Exists(p) ? p : null;
        }
        var all = ModelsIn(dir);
        return all.Count > 0 ? all[0] : null;
    }

    public static bool InstalledIn(string dir, string? wanted) => File.Exists(ServerIn(dir)) && ModelIn(dir, wanted) != null;

    /// <summary>The server for the model in <paramref name="dir"/> (the one named, or the best one), or null when
    /// llama.cpp or a model is missing.</summary>
    public static LlmServer? ServerFor(string dir, string? wantedModel) =>
        File.Exists(ServerIn(dir)) && ModelIn(dir, wantedModel) is { } model ? new LlmServer(dir, model) : null;

    /// <summary>A checker with a server of its own.</summary>
    public LlmGrammarChecker(string dir, string? wantedModel) : this(ServerFor(dir, wantedModel), dir) { _ownsServer = true; }

    /// <summary>A checker using a shared server (null: llama.cpp or a model is missing in <paramref name="dir"/>).</summary>
    public LlmGrammarChecker(LlmServer? server, string dir)
    {
        _server = server;
        if (server == null) { _status = $"llama.cpp or a model not found in {dir}"; return; }
        _status = "starting";
        _thread = new Thread(Loop) { IsBackground = true, Name = "Language model" };
        _thread.Start();
    }

    public string ModelName => _server?.ModelName ?? "";

    /// <summary>"Gemma 3 12B" for google_gemma-3-12b-it-Q4_K_M.gguf: the name findings are signed with.</summary>
    public static string DisplayName(string modelPath)
    {
        var f = Path.GetFileNameWithoutExtension(modelPath).ToLowerInvariant();
        if (f.Contains("gemma-3-12b", StringComparison.Ordinal)) return "Gemma 3 12B";
        if (f.Contains("gemma-3-4b", StringComparison.Ordinal)) return "Gemma 3 4B";
        if (f.Contains("qwen3-8b", StringComparison.Ordinal)) return "Qwen3 8B";
        if (f.Contains("qwen3-14b", StringComparison.Ordinal)) return "Qwen3 14B";
        if (f.Contains("yandexgpt-5-lite", StringComparison.Ordinal)) return "YandexGPT 5 Lite";
        if (f.Contains("phi-4-mini", StringComparison.Ordinal)) return "Phi-4-mini";
        var name = Path.GetFileNameWithoutExtension(modelPath);
        int q = name.IndexOf("-Q", StringComparison.OrdinalIgnoreCase);
        return q > 0 ? name[..q] : name;
    }
    // Off only when the server could not start. A paragraph that fails (a broken answer, a timeout) is that paragraph's
    // problem: before, it switched the checker off for the rest of the session without a word (found by MathBench, #48).
    public bool Available => _thread != null && !_broken;
    volatile bool _broken;

    /// <summary>Why the last paragraph that failed did (shown once per message), or null.</summary>
    public string? LastFailure { get; private set; }
    public string? Status => _status;
    public int Version => Volatile.Read(ref _version);

    public IReadOnlyList<GrammarIssue>? Issues(string paragraph, string language, bool queue = true)
    {
        if (!Available || paragraph.Trim().Length == 0 || paragraph.Length > MaxParagraph) return Array.Empty<GrammarIssue>();
        var key = (paragraph, language);
        if (_done.TryGetValue(key, out var issues))
            return issues;
        if (queue && _queued.TryAdd(key, 0) && !_work.IsAddingCompleted) _work.Add(key);
        return null;
    }


    // ---------- the worker thread ----------
    void Loop()
    {
        try
        {
            _server!.Url();
            _status = null;
            // llama.cpp is missing here or did not start: the server at an address answers instead (#95); say so once
            if (_server.FallbackReason is { } why) LastFailure = $"{why}; using {_server.Remote?.Model} on {_server.Remote?.Host}";
        }
        catch (InvalidOperationException ex)
        {
            _status = "language model did not start: " + ex.Message;
            _broken = true;
            Interlocked.Increment(ref _version);
            return;
        }
        Interlocked.Increment(ref _version);
        foreach (var (text, lang) in _work.GetConsumingEnumerable())
        {
            try { _done[(text, lang)] = Proofread(text, lang); }
            // the service's limit of requests: said once, nothing sent until the time it gave, then this paragraph is
            // asked again (not left as "nothing found")
            catch (LimitReachedException ex)
            {
                LastFailure = "language model: " + ex.Message;
                Interlocked.Increment(ref _version);
                while (DateTime.Now < ex.Until && !_work.IsAddingCompleted) Thread.Sleep(1000);
                if (!_work.IsAddingCompleted) _work.Add((text, lang));
                continue;
            }
            // ArgumentException and IndexOutOfRangeException too: a model's answer this code reads wrongly costs that
            // paragraph its findings, never the editor (an exception on this thread would end the process)
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException
                or KeyNotFoundException or ArgumentException or IndexOutOfRangeException)
            {
                _done[(text, lang)] = Array.Empty<GrammarIssue>();
                LastFailure = "language model: " + ex.Message;
            }
            _queued.TryRemove((text, lang), out _);
            Interlocked.Increment(ref _version);
        }
    }

    const string British =
        "British usage is correct and must stay: a plural verb after a collective noun or data (\"the committee have " +
        "decided\", \"the team were\", \"the data are\"), and -ise, -our, -re, travelled, learnt, whilst, programme are British spellings. ";

    // #124 (found by the Obsidian plugin): en-US text was asked for British spelling too
    const string American =
        "American spelling is correct and must stay: -ize, -or, -er, traveled, program, license, defense are American spellings. ";

    /// <summary>The instructions for a paragraph in <paramref name="lang"/>: the paragraph's own language named
    /// (<see cref="ParagraphLanguage.PromptName"/>), so the model neither translates nor answers in another; British or
    /// American spellings kept for those Englishes; "auto" (not found) the language of the text.</summary>
    internal static string InstructionsFor(string lang)
    {
        // until October 2026 every Russian and English paragraph was told "a proofreader for Russian and British
        // English", whatever its own language was: one language, the paragraph's, is named now
        var name = ParagraphLanguage.PromptName(lang);
        var who = name == null
            ? "You are a careful proofreader. Keep the text in its own language, never translate it. "
            : $"You are a careful proofreader of {name}. The user's text is in {name}: never translate it. ";
        var spelling = name == null ? "" : name.StartsWith("British", StringComparison.Ordinal) ? British
            : name.StartsWith("American", StringComparison.Ordinal) ? American : "";
        var explain = name == null ? "the language of the text" : name.Split(" (", 2)[0];
        return who + spelling + Common + $"Write every explanation in {explain}. " + Alternatives;
    }

    const string Common =
        "Fix only spelling, grammar and punctuation errors in the user's text. Do not rephrase, do not change style, " +
        "do not change anything that is correct, keep line breaks. " +
        "Read the whole text before correcting it: a sentence can be wrong only because of an earlier one. Check that " +
        "pronouns, gender and number agree with the people and things named before them, even sentences earlier (a " +
        "woman named, then \"he wrote\": \"she wrote\"), and that verb tenses agree with the time words (tomorrow or next " +
        "week: the future). " +
        "Answer only with JSON: " +
        "{\"corrected\": \"<the whole text, corrected>\", \"errors\": [{\"original\": \"<exact wrong fragment>\", " +
        "\"replacement\": \"<fix>\", \"alternatives\": [\"<other correct fixes, if the text allows several>\"], " +
        "\"explanation\": \"<short reason>\"}]}. ";

    const string Alternatives =
        "When more than one fix is correct (for example the speaker's gender is unknown: \"я пошёл\" or \"я пошла\"), " +
        "put the most likely one in replacement and the others in alternatives. " +
        "If there are no errors, return the text unchanged and an empty errors list.";

    /// <summary>Added for a paragraph of a LaTeX document (<see cref="LooksLikeLatex"/>). Measured (#115): Gemma 3 12B
    /// stopped looping while it copied LaTeX ("\n" or a no-break space on and on until the token limit), the LaTeX set
    /// 8 → 10 of 11; YandexGPT 10 of 11 with one false alarm less. Not for Markdown, whose formulas are all inside
    /// dollars: there it found nothing more and put more findings off target.</summary>
    internal const string LatexInstructions =
        " The text comes from a LaTeX document: copy every LaTeX command, brace, formula, \\cite and \\ref exactly as it is, character for character; correct only the words around them.";

    /// <summary>A command outside $…$: the text of a LaTeX document (\cite, \ref, \begin{equation} stay in the paragraph
    /// the checker gets), not of Markdown, whose LaTeX is all inside dollars.</summary>
    internal static bool LooksLikeLatex(string text) =>
        text.Contains('\\', StringComparison.Ordinal) && OutsideDollarsCommandRx().IsMatch(DollarsRx().Replace(text, " "));

    [System.Text.RegularExpressions.GeneratedRegex(@"\$\$.*?\$\$|\$[^$]*\$", System.Text.RegularExpressions.RegexOptions.Singleline)]
    private static partial System.Text.RegularExpressions.Regex DollarsRx();

    [System.Text.RegularExpressions.GeneratedRegex(@"\\[A-Za-z]")]
    private static partial System.Text.RegularExpressions.Regex OutsideDollarsCommandRx();

    static readonly JsonNode Schema = JsonNode.Parse("""
        {"type":"object","properties":{"corrected":{"type":"string"},"errors":{"type":"array","items":{"type":"object",
        "properties":{"original":{"type":"string"},"replacement":{"type":"string"},"alternatives":{"type":"array","items":{"type":"string"}},
        "explanation":{"type":"string"}},"required":["original","replacement","explanation"]}}},"required":["corrected","errors"]}
        """)!;

    /// <summary>Each answer as it comes, with the paragraph it is for: the benchmarks keep them, so the way answers become
    /// findings can be measured again without asking the models again.</summary>
    internal static Action<LlmGrammarChecker, string, string>? AnswerSeen;

    List<GrammarIssue> Proofread(string text, string lang)
    {
        try { return Ask(text, lang); }
        catch (CutOffException) when (WithoutFormulas(text) is { Formulas.Count: > 0 } short_)
        {
            // the model looped while copying a formula (#126): ask again with each formula as one word, which it has
            // nothing to copy in; the findings are moved back to the paragraph, any on a formula dropped
            return MapBack(Ask(short_.Text, lang), short_.Formulas);
        }
    }

    List<GrammarIssue> Ask(string text, string lang)
    {
        var instructions = InstructionsFor(lang);
        var answer = _server!.Chat(LooksLikeLatex(text) ? instructions + LatexInstructions : instructions, text, "proofread", Schema,
            Math.Min(4000, 200 + text.Length * 2));
        AnswerSeen?.Invoke(this, text, answer);
        return IssuesFrom(text, answer, lang, ModelName);
    }

    /// <summary>Turns the model's JSON answer into findings with positions in <paramref name="text"/>.</summary>
    internal static List<GrammarIssue> IssuesFrom(string text, string answer, string lang, string source = "Language model")
    {
        using var doc = JsonDocument.Parse(answer);
        var corrected = doc.RootElement.GetProperty("corrected").GetString() ?? text;
        var notes = doc.RootElement.TryGetProperty("errors", out var errs) && errs.ValueKind == JsonValueKind.Array
            ? errs.EnumerateArray().Select(e => (Original: Str(e, "original"), Replacement: Str(e, "replacement"), Explanation: Str(e, "explanation"),
                Alternatives: e.TryGetProperty("alternatives", out var alt) && alt.ValueKind == JsonValueKind.Array
                    ? alt.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList() : new List<string>())).ToList()
            : new();
        corrected = DropQuotedRepeats(text, RepairLatexEscapes(text, corrected));
        // the model may drop or add a final newline or trailing spaces: compare what it was asked about
        corrected = corrected.TrimEnd();
        var changes = TextDiff.Changes(text.TrimEnd(), corrected);
        if (TextDiff.ChangedShare(text, changes) > MaxChangedShare) return new();
        // after the guard: the words a join takes in between were not rewritten
        changes = JoinByNotes(text, changes, notes.Select(n => RepairLatexEscapes(text, n.Original)));
        var res = new List<GrammarIssue>();
        foreach (var c in changes)
        {
            var wrong = c.OriginalIn(text);
            // spaces only (markup the checker saw as blanks, a trimmed line start): nothing to show
            if (wrong.Trim() == c.Replacement.Trim() || AddsLatex(wrong, c.Replacement) || CopiesText(text, wrong, c.Replacement)
                || ChangesScript(wrong, c.Replacement) || ChangesNumbers(wrong, c.Replacement)) continue;
            var note = notes.ElementAtOrDefault(NoteFor(notes.Select(n => (n.Original, n.Replacement)).ToList(), wrong, c.Replacement));
            var message = note.Explanation is { Length: > 0 } ex ? ex : Possibly(wrong, c.Replacement);
            // other correct fixes the model named for the same fragment ("пошла" next to "пошёл")
            var fixes = new List<string> { c.Replacement };
            if (note.Alternatives != null && note.Original.Length > 0)
                foreach (var alt in note.Alternatives)
                {
                    // the alternative is for the model's fragment ("Я пошли" → "Я пошёл"): put it into the text and see
                    // what it makes of the stretch this change covers ("пошли" → "пошёл")
                    int at = text.IndexOf(note.Original, StringComparison.Ordinal);
                    if (at < 0) continue;
                    var withAlt = text.Remove(at, note.Original.Length).Insert(at, alt);
                    var same = TextDiff.Changes(text, withAlt).FirstOrDefault(x => x.Offset == c.Offset && x.Length == c.Length);
                    if (same != null && !fixes.Contains(same.Replacement)) fixes.Add(same.Replacement);
                }
            res.Add(new GrammarIssue(c.Offset, c.Length, message, fixes, $"LLM:{wrong}→{c.Replacement}", source));
        }
        // an error the model listed but did not put into its corrected text (Gemma 3 12B: "The survey were" → "was" in the
        // list, "were" left in the text): the error as the list has it, where its fragment stands in the text — only
        // once, so that it cannot be the wrong place
        foreach (var n in notes)
        {
            string original = RepairLatexEscapes(text, n.Original), replacement = RepairLatexEscapes(text, n.Replacement);
            if (original.Length == 0 || original == replacement) continue;
            int at = text.IndexOf(original, StringComparison.Ordinal);
            if (at < 0 || text.IndexOf(original, at + 1, StringComparison.Ordinal) >= 0) continue;
            if (res.Any(r => r.Offset <= at + original.Length && at <= r.Offset + r.Length)) continue;
            foreach (var c in TextDiff.Changes(text, text.Remove(at, original.Length).Insert(at, replacement)))
            {
                var wrong = c.OriginalIn(text);
                if (wrong.Trim() == c.Replacement.Trim() || AddsLatex(wrong, c.Replacement) || ChangesScript(wrong, c.Replacement)
                    || ChangesNumbers(wrong, c.Replacement)) continue;
                // "and  takes" → "andtakes": the gap of blanked markup ("\emph{and} takes") taken for a typo (YandexGPT)
                if (wrong.Contains("  ", StringComparison.Ordinal) && string.Concat(wrong.Where(ch => !char.IsWhiteSpace(ch))) == string.Concat(c.Replacement.Where(ch => !char.IsWhiteSpace(ch)))) continue;
                var message = n.Explanation.Length > 0 ? n.Explanation : Possibly(wrong, c.Replacement);
                res.Add(new GrammarIssue(c.Offset, c.Length, message, new[] { c.Replacement }, $"LLM:{wrong}→{c.Replacement}", source));
            }
        }
        return res;
    }

    /// <summary>
    /// Changes that lie inside the fragment of one error the model listed are one change (#149): a moved word
    /// ("where is the station" → "where the station is") comes out of the word diff as "is " → "" and "station" →
    /// "station is", each wrong alone, with words between them that #141's joining does not cross. Joined from the
    /// first change to the last, so the words around the fragment stay as they are. Changes no listed error holds
    /// stay apart. (Found by BlueProof for Obsidian, which joins the same way.)
    /// </summary>
    internal static List<TextChange> JoinByNotes(string text, List<TextChange> changes, IEnumerable<string> fragments)
    {
        foreach (var fragment in fragments)
        {
            if (fragment.Trim().Length == 0) continue;
            for (int at = text.IndexOf(fragment, StringComparison.Ordinal); at >= 0; at = text.IndexOf(fragment, at + 1, StringComparison.Ordinal))
            {
                int first = changes.FindIndex(c => c.Offset >= at && c.Offset + c.Length <= at + fragment.Length);
                int last = changes.FindLastIndex(c => c.Offset >= at && c.Offset + c.Length <= at + fragment.Length);
                if (first < 0 || last == first || !MovesWord(text, changes.GetRange(first, last - first + 1))) continue;
                // changes that overlap or come out of order (Gemma rewriting a text with blanked formulas) are not one
                // moved word: left apart, never joined across each other
                if (Enumerable.Range(first + 1, last - first).Any(i => changes[i].Offset < changes[i - 1].Offset + changes[i - 1].Length)) continue;
                int start = changes[first].Offset, end = changes[last].Offset + changes[last].Length;
                var joined = new System.Text.StringBuilder();
                for (int i = first, pos = start; i <= last; pos = changes[i].Offset + changes[i].Length, i++)
                    joined.Append(text, pos, changes[i].Offset - pos).Append(changes[i].Replacement);
                changes.RemoveRange(first, last - first + 1);
                changes.Insert(first, new TextChange(start, end - start, joined.ToString()));
            }
        }
        return changes;
    }

    // The message of a change no note explains; a deletion says what goes ("Possibly: " and nothing after it before).
    // In the interface's language: the model writes its own explanations in the paragraph's, but this one is ours, and
    // until October 2026 it knew only Russian and English (a German paragraph got English)
    static string Possibly(string wrong, string fix) =>
        fix.Trim().Length > 0 ? Ui.T("Possibly: {0}", fix) : Ui.T("Possibly delete: “{0}”", wrong.Trim());

    /// <summary>
    /// Which of the model's notes explains a change (#130, found by the Obsidian plugin); -1 for none. Taking the first
    /// note whose fragment holds the changed text gave a short change — a comma, a letter, which most fragments hold —
    /// the explanation and alternatives of another error: "Yes, he go home, said that." with notes "Yes, he go" → "Yes,
    /// he goes" and "home," → "home" explained the deleted comma as "Agreement of the verb.". Best first: the note with
    /// this wrong text and this fix; one whose fragment, with the fix put in, gives its own fix ("home," with "," → ""
    /// gives "home"), or whose fix holds this fix; one whose fragment only holds the wrong text, when that has a letter.
    /// "Holds" is as whole words: "a" → "an" is not about "and the integral are". (The plugin asks for three letters
    /// instead, which left "me" and "за" unexplained and the article "a" too.)
    /// </summary>
    internal static int NoteFor(IReadOnlyList<(string Original, string Replacement)> notes, string wrong, string fix)
    {
        string w = wrong.Trim(), r = fix.Trim();
        int Score((string Original, string Replacement) n)
        {
            string o = n.Original.Trim(), f = n.Replacement.Trim();
            if (o.Length == 0) return 0;
            if (o == w && f == r) return 3;
            int at = Find(o, w);
            if (at < 0 && Find(w, o) < 0) return 0;
            // the fix put into the note's fragment gives the note's fix (a deleted comma: "home," → "home")
            if (at >= 0 && w.Length > 0 && o.Remove(at, w.Length).Insert(at, r).Trim() == f) return 2;
            if (r.Length > 0 && f.Length > 0 && (Find(f, r) >= 0 || Find(r, f) >= 0)) return 2;
            // a word of the fragment, however short ("a", "me", "за"); a comma or a blank stands in too many
            return w.Any(char.IsLetter) ? 1 : 0;
        }
        int best = -1, bestScore = 0;
        for (int i = 0; i < notes.Count; i++)
            if (Score(notes[i]) is var s && s > bestScore) (best, bestScore) = (i, s);
        return best;
    }

    // Where `part` stands in `s` as whole words: "a" in "a slant heights", not in "and" (-1: nowhere)
    static int Find(string s, string part)
    {
        if (part.Length == 0) return s.Length > 0 ? 0 : -1;
        var m = System.Text.RegularExpressions.Regex.Match(s, (char.IsLetterOrDigit(part[0]) ? @"(?<!\w)" : "") + System.Text.RegularExpressions.Regex.Escape(part)
            + (char.IsLetterOrDigit(part[^1]) ? @"(?!\w)" : ""));
        return m.Success ? m.Index : -1;
    }

    // A word one change takes out and another puts in: the pieces of a move. Two errors the model named in one fragment
    // ("results was better then" → "results were better than", Phi-4-mini) share none and stay two fixes, each right alone.
    static bool MovesWord(string text, List<TextChange> pieces)
    {
        var words = pieces.Select(c => (Out: WordRx().Matches(c.OriginalIn(text)).Select(m => m.Value.ToLowerInvariant()).ToHashSet(),
            In: WordRx().Matches(c.Replacement).Select(m => m.Value.ToLowerInvariant()).ToHashSet())).ToList();
        for (int i = 0; i < words.Count; i++)
            for (int j = 0; j < words.Count; j++)
                if (i != j && words[i].Out.Overlaps(words[j].In)) return true;
        return false;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\w+")]
    private static partial System.Text.RegularExpressions.Regex WordRx();

    // A change that puts a LaTeX command where there was none: the model "fixing" the markup (Gemma 3 12B put "\\"
    // between the rows of an align, #115), never a fix of the prose.
    // Also one that only puts in or takes out LaTeX's own characters, { } ^ _ $ \ (#192, found by BlueProof for
    // Obsidian: Gemini 3.5 Flash-Lite put a "}" next to a display formula that was right).
    static bool AddsLatex(string wrong, string fix) =>
        fix.Contains('\\', StringComparison.Ordinal) && !wrong.Contains('\\', StringComparison.Ordinal)
        || wrong != fix && string.Concat(wrong.Where(ch => !IsLatexMark(ch))) == string.Concat(fix.Where(ch => !IsLatexMark(ch)));

    static bool IsLatexMark(char ch) => ch is '{' or '}' or '^' or '_' or '$' or '\\';

    // A word put into the other script, Latin ↔ Cyrillic: a translation, not a fix (#123, found by the Obsidian plugin:
    // Gemma 3 12B put "gradient descent" into Russian; Phi-4-mini answered English sentences in Russian). A word of both
    // scripts at once ("Pис" with a Latin P, a keyboard slip) is neither, so its fix stays.
    internal static bool ChangesScript(string wrong, string fix) => ScriptOf(wrong) is var a && ScriptOf(fix) is var b
        && ((a == 'L' && b == 'C') || (a == 'C' && b == 'L'));

    static char ScriptOf(string s)
    {
        bool latin = s.Any(ch => ch is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= 'À' and <= 'ɏ'));
        bool cyrillic = s.Any(ch => ch is >= 'Ѐ' and <= 'ӿ');
        return latin ? (cyrillic ? 'B' : 'L') : cyrillic ? 'C' : '-';
    }

    // Other digits: the model recomputing the notes, not proofreading them (#123: Gemma 3 12B, "about 314 cubic
    // centimetres" → 209, where 314 is right). A typo in a number is left to the writer.
    internal static bool ChangesNumbers(string wrong, string fix) =>
        !wrong.Where(char.IsAsciiDigit).SequenceEqual(fix.Where(char.IsAsciiDigit));

    const int MinCopy = 24;

    [System.Text.RegularExpressions.GeneratedRegex(@"\s?«([^«»]{24,}?)»")]
    private static partial System.Text.RegularExpressions.Regex QuotedRx();

    /// <summary>
    /// Takes out of the corrected text a sentence in «» that it has elsewhere too, unquoted: YandexGPT 5 Lite repeated the
    /// sentence before in quotes (73 changes in the benchmark answers of 4–7 October 2026). Taken out before the diff, so
    /// a fix next to the repeat ("нравиться" → "нравится") stays a finding of its own. A quotation the text has is kept.
    /// </summary>
    internal static string DropQuotedRepeats(string text, string corrected) =>
        QuotedRx().Replace(corrected, m =>
        {
            var inner = m.Groups[1].Value;
            if (text.Contains("«" + inner + "»", StringComparison.Ordinal)) return m.Value;
            var rest = corrected.Remove(m.Index, m.Length);
            return rest.Contains(inner.Trim(), StringComparison.Ordinal) || text.Contains(inner.Trim(), StringComparison.Ordinal) ? "" : m.Value;
        });

    // A change that puts in again a stretch the paragraph already has: YandexGPT repeated the sentence before in quotes
    // ("Анна пришла домой поздно. «Анна пришла домой поздно.» Потом…") or a piece of the paragraph elsewhere, where the
    // widened insertion then overlapped the next change. Never a fix; the fixes around it stay.
    internal static bool CopiesText(string text, string wrong, string fix)
    {
        if (fix.Length - wrong.Length < MinCopy) return false;
        for (int i = 0; i + MinCopy <= fix.Length; i++)
        {
            var piece = fix.Substring(i, MinCopy);
            if (!wrong.Contains(piece, StringComparison.Ordinal) && text.Contains(piece, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>
    /// A model copies LaTeX into its JSON without doubling the backslash: "\frac" is read back as a form feed and "rac",
    /// "\neq" as a line break and "eq", "\right" as a carriage return, "\times" as a tab, "\beta" as a backspace. The
    /// formula then looks changed, and enough such "changes" trip the rewrite guard and lose the real fixes. Each LaTeX
    /// command of the original that JSON would spoil this way is put back where the answer has its spoilt form. (Found by
    /// the Obsidian plugin BlueProof, which shares BlueEdit's proofing.)
    /// </summary>
    internal static string RepairLatexEscapes(string original, string corrected)
    {
        if (!original.Contains('\\', StringComparison.Ordinal)) return corrected;
        foreach (var name in LatexEscapeRx().Matches(original).Select(m => m.Value).Distinct().OrderByDescending(n => n.Length))
        {
            char control = name[1] switch { 'f' => '\f', 'n' => '\n', 'r' => '\r', 't' => '\t', _ => '\b' };
            corrected = corrected.Replace(control + name[2..], name, StringComparison.Ordinal);
        }
        return corrected;
    }

    /// <summary>The paragraph with each formula ($…$, $$…$$, \(…\), \[…\], a LaTeX equation environment) as the word
    /// <see cref="Prose.Placeholder"/>, and where each formula was: its place in the short text, its offset and length in
    /// the paragraph.</summary>
    internal static (string Text, List<(int At, int Offset, int Length)> Formulas) WithoutFormulas(string text)
    {
        var sb = new StringBuilder();
        var formulas = new List<(int At, int Offset, int Length)>();
        int from = 0;
        foreach (System.Text.RegularExpressions.Match m in FormulaRx().Matches(text))
        {
            sb.Append(text, from, m.Index - from);
            formulas.Add((sb.Length, m.Index, m.Length));
            sb.Append(Prose.Placeholder);
            from = m.Index + m.Length;
        }
        sb.Append(text, from, text.Length - from);
        return (sb.ToString(), formulas);
    }

    /// <summary>Findings on the text of <see cref="WithoutFormulas"/> moved back to the paragraph; one that touches a
    /// formula's placeholder is dropped, as a finding touching a formula is (#48).</summary>
    internal static List<GrammarIssue> MapBack(IEnumerable<GrammarIssue> issues, List<(int At, int Offset, int Length)> formulas)
    {
        var res = new List<GrammarIssue>();
        foreach (var x in issues)
        {
            if (Prose.Touches(formulas.Select(f => (f.At, 1)), x.Offset, x.Length)) continue;
            int shift = formulas.Where(f => f.At < x.Offset).Sum(f => f.Length - 1);
            res.Add(x with { Offset = x.Offset + shift });
        }
        return res;
    }

    [System.Text.RegularExpressions.GeneratedRegex(
        @"\$\$.+?\$\$|(?<!\\)\$[^$\s][^$]*?(?<!\s)\$|\\\(.+?\\\)|\\\[.+?\\\]|\\begin\{(equation|align|gather|multline|eqnarray)\*?\}.*?\\end\{\1\*?\}",
        System.Text.RegularExpressions.RegexOptions.Singleline)]
    private static partial System.Text.RegularExpressions.Regex FormulaRx();

    // \frac, \neq, \right, \times, \beta...: a backslash, one of JSON's escape letters f n r t b, more letters
    [System.Text.RegularExpressions.GeneratedRegex(@"\\[fnrtb][A-Za-z]+")]
    private static partial System.Text.RegularExpressions.Regex LatexEscapeRx();

    static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    public void Dispose()
    {
        _work.CompleteAdding();
        if (_ownsServer) _server?.Dispose();                     // a shared server is its owner's to stop
    }
}

/// <summary>Several grammar checkers at once (LanguageTool and a language model...): their findings together.</summary>
sealed class GrammarCheckers(IReadOnlyList<IGrammarChecker> all) : IGrammarChecker
{
    public IReadOnlyList<IGrammarChecker> All => all;
    public string Name => string.Join(" + ", all.Select(c => c.Name));
    public bool Available => all.Any(c => c.Available);
    public string? Status => all.Any(c => c.Status == "starting") ? "starting"
        : all.Where(c => !c.Available).Select(c => c.Name + ": " + c.Status).FirstOrDefault();
    public int Version => all.Sum(c => c.Version);

    public IReadOnlyList<GrammarIssue>? Issues(string paragraph, string language, bool queue = true) => Issues(_ => paragraph, language, queue);

    public IReadOnlyList<GrammarIssue>? Issues(Func<MathView, string> paragraph, string language, bool queue = true)
    {
        List<GrammarIssue>? res = null;
        foreach (var c in all)
        {
            if (!c.Available || c.Issues(paragraph(c.Formulas), language, queue) is not { } found) continue;
            res ??= new();
            // overlapping findings of different checkers are all kept: F7 offers what each of them suggests
            // (LanguageTool often explains without a fix, a language model gives the fix)
            foreach (var i in found)
                if (!res.Any(r => r.Offset == i.Offset && r.Length == i.Length && r.Replacements.SequenceEqual(i.Replacements))) res.Add(i);
        }
        return res;
    }

    public void Dispose() { foreach (var c in all) c.Dispose(); }
}
