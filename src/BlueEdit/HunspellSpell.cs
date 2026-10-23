using WeCantSpell.Hunspell;

/// <summary>
/// Spelling with Hunspell dictionaries (the ones LibreOffice and Firefox use: ru_RU.dic + ru_RU.aff) through
/// WeCantSpell.Hunspell, a managed port of Hunspell. The spelling provider on Linux and macOS, where the system has the
/// dictionaries (apt install hunspell-ru hunspell-en-gb) or the editor downloads LibreOffice's (ProofingTools); "hunspell" in the settings picks it on Windows too.
/// A dictionary takes a moment to load, so they load in the background: until then every word counts as correct
/// (null from <see cref="IsCorrect"/>), and <see cref="Version"/> goes up when they are ready.
/// </summary>
sealed class HunspellSpell : ISpellProvider
{
    public string Name => "Hunspell";
    readonly UserDictionary? _user;
    readonly HashSet<string> _ignored = new(StringComparer.Ordinal);
    readonly Dictionary<string, bool> _cache = new(StringComparer.Ordinal);
    readonly List<string> _missing = new();
    volatile List<(string Tag, WordList Words)>? _lists;         // null while loading
    volatile int _version;

    public HunspellSpell(UserDictionary? user, IEnumerable<string> wanted, IEnumerable<string>? folders = null)
    {
        _user = user;
        var dirs = (folders ?? DefaultFolders()).ToList();
        var found = new List<(string Tag, string Dic)>();
        foreach (var tag in wanted)
        {
            if (FileFor(tag, dirs) is { } dic) found.Add((tag, dic));
            else _missing.Add(tag);
        }
        if (_missing.Count > 0)
            Error = "no Hunspell dictionary for " + string.Join(", ", _missing) + " in " + string.Join(", ", dirs)
                + " (Tools > Get spelling dictionaries)";
        if (found.Count == 0) { _lists = new(); return; }
        Languages = found.Select(f => f.Tag).ToList();
        // a thread of its own: queued on the thread pool, the load waited for a free thread past the tests' 10 s
        // while parallel tests kept the pool busy (twice on the runners and once here)
        Task.Factory.StartNew(() =>
        {
            var lists = new List<(string, WordList)>();
            foreach (var (tag, dic) in found)
            {
                try { lists.Add((tag, WordList.CreateFromFiles(dic))); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or FormatException)
                { Error += $"\n{tag}: {ex.Message}"; }
            }
            _lists = lists;
            _version++;
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    /// <summary>Where Linux and macOS keep Hunspell dictionaries (the system's, then the user's), and BlueEdit's own
    /// folder (%LOCALAPPDATA%\BlueEdit\hunspell on Windows).</summary>
    public static IEnumerable<string> DefaultFolders()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), "BlueEdit", "hunspell");
        if (OperatingSystem.IsWindows()) yield break;
        yield return Path.Combine(home, ".local", "share", "hunspell");
        yield return "/usr/share/hunspell";
        yield return "/usr/share/myspell/dicts";
        yield return "/usr/share/myspell";
        yield return "/usr/local/share/hunspell";
        yield return "/opt/homebrew/share/hunspell";
        yield return Path.Combine(home, "Library", "Spelling");
    }

    /// <summary>The dictionary for the language in any of the folders, or null.</summary>
    public static string? Find(string tag) => FileFor(tag, DefaultFolders().ToList());

    /// <summary>ru-RU → ru_RU.dic (with its .aff next to it); a bare "ru" also finds ru_RU.dic.</summary>
    static string? FileFor(string tag, List<string> dirs)
    {
        var name = tag.Replace('-', '_');
        foreach (var d in dirs)
        {
            try
            {
                if (!Directory.Exists(d)) continue;
                var exact = Path.Combine(d, name + ".dic");
                if (File.Exists(exact) && File.Exists(Path.ChangeExtension(exact, ".aff"))) return exact;
                if (!name.Contains('_', StringComparison.Ordinal))
                    foreach (var f in Directory.EnumerateFiles(d, name + "_*.dic").Order(StringComparer.Ordinal))
                        if (File.Exists(Path.ChangeExtension(f, ".aff"))) return f;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return null;
    }

    /// <summary>The languages Hunspell has dictionaries for here ("ru-RU", "en-GB"...), for the menu.</summary>
    public static List<string> InstalledTags(IEnumerable<string>? folders = null)
    {
        var tags = new List<string>();
        foreach (var d in folders ?? DefaultFolders())
        {
            try
            {
                if (!Directory.Exists(d)) continue;
                foreach (var f in Directory.EnumerateFiles(d, "*.dic"))
                {
                    var tag = Path.GetFileNameWithoutExtension(f).Replace('_', '-');
                    if (File.Exists(Path.ChangeExtension(f, ".aff")) && !tags.Contains(tag, StringComparer.OrdinalIgnoreCase)) tags.Add(tag);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        tags.Sort(StringComparer.Ordinal);
        return tags;
    }

    public bool Available => Languages.Count > 0;
    public string? Error { get; private set; }
    public IReadOnlyList<string> Languages { get; } = new List<string>();
    public int Version => _version;

    /// <summary>Loaded: all dictionaries are in memory (tests wait for this).</summary>
    public bool Loaded => _lists != null;

    public bool? IsCorrect(string word)
    {
        if (word.Length < 2 || _ignored.Contains(word) || _user?.Contains(word) == true) return true;
        if (_lists is not { } lists) return null;
        if (lists.Count == 0) return true;
        if (_cache.TryGetValue(word, out var known)) return known;
        bool ok = !Scripts.Readable(Languages, word) || lists.Any(l => l.Words.Check(word));
        _cache[word] = ok;
        return ok;
    }

    public List<string> Suggest(string word)
    {
        var res = new List<string>();
        if (_lists is not { } lists) return res;
        foreach (var (_, words) in lists)
        {
            foreach (var s in words.Suggest(word))
                if (res.Count < 12 && !res.Contains(s)) res.Add(s);
            if (res.Count > 0) break;      // suggestions from the language that knows the word best are enough
        }
        return res;
    }

    /// <summary>Into the editor's own dictionary (Hunspell's files are not written).</summary>
    public void AddToDictionary(string word)
    {
        _user?.Add(word);
        _ignored.Add(word);
        _cache.Remove(word); _version++;
    }

    /// <summary>Accepted until the editor closes.</summary>
    public void Ignore(string word)
    {
        _ignored.Add(word);
        _cache.Remove(word); _version++;
    }

    public void Dispose() { }
}
