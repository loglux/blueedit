/// <summary>
/// A source of spelling answers: the Windows spell checker (instant, in-process) or Microsoft Office (slow, so it
/// answers in the background). Both share the editor's own word list (<see cref="UserDictionary"/>).
/// </summary>
interface ISpellProvider : IDisposable
{
    /// <summary>"Windows" or "Office", for the status line and the menu.</summary>
    string Name { get; }
    bool Available { get; }
    /// <summary>Why it is not available, or problems with single languages.</summary>
    string? Error { get; }
    IReadOnlyList<string> Languages { get; }

    /// <summary>True or false, or null while the answer is still being worked out (shown as correct meanwhile).</summary>
    bool? IsCorrect(string word);
    /// <summary>Replacements, best first. May take a moment (it is asked for by F7, not while drawing).</summary>
    List<string> Suggest(string word);
    void AddToDictionary(string word);
    void Ignore(string word);

    /// <summary>Goes up whenever earlier answers may have changed (new answers arrived, a word was added).</summary>
    int Version { get; }
}

/// <summary>
/// Whether any spelling language is written in a word's alphabet. A word no spelling language can read ("мир" with only
/// en-US, as on a server whose locale names no language) is not a mistake but unchecked: every provider counts it
/// correct instead of underlining a whole language. The alphabets are ParagraphLanguage's: a letter's script
/// (ScriptOfLetter) and a language's (ScriptOf).
/// </summary>
static class Scripts
{
    /// <summary>True when one of the languages is written in the alphabet of all the word's letters. A word mixing
    /// alphabets (a layout slip, see <see cref="Lookalike"/>) is checked as before; one with letters of a script
    /// ParagraphLanguage does not know is not.</summary>
    public static bool Readable(IEnumerable<string> languages, string word)
    {
        Script? script = null;
        foreach (char c in word)
        {
            if (!char.IsLetter(c)) continue;
            if (ParagraphLanguage.ScriptOfLetter(c) is not { } s) return false;
            if (script != null && script != s) return true;
            script = s;
        }
        return script is not { } found || languages.Any(l => ParagraphLanguage.ScriptOf(l) == found);
    }
}

/// <summary>
/// Words the user added with F7 > Add to dictionary: one per line in dictionary.txt next to settings.json. Kept by
/// the editor, not by Windows or Office, so they stay when the spelling provider changes (and on other systems).
/// A word added in lower case also accepts its capitalised forms.
/// </summary>
sealed class UserDictionary
{
    readonly string _path;
    readonly HashSet<string> _words = new(StringComparer.Ordinal);

    public UserDictionary(string path)
    {
        _path = path;
        try
        {
            if (File.Exists(path))
                foreach (var l in File.ReadAllLines(path)) if (l.Trim() is { Length: > 0 } w && !w.StartsWith('#')) _words.Add(w);
        }
        catch (IOException) { }
    }

    public static string DefaultPath => Path.Combine(Settings.Directory, "dictionary.txt");

    public bool Contains(string word) => _words.Contains(word) || _words.Contains(word.ToLowerInvariant());

    public IReadOnlyCollection<string> Words => _words;

    public void Add(string word)
    {
        if (!_words.Add(word)) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.AppendAllText(_path, word + Environment.NewLine);
        }
        catch (IOException) { }
    }
}
