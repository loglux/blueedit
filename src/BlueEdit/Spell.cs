using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

/// <summary>
/// Spell checking through the Windows Spell Checking API (the same dictionaries Edge, Notepad etc. use): the default
/// provider, in-process and instant. A word is accepted if ANY enabled language accepts it, so mixed Russian/English
/// text works. The static helpers (Windows languages, tags) serve every provider.
/// </summary>
sealed class Spell : ISpellProvider
{
    public string Name => "Windows";
    readonly UserDictionary? _user;
    readonly HashSet<string> _ignored = new(StringComparer.Ordinal);
    // ---- COM interfaces (vtable order matters; only the prefix we need is declared) ----
    [ComImport, Guid("8E018A9D-2415-4677-BF08-794EA61F94BB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISpellCheckerFactory
    {
        IEnumString SupportedLanguages { [return: MarshalAs(UnmanagedType.Interface)] get; }
        [return: MarshalAs(UnmanagedType.Bool)]
        bool IsSupported([MarshalAs(UnmanagedType.LPWStr)] string languageTag);
        ISpellChecker CreateSpellChecker([MarshalAs(UnmanagedType.LPWStr)] string languageTag);
    }

    [ComImport, Guid("E7ED1C71-87F7-4378-A840-C9200DACEE47"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISpellChecker
    {
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetLanguageTag();
        IEnumSpellingError Check([MarshalAs(UnmanagedType.LPWStr)] string text);
        IEnumString Suggest([MarshalAs(UnmanagedType.LPWStr)] string word);
        void Add([MarshalAs(UnmanagedType.LPWStr)] string word);
        void Ignore([MarshalAs(UnmanagedType.LPWStr)] string word);
    }

    [ComImport, Guid("803E3BD4-2828-4410-8290-418D1D73C762"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IEnumSpellingError
    {
        [PreserveSig] int Next([MarshalAs(UnmanagedType.Interface)] out ISpellingError? error);
    }

    [ComImport, Guid("B7C82D61-FBE8-4B47-9B27-6C0D2E0DE0A3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISpellingError { }

    static readonly Guid ClsidFactory = new("7AB36653-1796-484B-BDFA-E74F1DB7C1DC");

    readonly List<(string Tag, ISpellChecker Checker)> _checkers = new();
    readonly Dictionary<string, bool> _cache = new(StringComparer.Ordinal);

    public bool Available => _checkers.Count > 0;
    public int Version { get; private set; }
    public IReadOnlyList<string> Languages => _checkers.Select(c => c.Tag).ToList();

    public Spell(params string[] wanted) : this(null, wanted) { }

    public Spell(UserDictionary? user, params string[] wanted)
    {
        _user = user;
        try
        {
            var type = Type.GetTypeFromCLSID(ClsidFactory);
            if (type == null) return;
            var factory = (ISpellCheckerFactory)Activator.CreateInstance(type)!;
            foreach (var tag in wanted)
            {
                try
                {
                    if (factory.IsSupported(tag)) _checkers.Add((tag, factory.CreateSpellChecker(tag)));
                }
                catch (Exception ex) { Error += $"{tag}: {ex.GetType().Name} {ex.Message}\n"; }
            }
        }
        catch (Exception ex) { Error += ex.GetType().Name + " " + ex.Message; }   // no spell checking: everything counts as correct
    }

    public string? Error { get; private set; }

    /// <summary>
    /// The languages added in Windows (Settings > Time &amp; language > Language, in their order) that Windows can
    /// spell-check: what "spellLanguages": "auto" uses. Empty when there are none or Windows cannot say.
    /// </summary>
    public static List<string> WindowsLanguages()
    {
        var res = new List<string>();
        if (!OperatingSystem.IsWindows()) return res;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\International\User Profile");
            if (key?.GetValue("Languages") is not string[] tags) return res;
            var type = Type.GetTypeFromCLSID(ClsidFactory)!;
            var factory = (ISpellCheckerFactory)Activator.CreateInstance(type)!;
            foreach (var tag in tags)
                if (!res.Contains(tag, StringComparer.OrdinalIgnoreCase) && factory.IsSupported(tag)) res.Add(tag);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or System.Security.SecurityException or IOException) { }
        return res;
    }

    /// <summary>The system's languages, what "spellLanguages": "auto" uses: on Windows <see cref="WindowsLanguages"/>,
    /// elsewhere <see cref="LocaleLanguages"/> of the environment.</summary>
    public static List<string> SystemLanguages() =>
        OperatingSystem.IsWindows() ? WindowsLanguages() : LocaleLanguages(Environment.GetEnvironmentVariable);

    /// <summary>The languages of a Unix locale as tags: LANGUAGE's list ("de_DE:en_GB:en"), else the first of LC_ALL,
    /// LC_MESSAGES and LANG ("de_DE.UTF-8" → "de-DE"). Only a language with its country counts (dictionaries are
    /// per country); C and POSIX, a server's usual locale, name none.</summary>
    public static List<string> LocaleLanguages(Func<string, string?> env)
    {
        var res = new List<string>();
        var names = env("LANGUAGE") is { Length: > 0 } list ? list.Split(':')
            : new[] { "LC_ALL", "LC_MESSAGES", "LANG" }.Select(env).OfType<string>().Where(v => v.Length > 0).Take(1).ToArray();
        foreach (var name in names)
        {
            var bare = name.Split('.', '@')[0];
            var parts = bare.Split('_');
            if (parts.Length != 2 || parts[0].Length is < 2 or > 3 || parts[1].Length != 2) continue;
            var tag = parts[0].ToLowerInvariant() + "-" + parts[1].ToUpperInvariant();
            if (!res.Contains(tag)) res.Add(tag);
        }
        return res;
    }

    /// <summary>Short form for the status line: "ru" for ru-RU (the language's main country), "en-GB" kept whole.</summary>
    public static string ShortTag(string tag)
    {
        var parts = tag.Split('-');
        return parts.Length == 2 && string.Equals(parts[0], parts[1], StringComparison.OrdinalIgnoreCase) ? parts[0] : tag;
    }

    /// <summary>"English (United Kingdom)" for "en-GB"; the tag itself when Windows does not know it.</summary>
    public static string DisplayName(string tag)
    {
        try { return System.Globalization.CultureInfo.GetCultureInfo(tag).EnglishName; }
        catch (System.Globalization.CultureNotFoundException) { return tag; }
    }

    /// <summary><see cref="DisplayName"/> for a list that shows the tag already: empty when it is only the tag again
    /// (no ICU on Linux, the languages unnamed).</summary>
    public static string NameBesideTag(string tag) => DisplayName(tag) is var name && name != tag ? name : "";

    /// <summary>Tags of every language Windows can spell-check (for diagnostics).</summary>
    public static List<string> SupportedTags()
    {
        var res = new List<string>();
        try
        {
            var type = Type.GetTypeFromCLSID(ClsidFactory)!;
            var factory = (ISpellCheckerFactory)Activator.CreateInstance(type)!;
            var e = factory.SupportedLanguages;
            var buf = new string[1];
            while (e.Next(1, buf, IntPtr.Zero) == 0) res.Add(buf[0]);
        }
        catch (Exception ex) when (ComFailed(ex)) { }
        return res;
    }

    public bool? IsCorrect(string word)
    {
        if (_checkers.Count == 0 || word.Length < 2) return true;
        if (_ignored.Contains(word) || _user?.Contains(word) == true) return true;
        if (_cache.TryGetValue(word, out var known)) return known;
        bool ok = !Scripts.Readable(_checkers.Select(c => c.Tag), word);
        foreach (var (_, c) in _checkers)
        {
            if (ok || !HasError(c, word)) { ok = true; break; }
        }
        _cache[word] = ok;
        return ok;
    }

    /// <summary>What a call into Windows' spell checking may throw when it cannot answer: a failed HRESULT comes as
    /// COMException or as the .NET exception it maps to (E_INVALIDARG ArgumentException, E_ACCESSDENIED
    /// UnauthorizedAccessException, E_NOTIMPL NotImplementedException). The word then counts as correct, with no
    /// suggestions; anything else is a bug and is not swallowed.</summary>
    static bool ComFailed(Exception ex) => ex is COMException or InvalidCastException or System.Security.SecurityException
        or ArgumentException or UnauthorizedAccessException or NotImplementedException;

    static bool HasError(ISpellChecker c, string word)
    {
        try
        {
            var errors = c.Check(word);
            return errors.Next(out var err) == 0 && err != null;     // S_OK means at least one error
        }
        catch (Exception ex) when (ComFailed(ex)) { return false; }
    }

    public List<string> Suggest(string word)
    {
        var res = new List<string>();
        foreach (var (_, c) in _checkers)
        {
            try
            {
                var e = c.Suggest(word); var buf = new string[1];
                while (e.Next(1, buf, IntPtr.Zero) == 0 && res.Count < 12) if (!res.Contains(buf[0])) res.Add(buf[0]);
            }
            catch (Exception ex) when (ComFailed(ex)) { }
            if (res.Count > 0) break;      // suggestions from the language that knows the word best are enough
        }
        return res;
    }

    /// <summary>Into the editor's own dictionary (see <see cref="UserDictionary"/>); without one, into Windows'.</summary>
    public void AddToDictionary(string word)
    {
        if (_user != null) _user.Add(word);
        else foreach (var (_, c) in _checkers) try { c.Add(word); } catch (Exception ex) when (ComFailed(ex)) { }
        _cache.Remove(word); Version++;
    }

    /// <summary>Accepted until the editor closes.</summary>
    public void Ignore(string word)
    {
        _ignored.Add(word);
        _cache.Remove(word); Version++;
    }

    public void Dispose() { }
}
