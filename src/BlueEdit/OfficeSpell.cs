using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;

/// <summary>
/// Spelling through Microsoft Word (Office's own dictionaries), for those who prefer them to Windows'.
/// Word is slow to ask (about 0.1 s a word), so it is asked in the background: unknown words are queued, checked in
/// batches of a few hundred in a hidden document on a thread of its own, and the answers are remembered. Until a
/// word's answer arrives it counts as correct; then <see cref="Version"/> goes up and the screen shows the result.
/// Word starts on the first question (1-2 s). If Word was already running and the editor got that instance, the
/// editor only closes its own hidden document when done, never Word itself.
/// </summary>
sealed class OfficeSpell : ISpellProvider
{
    public string Name => "Office";

    readonly UserDictionary? _user;
    readonly string[] _tags;
    readonly int[] _lcids;
    readonly ConcurrentDictionary<string, bool> _known = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, byte> _queued = new(StringComparer.Ordinal);
    readonly ConcurrentQueue<string> _pending = new();
    readonly ConcurrentDictionary<string, byte> _ignored = new(StringComparer.Ordinal);
    readonly BlockingCollection<Action> _work = new();
    readonly Thread? _thread;
    int _batchScheduled, _version;
    volatile string? _error;
    dynamic? _app, _doc;
    bool _ours;

    const int BatchSize = 300;

    /// <summary>Microsoft Word is registered for automation.</summary>
    public static bool Installed => Type.GetTypeFromProgID("Word.Application") != null;

    public OfficeSpell(UserDictionary? user, params string[] tags)
    {
        _user = user;
        _tags = tags;
        _lcids = tags.Select(Lcid).ToArray();
        if (!Installed) { _error = "Microsoft Word not found"; return; }
        _thread = new Thread(Loop) { IsBackground = true, Name = "Office spelling" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    static int Lcid(string tag)
    {
        try { return CultureInfo.CreateSpecificCulture(tag).LCID; }
        catch (CultureNotFoundException) { return 0; }
    }

    public bool Available => _thread != null && _error == null;
    public string? Error => _error;
    public IReadOnlyList<string> Languages => _tags;
    public int Version => Volatile.Read(ref _version);

    public bool? IsCorrect(string word)
    {
        if (!Available || word.Length < 2) return true;
        if (_ignored.ContainsKey(word) || _user?.Contains(word) == true) return true;
        if (_known.TryGetValue(word, out var ok)) return ok;
        if (!Scripts.Readable(_tags, word)) return true;
        if (_queued.TryAdd(word, 0))
        {
            _pending.Enqueue(word);
            if (Interlocked.Exchange(ref _batchScheduled, 1) == 0) Post(CheckPending);
        }
        return null;
    }

    public List<string> Suggest(string word)
    {
        if (!Available) return new();
        var done = new TaskCompletionSource<List<string>>();
        Post(() =>
        {
            var res = new List<string>();
            try
            {
                foreach (int lcid in _lcids)
                {
                    var r = Doc().Content;
                    r.Text = word;
                    r.LanguageID = lcid;
                    if ((int)r.SpellingErrors.Count == 0) continue;           // this language knows it
                    var s = r.GetSpellingSuggestions();
                    for (int i = 1; i <= (int)s.Count && res.Count < 12; i++) res.Add((string)s.Item(i).Name);
                    if (res.Count > 0) break;
                }
            }
            catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
            done.TrySetResult(res);
        });
        return done.Task.Wait(TimeSpan.FromSeconds(15)) ? done.Task.Result : new();
    }

    public void AddToDictionary(string word)
    {
        _user?.Add(word);
        if (_user == null) _ignored[word] = 0;
        _known.TryRemove(word, out _);
        Interlocked.Increment(ref _version);
    }

    public void Ignore(string word)
    {
        _ignored[word] = 0;
        Interlocked.Increment(ref _version);
    }

    // ---------- the Word thread ----------
    void Post(Action a) { if (!_work.IsAddingCompleted) _work.Add(a); }

    void Loop()
    {
        foreach (var a in _work.GetConsumingEnumerable())
        {
            try { a(); }
            catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidOperationException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
            {
                _error = "Word: " + ex.Message;
                Interlocked.Increment(ref _version);
            }
        }
    }

    dynamic Doc()
    {
        if (_doc != null) return _doc;
        var type = Type.GetTypeFromProgID("Word.Application") ?? throw new InvalidOperationException("Microsoft Word not found");
        _app = Activator.CreateInstance(type)!;
        _ours = !(bool)_app.Visible;                     // a fresh automation instance starts hidden; the user's does not
        // Word gives suggestions only for a document marked visible ("no document is open" otherwise). In our own
        // hidden Word nothing shows anyway; in the user's open Word the document must stay invisible (checking works,
        // suggestions then do not).
        _doc = _ours ? _app.Documents.Add() : _app.Documents.Add(Type.Missing, Type.Missing, Type.Missing, false);
        return _doc;
    }

    void CheckPending()
    {
        Volatile.Write(ref _batchScheduled, 0);
        var batch = new List<string>();
        while (batch.Count < BatchSize && _pending.TryDequeue(out var w)) batch.Add(w);
        if (batch.Count == 0) return;
        var left = batch;
        foreach (int lcid in _lcids)                     // a word is right if any language accepts it
        {
            if (left.Count == 0) break;
            var wrong = WrongIn(left, lcid);
            foreach (var w in left) if (!wrong.Contains(w)) _known[w] = true;
            left = left.Where(wrong.Contains).ToList();
        }
        foreach (var w in left) _known[w] = false;
        foreach (var w in batch) _queued.TryRemove(w, out _);
        Interlocked.Increment(ref _version);
        if (!_pending.IsEmpty && Interlocked.Exchange(ref _batchScheduled, 1) == 0) Post(CheckPending);
    }

    // The words one language rejects: all of them in the hidden document, one per paragraph, one call to Word.
    HashSet<string> WrongIn(List<string> words, int lcid)
    {
        var content = Doc().Content;
        content.Text = string.Join("\r", words);
        content.LanguageID = lcid;
        var wrong = new HashSet<string>(StringComparer.Ordinal);
        var errors = Doc().SpellingErrors;
        int n = errors.Count;
        for (int i = 1; i <= n; i++) wrong.Add(((string)errors.Item(i).Text).Trim());
        return wrong;
    }

    public void Dispose()
    {
        if (_thread == null || _work.IsAddingCompleted) return;
        Post(() =>
        {
            try
            {
                _doc?.Close(0);                          // 0 = don't save
                if (_ours) _app?.Quit(0);
            }
            catch (COMException) { }
            if (_app != null) Marshal.FinalReleaseComObject(_app);
            _doc = null; _app = null;
        });
        _work.CompleteAdding();
        _thread.Join(TimeSpan.FromSeconds(5));
    }
}
