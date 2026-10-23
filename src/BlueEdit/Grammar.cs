using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

/// <summary>One grammar or style problem in a paragraph: where it is, what is wrong, how to fix it, and which
/// checker said so (shown to the user, e.g. "LanguageTool" or "Gemma 3 12B").</summary>
sealed record GrammarIssue(int Offset, int Length, string Message, IReadOnlyList<string> Replacements, string Rule, string Source = "", string Category = "");

/// <summary>LanguageTool's rules as the user set them (settings languageToolDisabledRules, ...DisabledCategories,
/// ...EnabledRules, languageToolLevel): which are off, which off-by-default ones are on, "default" or "picky".</summary>
sealed record LanguageToolOptions(IReadOnlyList<string> DisabledRules, IReadOnlyList<string> DisabledCategories, IReadOnlyList<string> EnabledRules, string Level, string Memory = LanguageToolOptions.DefaultMemory)
{
    // the Oxford -ize suggestions flag correct British -ise. TYPOS stays on: besides the dictionary it holds then/than
    // and dont → don't (#105); the dictionary's own rules are left out by IsDictionaryRule instead
    public static readonly string[] DefaultDisabledCategories = { "BRE_STYLE_OXFORD_SPELLING" };
    /// <summary>The default before #105, which a settings file may still hold word for word: read as today's default.</summary>
    public static readonly string[] OldDefaultDisabledCategories = { "TYPOS", "BRE_STYLE_OXFORD_SPELLING" };

    /// <summary>A dictionary rule (MORFOLOGIK_RULE_EN_GB, GERMAN_SPELLER_RULE, HUNSPELL_…): spelling is the spelling
    /// provider's job, and the dictionary flags names and terms. Recognised by the name's form, for any language.</summary>
    public static bool IsDictionaryRule(string id) =>
        id.StartsWith("MORFOLOGIK_RULE_", StringComparison.Ordinal) || id.EndsWith("SPELLER_RULE", StringComparison.Ordinal)
        || id.StartsWith("HUNSPELL_", StringComparison.Ordinal);
    // runs of spaces are the editor's own (markup is blanked to keep offsets): CONSECUTIVE_SPACES made two thirds of
    // all findings on the user's notes (64 of 89, #48); the others are about spacing and sentence starts the same way
    public static readonly string[] DefaultDisabledRules = { "WHITESPACE_RULE", "CONSECUTIVE_SPACES", "COMMA_PARENTHESIS_WHITESPACE", "UPPERCASE_SENTENCE_START" };
    /// <summary>Java's heap for the server (-Xmx). Measured (#88): with Russian and English 512m and 1g check 400
    /// paragraphs alike (51 and 52 s); five languages in turn took 534 s at 512m (collecting garbage), 202 s at 768m,
    /// 205 s at 1g. No run died. 768m: the speed of 1g, ~100 MB less.</summary>
    public const string DefaultMemory = "768m";
    public static readonly LanguageToolOptions Default = new(DefaultDisabledRules, DefaultDisabledCategories, Array.Empty<string>(), "default");
    /// <summary>The same options (for recreating the checker only when they changed).</summary>
    public string Key => string.Join(",", DisabledRules) + "|" + string.Join(",", DisabledCategories) + "|" + string.Join(",", EnabledRules) + "|" + Level + "|" + Memory;
}

/// <summary>Checks paragraphs of prose for grammar. Slow, so answers come in the background.</summary>
interface IGrammarChecker : IDisposable
{
    string Name { get; }
    bool Available { get; }
    /// <summary>Starting up, a problem, or null when working.</summary>
    string? Status { get; }
    /// <summary>The problems of a paragraph, or null while it is not checked yet. With <paramref name="queue"/> an
    /// unknown paragraph is sent for checking (not while it is still being typed).</summary>
    IReadOnlyList<GrammarIssue>? Issues(string paragraph, string language, bool queue = true);
    /// <summary>How this checker gets formulas. As they are, for every checker so far: measured in #48
    /// (tests/BlueEdit.Tests/MathBench.cs), a placeholder word found nothing more and added false alarms ("a X" → "an X"),
    /// for LanguageTool as for the models. Findings touching a formula are dropped by the editor either way.</summary>
    MathView Formulas => MathView.Source;
    /// <summary>The same with the paragraph in every form (<paramref name="paragraph"/> gives it for a <see cref="MathView"/>):
    /// each checker takes its own.</summary>
    IReadOnlyList<GrammarIssue>? Issues(Func<MathView, string> paragraph, string language, bool queue = true) =>
        Issues(paragraph(Formulas), language, queue);
    /// <summary>Why the last paragraph that could not be checked was not (a timeout, a broken answer), or null. Such a
    /// paragraph is that paragraph's problem: the checker goes on (before, one failure switched it off for the session).</summary>
    string? LastFailure => null;
    /// <summary>Goes up whenever new answers arrive.</summary>
    int Version { get; }
}

/// <summary>
/// LanguageTool (languagetool.org) running as a local server: grammar and style for Russian, English and some 30 other
/// languages, nothing leaves the machine. The editor starts the server (Java) on first use from
/// %LOCALAPPDATA%\BlueEdit\{jre,languagetool}, talks to it over 127.0.0.1 (not "localhost": Windows tries IPv6
/// first and loses 2 s on every request), and stops it on exit. A Windows job object ends the server even if the
/// editor itself is killed. Spelling is left to the spelling provider (LanguageTool's spelling rules are off).
/// </summary>
sealed class LanguageToolChecker : IGrammarChecker
{
    public string Name => "LanguageTool";

    readonly string _home;
    readonly ConcurrentDictionary<(string, string), IReadOnlyList<GrammarIssue>> _done = new();
    readonly ConcurrentDictionary<(string, string), byte> _queued = new();
    readonly BlockingCollection<(string Text, string Lang)> _work = new();
    readonly Thread? _thread;
    readonly HttpClient _http = Net.Client(TimeSpan.FromSeconds(30));
    Process? _server;
    IntPtr _job;
    int _version;
    volatile string? _status;

    /// <summary>Where the portable Java and LanguageTool live.</summary>
    public static string DefaultHome => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), "BlueEdit");

    static string JavaIn(string home) => Path.Combine(home, "jre", "bin", LlmServer.Exe("java"));
    static string ServerJarIn(string home) => Path.Combine(home, "languagetool", "languagetool-server.jar");

    /// <summary>Java and the LanguageTool server are there.</summary>
    public static bool InstalledIn(string home) => File.Exists(JavaIn(home)) && File.Exists(ServerJarIn(home));

    readonly LanguageToolOptions _options;
    public LanguageToolOptions Options => _options;

    public LanguageToolChecker(string home, LanguageToolOptions? options = null)
    {
        _home = home;
        _options = options ?? LanguageToolOptions.Default;
        if (!InstalledIn(home)) { _status = $"LanguageTool not installed in {home}"; return; }
        _status = "starting";
        _thread = new Thread(Loop) { IsBackground = true, Name = "LanguageTool" };
        _thread.Start();
    }

    // off only when the server could not start; a failed request is that paragraph's (LastFailure)
    public bool Available => _thread != null && !_broken;
    volatile bool _broken;
    public string? LastFailure { get; private set; }
    public string? Status => _status;

    /// <summary>The Java server's process id once it was started (tests: it must be gone after Dispose).</summary>
    internal int? ServerProcessId { get { try { return _server?.Id; } catch (InvalidOperationException) { return null; } } }
    public int Version => Volatile.Read(ref _version);

    public IReadOnlyList<GrammarIssue>? Issues(string paragraph, string language, bool queue = true)
    {
        if (!Available || paragraph.Trim().Length == 0) return Array.Empty<GrammarIssue>();
        var key = (paragraph, language);
        if (_done.TryGetValue(key, out var issues))
            return issues;
        if (queue && _queued.TryAdd(key, 0) && !_work.IsAddingCompleted) _work.Add(key);
        return null;
    }


    // ---------- the worker thread ----------
    void Loop()
    {
        string? baseUrl = null;
        try
        {
            baseUrl = StartServer();
            _status = null;
            Interlocked.Increment(ref _version);
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
            _status = "LanguageTool did not start: " + ex.Message;
            _broken = true;
            Interlocked.Increment(ref _version);
            return;
        }
        foreach (var (text, lang) in _work.GetConsumingEnumerable())
        {
            try { _done[(text, lang)] = Check(baseUrl, text, lang); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                _done[(text, lang)] = Array.Empty<GrammarIssue>();
                LastFailure = "LanguageTool: " + ex.Message;
            }
            _queued.TryRemove((text, lang), out _);
            Interlocked.Increment(ref _version);
        }
    }

    string StartServer()
    {
        int port;
        using (var probe = new TcpListener(IPAddress.Loopback, 0)) { probe.Start(); port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop(); }
        var psi = new ProcessStartInfo(JavaIn(_home))
        {
            WorkingDirectory = Path.GetDirectoryName(ServerJarIn(_home))!,
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var a in new[] { "-Xmx" + _options.Memory, "-cp", "languagetool-server.jar", "org.languagetool.server.HTTPServer", "--port", port.ToString(System.Globalization.CultureInfo.InvariantCulture) })
            psi.ArgumentList.Add(a);
        _server = Process.Start(psi) ?? throw new InvalidOperationException("java did not start");
        _server.OutputDataReceived += (_, _) => { };
        _server.ErrorDataReceived += (_, _) => { };
        _server.BeginOutputReadLine(); _server.BeginErrorReadLine();
        _job = JobObject.KillOnClose(_server);
        var url = $"http://127.0.0.1:{port}/v2/check";
        var sw = Stopwatch.StartNew();
        while (true)                                         // ready when it answers a check (about 6 s)
        {
            if (_server.HasExited) throw new InvalidOperationException($"java exited with code {_server.ExitCode}");
            try { Check(url, "ok", "en-GB"); return url; }
            catch (HttpRequestException) when (sw.Elapsed < TimeSpan.FromSeconds(90)) { Thread.Sleep(250); }
        }
    }

    List<GrammarIssue> Check(string url, string text, string lang)
    {
        var form = new Dictionary<string, string>
        {
            ["text"] = text,
            ["language"] = lang,
        };
        if (_options.DisabledCategories.Count > 0) form["disabledCategories"] = string.Join(",", _options.DisabledCategories);
        if (_options.DisabledRules.Count > 0) form["disabledRules"] = string.Join(",", _options.DisabledRules);
        if (_options.EnabledRules.Count > 0) form["enabledRules"] = string.Join(",", _options.EnabledRules);
        if (_options.Level == "picky") form["level"] = "picky";
        using var content = new FormUrlEncodedContent(form);
        using var resp = _http.PostAsync(new Uri(url), content).GetAwaiter().GetResult();
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(resp.Content.ReadAsStream());
        var res = new List<GrammarIssue>();
        foreach (var m in doc.RootElement.GetProperty("matches").EnumerateArray())
        {
            if (LanguageToolOptions.IsDictionaryRule(m.GetProperty("rule").GetProperty("id").GetString() ?? "")) continue;
            var repl = m.GetProperty("replacements").EnumerateArray().Select(r => r.GetProperty("value").GetString() ?? "").Take(8).ToList();
            res.Add(new GrammarIssue(m.GetProperty("offset").GetInt32(), m.GetProperty("length").GetInt32(),
                m.GetProperty("message").GetString() ?? "", repl, m.GetProperty("rule").GetProperty("id").GetString() ?? "", "LanguageTool",
                m.GetProperty("rule").TryGetProperty("category", out var cat) && cat.TryGetProperty("id", out var cid) ? cid.GetString() ?? "" : ""));
        }
        return res;
    }

    public void Dispose()
    {
        _work.CompleteAdding();
        try { if (_server is { HasExited: false }) _server.Kill(); } catch (InvalidOperationException) { }
        _server?.Dispose();
        if (_job != IntPtr.Zero) { JobObject.Close(_job); _job = IntPtr.Zero; }
        _http.Dispose();
    }
}

/// <summary>A Windows job object that ends its process when the editor goes away, however it goes.</summary>
static partial class JobObject
{
    [StructLayout(LayoutKind.Sequential)]
    struct BasicLimits { public long PerProcessUserTimeLimit, PerJobUserTimeLimit; public uint LimitFlags; public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize; public uint ActiveProcessLimit; public UIntPtr Affinity; public uint PriorityClass, SchedulingClass; }
    [StructLayout(LayoutKind.Sequential)]
    struct IoCounters { public ulong a, b, c, d, e, f; }
    [StructLayout(LayoutKind.Sequential)]
    struct ExtendedLimits { public BasicLimits Basic; public IoCounters Io; public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed; }

    const uint KillOnJobClose = 0x2000;
    const int ExtendedLimitInformation = 9;

    [LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateJobObject(IntPtr attributes, string? name);
    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(IntPtr job, int infoClass, ref ExtendedLimits info, uint length);
    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);

    /// <summary>Elsewhere there is no such object for a process started by .NET (prctl's parent-death signal is set in
    /// the child): the owners' Dispose ends their processes on a normal exit, a killed editor leaves them running.</summary>
    public static IntPtr KillOnClose(Process p)
    {
        if (!OperatingSystem.IsWindows()) return IntPtr.Zero;
        var job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero) return IntPtr.Zero;
        var info = new ExtendedLimits { Basic = new BasicLimits { LimitFlags = KillOnJobClose } };
        SetInformationJobObject(job, ExtendedLimitInformation, ref info, (uint)Marshal.SizeOf<ExtendedLimits>());
        AssignProcessToJobObject(job, p.Handle);
        return job;
    }

    public static void Close(IntPtr job) => CloseHandle(job);
}

/// <summary>
/// Turns Markdown / plain text into the paragraphs grammar is checked on. Markup is blanked out with spaces of the
/// same length (a checker should read "**bold**" as "  bold  "), so offsets in a paragraph are offsets in the source;
/// lines are joined with '\n'. Code blocks, tables, front matter and HTML comments are not prose.
/// </summary>
static class Prose
{
    /// <summary>What stands for a formula in the text the checkers see: "где $x$ — длина" is read as "где X — длина".
    /// A capital Latin letter, as a variable is written, the rest of the formula's columns spaces (#48).</summary>
    public const char Placeholder = 'X';

    /// <summary>A formula on one line: $$…$$ (or an opening $$ that runs to the line end); $…$ as Obsidian and the
    /// highlighter see it (no space just inside the dollars, not \$; a \$ inside is a dollar sign, "$\$ 14.8$": 13 such
    /// in the user's notes were cut there, #192); \(…\) and \[…\] (mathpix and pandoc write them),
    /// or an opening \[ at the line start that runs to the line end (elsewhere \[ is an escaped bracket). $$ is tried first, so a real $$…$$ is taken whole, and "$a$$b$" is
    /// two inline formulas, as Obsidian draws them (#128; before, the second was read as an opening $$ and the words
    /// after it were not checked). Not a formula: shell code outside a code block, "$(addprefix --profile ,$(VARS))" —
    /// a $( followed by a word of three letters or more, or " --" inside (#70: 5 such of 2491 in the user's notes).</summary>
    public static readonly System.Text.RegularExpressions.Regex MathRx = new(
        @"\$\$.*?(?:\$\$|$)" +
        @"|(?<!\\)\$(?!\((?:[a-z_]{3,}|[A-Z_]{3,})[\s)])(?![^$]*\s--)(?:\\\$|[^$\s])(?:\\\$|[^$])*?(?<![\s\\])\$" +
        @"|\\\(.+?\\\)|\\\[.+?\\\]|(?<=^\s*)\\\[.*$",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Whether offset..offset+length (an empty finding counts as one character) touches one of the spans.</summary>
    public static bool Touches(IEnumerable<(int Offset, int Length)> spans, int offset, int length)
    {
        int end = offset + Math.Max(1, length);
        foreach (var (o, n) in spans) if (offset < o + n && o < end) return true;
        return false;
    }

    /// <summary>
    /// Whether a finding on <paramref name="text"/> changes one of the formula spans: what each fix leaves alone at the
    /// start and at the end of the finding is not counted (#158, found by BlueProof for Obsidian). The word diff puts a
    /// comma after a formula together with its closing "$" ("$x>0$" → "$x>0$,"), and that change was dropped as one
    /// touching the formula; it only adds a comma outside it. A mark put in at a formula's edge is outside it; one
    /// put in before its closing "$" ("b" → "b," in "$a=b$") is inside.
    /// </summary>
    public static bool ChangesFormula(IReadOnlyList<(int Offset, int Length)> spans, string text, GrammarIssue x)
    {
        if (x.Replacements.Count == 0 || x.Offset + x.Length > text.Length) return Touches(spans, x.Offset, x.Length);
        var wrong = text.Substring(x.Offset, x.Length);
        foreach (var fix in x.Replacements)
        {
            int p = 0, s = 0;
            while (p < wrong.Length && p < fix.Length && wrong[p] == fix[p]) p++;
            while (s < wrong.Length - p && s < fix.Length - p && wrong[^(s + 1)] == fix[^(s + 1)]) s++;
            int start = x.Offset + p, end = x.Offset + x.Length - s;
            if (end > start ? Touches(spans, start, end - start) : spans.Any(f => f.Offset < start && start < f.Offset + f.Length)) return true;
        }
        return false;
    }

    /// <summary>The line as a grammar checker should see it: same length, markup, web addresses, e-mail and paths
    /// turned into spaces.</summary>
    public static string Plain(string line, bool markdown, bool latex = false)
    {
        if (latex) return LatexPlain(line);
        char[] chars;
        var d = markdown ? Conceal.Get(line) : null;
        if (d == null) chars = line.ToCharArray();
        else
        {
            chars = new char[line.Length];
            Array.Fill(chars, ' ');
            for (int i = 0; i < d.Map.Length; i++)
            {
                int src = d.Map[i];
                if (d.Over[i] == null && src < line.Length && d.Text[i] == line[src]) chars[src] = line[src];
            }
        }
        foreach (System.Text.RegularExpressions.Match m in EditorView.NotProseRx.Matches(line))
            Array.Fill(chars, ' ', m.Index, m.Length);
        return new string(chars);
    }

    // LaTeX (#78), as TeXtidote and LanguageTool's LaTeX support read it: a command keeps the text of its argument
    // (\emph{…}, \section{…}, \footnote{…}...) unless it is one of these

    /// <summary>Commands whose argument is not prose (a citation, a reference, a label, a file): the command and all its
    /// arguments stand like a formula — "see Section~\ref{s}" is read with the reference as one word.</summary>
    public static readonly HashSet<string> ReferenceCommands = new(StringComparer.Ordinal)
    {
        "cite", "citep", "citet", "citeauthor", "citeyear", "parencite", "textcite", "autocite", "footcite", "nocite",
        "ref", "eqref", "pageref", "autoref", "cref", "Cref", "vref", "nameref", "label", "url", "href", "hyperref",
        "includegraphics", "input", "include",
    };

    /// <summary>A LaTeX command with its arguments: \name, a star, then [optional] and {required} ones (one level of
    /// nested braces).</summary>
    public static readonly System.Text.RegularExpressions.Regex LatexCommandRx = new(
        @"\\([A-Za-z@]+)\*?((?:\s*\[[^\]]*\]|\s*\{(?:[^{}]|\{[^{}]*\})*\})*)",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>A LaTeX line as a checker should see it, same length: a comment, commands, braces and the arguments of
    /// <see cref="ReferenceCommands"/> as spaces; the text inside \emph{…}, \section{…}… kept; \% \& \_ \# \$ as the sign;
    /// ~ (a space that does not break) a space. Formulas are left to <see cref="MathRx"/>, as in Markdown.</summary>
    public static string LatexPlain(string line)
    {
        var chars = line.ToCharArray();
        int comment = LatexComment(line);
        if (comment >= 0) Array.Fill(chars, ' ', comment, line.Length - comment);
        var code = comment >= 0 ? line[..comment] : line;
        foreach (System.Text.RegularExpressions.Match m in LatexCommandRx.Matches(code))
        {
            var name = m.Groups[1].Value;
            if (ReferenceCommands.Contains(name) || name is "begin" or "end" or "documentclass" or "usepackage" or "newcommand"
                    or "renewcommand" or "bibliography" or "bibliographystyle" or "maketitle" or "tableofcontents")
            {
                Array.Fill(chars, ' ', m.Index, m.Length);
                continue;
            }
            // the command's name (and star) goes; for text commands the last {argument}'s text stays, its braces go,
            // optional [arguments] go; another command's {arguments} stay as text ("\textcolor{red}{word}" keeps both)
            int nameEnd = m.Groups[2].Index;
            Array.Fill(chars, ' ', m.Index, nameEnd - m.Index);
            foreach (System.Text.RegularExpressions.Match opt in System.Text.RegularExpressions.Regex.Matches(m.Groups[2].Value, @"\[[^\]]*\]"))
                Array.Fill(chars, ' ', nameEnd + opt.Index, opt.Length);
        }
        for (int i = 0; i < code.Length; i++)
        {
            char c = code[i];
            if (c == '\\' && i + 1 < code.Length && "%&_#${}".Contains(code[i + 1], StringComparison.Ordinal)) { chars[i] = ' '; i++; continue; }
            if (c == '\\' && i + 1 < code.Length && code[i + 1] == '\\') { chars[i] = chars[i + 1] = ' '; i++; continue; }   // a line break
            if (c is '{' or '}') chars[i] = ' ';
            else if (c == '~') chars[i] = ' ';
        }
        return new string(chars);
    }

    /// <summary>Where a LaTeX comment starts (an unescaped %), or -1.</summary>
    public static int LatexComment(string line)
    {
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '\\') { i++; continue; }
            if (line[i] == '%') return i;
        }
        return -1;
    }

}
