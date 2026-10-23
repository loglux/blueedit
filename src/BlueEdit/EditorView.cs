using System.Runtime.InteropServices;
using System.Text;
using Tui;

/// <summary>The text area: owns the open document's state and draws it into a rectangle of the screen.</summary>
sealed partial class EditorView
{
    public List<string> Lines { get; private set; } = new() { "" };
    public string? Path { get; set; }
    public bool Modified { get; private set; }
    public bool ColumnMode { get; private set; }
    public int Row, Col;
    int _top, _left;
    (int Row, int Col)? _anchor;
    string[] _clip = Array.Empty<string>();
    bool _clipColumn;
    public event Action? CursorMoved;
    public string LastKey { get; private set; } = "";
    /// <summary>Application-level shortcuts (F2, Ctrl+Q ...). They are checked before the editor's own keys.</summary>
    public List<(KeyInput Key, Action Run)> Shortcuts { get; } = new();

    /// <summary>Where the text area sits on the screen; the host sets it before drawing and handling input.</summary>
    public Rect Area { get; private set; } = new(0, 0, 80, 24);

    public void SetArea(Rect area)
    {
        if (area == Area) return;
        Area = area;
        EnsureVisible();
    }

    Lang? _lang;
    int[]? _states;                       // block-comment state at the start of each line, null = needs recompute
    public string LangName => _lang?.Name ?? "Plain";

    // resetWrap: true for freshly loaded/created content, false when only the name changed or a document is re-activated.
    void SetLang(bool resetWrap)
    {
        // by the name; a script without an extension by its #! line
        _lang = Languages.ForPath(Path) ?? (System.IO.Path.HasExtension(Path) ? null : Languages.ForShebang(Lines[0]));
        _states = null; _rowOf = null; _liveRow = -1; _badCache.Clear();
        if (resetWrap)
        {
            bool prose = _lang == null || _lang.Name is "Markdown" or "LaTeX";
            WrapOn = prose ? WrapProse : WrapCode;
            View = _lang?.Name == "Markdown" ? DefaultMdView : MdView.Source;
            _editView = DefaultMdView == MdView.Reading ? MdView.Live : DefaultMdView;
        }
    }

    // ---------- options (from the settings file) ----------
    Style[] _pal = Themes.Get("dos-blue").Palette();                 // token colours in Tok order
    Style _selStyle = Themes.Get("dos-blue").SelectionStyle;
    Style _badStyle = Themes.Get("dos-blue").MisspelledStyle;
    Style _grammarStyle = Themes.Get("dos-blue").GrammarStyle;

    public void ApplyTheme(EditorTheme t)
    {
        _pal = t.Palette(); _selStyle = t.SelectionStyle; _badStyle = t.MisspelledStyle; _grammarStyle = t.GrammarStyle; _errorStyle = t.ErrorStyle; _warningStyle = t.WarningStyle;
    }

    /// <summary>Spaces inserted by the Tab key.</summary>
    public int TabSize { get; set; } = 4;
    /// <summary>Enter keeps the indentation of the current line.</summary>
    public bool AutoIndent { get; set; } = true;

    // Enter after a line that opens a block indents one step more, as code editors do: Python's and YAML's ":", the
    // brackets of the brace languages (the user, 11 October 2026: after "def f():" the caret went to the line start).
    // closerOwnLine: the caret was between the opener and its closer ("{|}"), which then gets a line of its own.
    (bool Inner, bool CloserOwnLine) BlockOpens(string before, string after)
    {
        var t = before.TrimEnd(' ');
        if (t.Length == 0 || _lang == null) return (false, false);
        char last = t[^1];
        if (_lang.Name is "Python" or "YAML") return (last == ':', false);
        if (_lang.Name is not ("C#" or "C/C++" or "Java" or "JavaScript" or "Go" or "Rust" or "PowerShell" or "JSON" or "Shell"))
            return (false, false);
        if (last is not ('{' or '[' or '(')) return (false, false);
        char close = last switch { '{' => '}', '[' => ']', _ => ')' };
        var a = after.TrimStart(' ');
        return (true, a.Length > 0 && a[0] == close);
    }
    /// <summary>Soft wrap for newly opened prose (Markdown, LaTeX, text) and code files.</summary>
    public bool WrapProse { get; set; } = true;
    public bool WrapCode { get; set; }
    /// <summary>The view newly opened Markdown files start in.</summary>
    public MdView DefaultMdView { get; set; } = MdView.Live;

    // ---------- file ----------
    // A file is saved back the way it was read: same encoding (see FileEncodings) and same line endings.
    Encoding _enc = new UTF8Encoding(false);
    string _eol = Environment.NewLine;
    public Encoding Encoding => _enc;
    public string EncodingName => FileEncodings.ShortName(_enc);
    public bool CrLf => _eol == "\r\n";
    public string FileFormat => $"{EncodingName} {(CrLf ? "CRLF" : "LF")}";

    void SetText(string text, Encoding enc)
    {
        _enc = enc;
        _eol = text.Contains("\r\n") ? "\r\n" : "\n";
        Lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        EditVersion++;
    }

    public void Load(string path)
    {
        if (File.Exists(path))
        {
            var (text, enc) = FileEncodings.Detect(File.ReadAllBytes(path));
            SetText(text, enc);
        }
        else { Lines = new() { "" }; _enc = new UTF8Encoding(false); _eol = Environment.NewLine; }
        Path = path; Row = Col = _top = _left = 0; Modified = false; _anchor = null;
        _disk = _seen = Stamp(path);
        SetLang(true); ClearHistory(); Touch();
    }

    /// <summary>Opens <paramref name="path"/> (null: a new document) with text recovered after the editor ended
    /// unsaved: the document counts as modified, and the file on disk as it is now as the one it was opened from.</summary>
    public void Recover(string? path, string text, Encoding enc, bool crLf)
    {
        Lines = text.Split('\n').ToList(); EditVersion++;
        _enc = enc; _eol = crLf ? "\r\n" : "\n";
        Path = path; Row = Col = _top = _left = 0; _anchor = null;
        _disk = _seen = path == null ? null : Stamp(path);
        SetLang(true); ClearHistory(); Touch();
        Modified = true; CursorMoved?.Invoke();
    }

    /// <summary>The text as one string with "\n" line ends: what a recovery copy keeps.</summary>
    public static string TextOf(List<string> lines) => string.Join('\n', lines);

    public void Save()
    {
        if (Path == null) return;
        var bytes = _enc.GetPreamble().Concat(_enc.GetBytes(string.Join(_eol, Lines))).ToArray();
        SafeFile.Write(Path, bytes);
        _disk = _seen = Stamp(Path);
        Modified = false; CursorMoved?.Invoke();
    }

    // ---------- changes made by other programs ----------
    // The file's time and size as of the last load or save; null when it does not exist on disk.
    (DateTime Time, long Size)? _disk, _seen;

    static (DateTime, long)? Stamp(string path)
    {
        var fi = new FileInfo(path);
        return fi.Exists ? (fi.LastWriteTimeUtc, fi.Length) : null;
    }

    public enum DiskChange { None, Changed, Deleted }

    /// <summary>
    /// Has another program changed or deleted the file since it was loaded or saved? A change is reported only once
    /// it has looked the same on two calls in a row, so a file in the middle of being written (or replaced by
    /// delete + rename) is not read half-done. Call it every half second or so.
    /// </summary>
    public DiskChange CheckDisk()
    {
        if (Path == null) return DiskChange.None;
        var now = Stamp(Path);
        if (now == _disk) { _seen = now; return DiskChange.None; }
        if (now != _seen) { _seen = now; return DiskChange.None; }
        return now == null ? DiskChange.Deleted : DiskChange.Changed;
    }

    /// <summary>Keep the text as it is and stop reporting the current state of the file on disk.
    /// The buffer then counts as unsaved: it no longer matches the file.</summary>
    public void AcceptDisk()
    {
        if (Path == null) return;
        _disk = _seen = Stamp(Path);
        Modified = true; CursorMoved?.Invoke();
    }

    /// <summary>The file on disk is not what was last loaded or saved (checked right now, no settling).</summary>
    public bool DiskDiffers => Path != null && Stamp(Path) is { } now && now != _disk;

    /// <summary>Takes the file as another program left it. One undo step brings back the text from before.</summary>
    public void ReloadFromDisk()
    {
        if (Path == null || !File.Exists(Path)) return;
        var (text, enc) = FileEncodings.Detect(File.ReadAllBytes(Path));
        _disk = _seen = Stamp(Path);
        var newLines = text.Replace("\r\n", "\n").Split('\n').ToList();
        if (newLines.SequenceEqual(Lines)) { _enc = enc; Modified = false; CursorMoved?.Invoke(); return; }
        _lastKind = null; Snapshot("reload"); _lastKind = null;
        SetText(text, enc);
        _anchor = null;
        Row = Math.Min(Row, Lines.Count - 1);
        Touch(true);
        Modified = false; CursorMoved?.Invoke();
    }

    public void NewFile()
    {
        Lines = new() { "" }; Path = null; Row = Col = _top = _left = 0; Modified = false; _anchor = null;
        _enc = new UTF8Encoding(false); _eol = Environment.NewLine; _disk = _seen = null;
        SetLang(true); ClearHistory(); Touch();
    }

    /// <summary>Saves under a new name; if the file cannot be written, the document keeps its old name.</summary>
    public void SaveAs(string path)
    {
        var old = Path;
        Path = path; SetLang(false);
        try { Save(); }
        catch { Path = old; SetLang(false); throw; }
    }

    /// <summary>Reads the file again from disk as <paramref name="enc"/> (the bytes were misread). Unsaved edits and
    /// undo history are dropped; the cursor stays where it was. Returns how many characters did not decode (shown as �).</summary>
    public int Reload(Encoding enc)
    {
        if (Path == null || !File.Exists(Path)) return 0;
        var (text, used) = FileEncodings.Decode(File.ReadAllBytes(Path), enc);
        SetText(text, used);
        _disk = _seen = Stamp(Path);
        Row = Math.Min(Row, Lines.Count - 1); Col = Math.Min(Col, Lines[Row].Length);
        _anchor = null; Modified = false;
        ClearHistory(); Touch(); CursorMoved?.Invoke();
        return text.Count(c => c == '�');
    }

    /// <summary>The next save writes the text in <paramref name="enc"/>. The text itself does not change.</summary>
    public void ConvertTo(Encoding enc)
    {
        if (FileEncodings.ShortName(enc) == EncodingName) return;
        _enc = enc; Modified = true; Touch(); CursorMoved?.Invoke();
    }

    /// <summary>Characters the encoding cannot store, as (line, column), at most <paramref name="max"/>.</summary>
    public List<(int Row, int Col)> Unencodable(Encoding enc, int max = 1000) => FileEncodings.Unencodable(Lines, enc, max);

    /// <summary>The next save uses Windows (CRLF) or Unix (LF) line endings.</summary>
    public void SetLineEndings(bool crlf)
    {
        var eol = crlf ? "\r\n" : "\n";
        if (eol == _eol) return;
        _eol = eol; Modified = true; Touch(); CursorMoved?.Invoke();
    }

    /// <summary>1-based line and column, clamped to the text.</summary>
    public void GoToLine(int line, int col = 1)
    {
        Row = Math.Clamp(line - 1, 0, Lines.Count - 1); Col = Math.Clamp(col - 1, 0, Lines[Row].Length); _anchor = null; Touch();
    }

    // ---------- selection ----------
    public bool HasSelection => _anchor is { } a && (a.Row != Row || a.Col != Col);

    public void ToggleColumnMode()
    {
        ColumnMode = !ColumnMode;
        Touch();
    }

    public void SelectAll()
    {
        _anchor = (0, 0); Row = Lines.Count - 1; Col = Lines[Row].Length; Touch();
    }

    // visual column of a char index doesn't matter here: tabs are inserted as spaces
    bool IsSelected(int row, int col)
    {
        if (!HasSelection) return false;
        var a = _anchor!.Value;
        if (ColumnMode)
        {
            int r1 = Math.Min(a.Row, Row), r2 = Math.Max(a.Row, Row);
            int c1 = Math.Min(a.Col, Col), c2 = Math.Max(a.Col, Col);
            return row >= r1 && row <= r2 && col >= c1 && col < c2;
        }
        var (s, e) = Order((a.Row, a.Col), (Row, Col));
        if (row < s.Item1 || row > e.Item1) return false;
        if (row == s.Item1 && col < s.Item2) return false;
        if (row == e.Item1 && col >= e.Item2) return false;
        return true;
    }

    static ((int, int), (int, int)) Order((int, int) p, (int, int) q) =>
        p.Item1 < q.Item1 || (p.Item1 == q.Item1 && p.Item2 <= q.Item2) ? (p, q) : (q, p);

    string[] SelectedText()
    {
        var a = _anchor!.Value;
        if (ColumnMode)
        {
            int r1 = Math.Min(a.Row, Row), r2 = Math.Max(a.Row, Row);
            int c1 = Math.Min(a.Col, Col), c2 = Math.Max(a.Col, Col);
            var res = new List<string>();
            for (int r = r1; r <= r2; r++)
            {
                var l = Lines[r];
                res.Add(c1 >= l.Length ? "" : l.Substring(c1, Math.Min(c2, l.Length) - c1));
            }
            return res.ToArray();
        }
        var (s, e) = Order((a.Row, a.Col), (Row, Col));
        if (s.Item1 == e.Item1) return new[] { Lines[s.Item1][s.Item2..e.Item2] };
        var list = new List<string> { Lines[s.Item1][s.Item2..] };
        for (int r = s.Item1 + 1; r < e.Item1; r++) list.Add(Lines[r]);
        list.Add(Lines[e.Item1][..e.Item2]);
        return list.ToArray();
    }

    void DeleteSelection()
    {
        if (!HasSelection) return;
        var a = _anchor!.Value;
        if (ColumnMode)
        {
            int r1 = Math.Min(a.Row, Row), r2 = Math.Max(a.Row, Row);
            int c1 = Math.Min(a.Col, Col), c2 = Math.Max(a.Col, Col);
            for (int r = r1; r <= r2; r++)
            {
                var l = Lines[r];
                if (c1 < l.Length) Lines[r] = l.Remove(c1, Math.Min(c2, l.Length) - c1);
            }
            Row = r1; Col = c1;
        }
        else
        {
            var (s, e) = Order((a.Row, a.Col), (Row, Col));
            Lines[s.Item1] = Lines[s.Item1][..s.Item2] + Lines[e.Item1][e.Item2..];
            Lines.RemoveRange(s.Item1 + 1, e.Item1 - s.Item1);
            Row = s.Item1; Col = s.Item2;
        }
        _anchor = null;
    }

    /// <summary>System clipboard (set by the host). The internal copy stays the source of truth for column blocks.</summary>
    public IClipboardService? Clipboard { get; set; }
    string _clipText = "";

    public void Copy()
    {
        if (!HasSelection) return;
        _clip = SelectedText(); _clipColumn = ColumnMode;
        _clipText = string.Join("\n", _clip);
        try { Clipboard?.SetText(string.Join("\r\n", _clip)); } catch (ExternalException) { }
    }

    // Prefer text copied elsewhere; keep the internal block (and its column flag) if the system clipboard still holds our copy.
    void SyncFromSystemClipboard()
    {
        string? sys = null;
        try { sys = Clipboard?.GetText(); } catch (ExternalException) { }
        if (string.IsNullOrEmpty(sys)) return;
        sys = sys.Replace("\r\n", "\n");
        if (sys == _clipText) return;
        _clip = sys.Split('\n'); _clipColumn = false; _clipText = sys;
    }

    public void Cut()
    {
        if (!HasSelection || ReadOnlyView) return;
        Snapshot("cut");
        Copy(); DeleteSelection(); Touch(true);
    }

    // ---------- undo / redo ----------
    internal sealed record State(List<string> Lines, int Row, int Col);
    Stack<State> _undo = new(), _redo = new();
    string? _lastKind;
    const int MaxUndo = 200;

    // Call before every edit. Consecutive edits of the same kind (typing, backspace) share one undo step.
    void Snapshot(string kind)
    {
        if (kind == _lastKind && kind is "type" or "back" or "del") return;
        _lastKind = kind;
        _undo.Push(new State(new List<string>(Lines), Row, Col));
        if (_undo.Count > MaxUndo)
        {
            var keep = _undo.Reverse().Skip(_undo.Count - MaxUndo).ToArray();
            _undo.Clear();
            foreach (var s in keep) _undo.Push(s);
        }
        _redo.Clear();
    }

    void Restore(State s)
    {
        Lines = new List<string>(s.Lines); Row = s.Row; Col = s.Col; _anchor = null; _lastKind = null;
        Touch(true);
    }

    public void Undo()
    {
        if (ReadOnlyView) return;
        if (_undo.Count == 0) return;
        _redo.Push(new State(new List<string>(Lines), Row, Col));
        Restore(_undo.Pop());
    }

    public void Redo()
    {
        if (ReadOnlyView) return;
        if (_redo.Count == 0) return;
        _undo.Push(new State(new List<string>(Lines), Row, Col));
        Restore(_redo.Pop());
    }

    // New stacks instead of Clear(): a captured document may still hold the old ones.
    void ClearHistory() { _undo = new(); _redo = new(); _lastKind = null; }

    public void Paste()
    {
        if (ReadOnlyView) return;
        SyncFromSystemClipboard();
        if (_clip.Length == 0) return;
        Snapshot("paste");
        DeleteSelection();
        if (_clipColumn)
        {
            for (int i = 0; i < _clip.Length; i++)
            {
                int r = Row + i;
                if (r >= Lines.Count) Lines.Add("");
                var l = Lines[r].PadRight(Col);
                Lines[r] = l.Insert(Col, _clip[i]);
            }
        }
        else if (_clip.Length == 1)
        {
            Lines[Row] = Lines[Row].Insert(Col, _clip[0]); Col += _clip[0].Length;
        }
        else
        {
            var tail = Lines[Row][Col..];
            Lines[Row] = Lines[Row][..Col] + _clip[0];
            for (int i = 1; i < _clip.Length; i++) Lines.Insert(Row + i, _clip[i]);
            Row += _clip.Length - 1;
            Col = _clip[^1].Length;
            Lines[Row] += tail;
        }
        Touch(true);
    }

    // ---------- spell checking ----------
    public ISpellProvider? Spell { get; set; }
    public bool SpellOn { get; set; }

    /// <summary>What Esc does once there is nothing to cancel (close the document, as Far's editor does), or null:
    /// then Esc only cancels.</summary>
    public Action? EscapeAction { get; set; }

    /// <summary>Whether Esc closes now (checked when it is pressed); null: always, while <see cref="EscapeAction"/> is set.</summary>
    public Func<bool>? EscapeCloses { get; set; }

    internal static readonly System.Text.RegularExpressions.Regex WordRx =
        new(@"(?<![\p{L}\p{N}_])\p{L}[\p{L}\p{M}'’]*(?:-\p{L}[\p{L}\p{M}'’]*)*(?![\p{N}_])", System.Text.RegularExpressions.RegexOptions.Compiled);

    // Hyphenated words ("по-русски", "well-known") are judged whole; if the dictionary lacks the compound, its parts may vouch for it.
    // A word whose answer is still on its way (a background provider) counts as correct for now.
    internal static bool IsCorrectWord(ISpellProvider sp, string w) =>
        sp.IsCorrect(w) is not false || (w.Contains('-', StringComparison.Ordinal) && w.Split('-').All(p => p.Length < 3 || sp.IsCorrect(p) is not false));

    // Which token kinds hold prose worth checking. Code identifiers must never be flagged.
    // `lang` is the line's own language: inside a Markdown code block it is the block's language.
    static bool Spellable(Tok t, Lang? lang) => lang?.Name switch
    {
        null => true,
        "Markdown" => t is Tok.Normal or Tok.Heading or Tok.Emphasis or Tok.Comment,
        "LaTeX" => t is Tok.Normal or Tok.Comment,
        _ => t is Tok.Comment or Tok.String,
    };

    static bool LooksLikeIdentifier(string w)
    {
        bool allUpper = true, mixed = false;
        for (int i = 0; i < w.Length; i++)
        {
            if (char.IsLower(w[i])) allUpper = false;
            if (i > 0 && char.IsUpper(w[i]) && char.IsLower(w[i - 1])) mixed = true;
        }
        return allUpper || mixed;      // HTML, NASA, camelCase, BlueEdit
    }

    // Misspelling masks per (line, state), valid until the user dictionary changes.
    readonly Dictionary<(string, int, Lang?), (int Ver, bool[]? Mask)> _badCache = new();

    bool[]? BadFor(int li, string line, int state, Tok[] toks)
    {
        if (!SpellOn || Spell is not { Available: true } sp) return null;
        var lang = LineLanguage(li);
        if (_badCache.TryGetValue((line, state, lang), out var e) && e.Ver == sp.Version) return e.Mask;
        var mask = MisspelledMask(line, toks, lang);
        if (_badCache.Count > 20000) _badCache.Clear();
        _badCache[(line, state, lang)] = (sp.Version, mask);
        return mask;
    }

    // Addresses, e-mail and paths are not words: https://example.com/docs, name@mail.ru, C:\Users\..., docs/notes.md
    internal static readonly System.Text.RegularExpressions.Regex NotProseRx = new(
        @"\b(?:[a-z][a-z0-9+.-]*://|www\.)\S+|[\w.+-]+@[\w-]+(?:\.[\w-]+)+|\b[A-Za-z]:\\\S*|(?<![\w/])\.{0,2}/?(?:[\w.-]+/)+[\w.-]*",
        System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    bool[]? MisspelledMask(string line, Tok[] toks, Lang? lang)
    {
        if (!SpellOn || Spell is not { Available: true } sp || line.Length == 0) return null;
        bool[]? mask = null;
        var skip = NotProseRx.Matches(line);
        bool Skipped(int at)
        {
            foreach (System.Text.RegularExpressions.Match s in skip) if (at >= s.Index && at < s.Index + s.Length) return true;
            return false;
        }
        foreach (System.Text.RegularExpressions.Match m in WordRx.Matches(line))
        {
            if (!Spellable(toks[m.Index], lang) || m.Length < 3 || (skip.Count > 0 && Skipped(m.Index))) continue;
            if (lang?.Name == "LaTeX" && ((m.Index > 0 && line[m.Index - 1] is '{' or '\\') ||
                                           (m.Index + m.Length < line.Length && line[m.Index + m.Length] == '}'))) continue;
            var w = m.Value.TrimEnd('\'', '’');
            // a word typed partly in the other layout ("рrивет") is wrong whatever a dictionary says
            if (!Lookalike.IsMixed(w) && (LooksLikeIdentifier(w) || IsCorrectWord(sp, w))) continue;
            mask ??= new bool[line.Length];
            Array.Fill(mask, true, m.Index, w.Length);
        }
        return mask;
    }

    /// <summary>The word under (or just before) the cursor, if any.</summary>
    public (int Start, int Length, string Word)? WordAtCursor()
    {
        var l = Lines[Row];
        int a = Math.Min(Col, l.Length), b = a;
        while (a > 0 && (char.IsLetter(l[a - 1]) || l[a - 1] is '\'' or '’')) a--;
        while (b < l.Length && (char.IsLetter(l[b]) || l[b] is '\'' or '’')) b++;
        if (b == a) return null;
        return (a, b - a, l.Substring(a, b - a));
    }

    public void ReplaceWord(int start, int length, string with)
    {
        if (ReadOnlyView) return;
        Snapshot("spell");
        Lines[Row] = Lines[Row].Remove(start, length).Insert(start, with);
        Col = start + with.Length; _anchor = null;
        Touch(true);
    }

    // ---------- documents (several buffers share this one view) ----------
    /// <summary>Everything that belongs to one open file. Path/Modified are only current for documents that are not active.</summary>
    public sealed class Doc
    {
        public string? Path { get; internal set; }
        public bool Modified { get; internal set; }
        internal List<string> Lines = new() { "" };
        internal int Row, Col, Top, Left;
        internal (int Row, int Col)? Anchor;
        internal bool ColumnMode, Wrap;
        internal MdView View, EditView;
        internal Encoding Enc = new UTF8Encoding(false);
        internal string Eol = "\n";
        internal Stack<State> Undo = new(), Redo = new();
        internal (DateTime, long)? Disk, Seen;
    }

    public Doc CaptureDoc() => new()
    {
        Path = Path, Modified = Modified, Lines = Lines, Row = Row, Col = Col, Top = _top, Left = _left,
        Anchor = _anchor, ColumnMode = ColumnMode, Wrap = WrapOn, View = View, EditView = _editView, Enc = _enc, Eol = _eol, Undo = _undo, Redo = _redo,
        Disk = _disk, Seen = _seen,
    };

    public void ApplyDoc(Doc d)
    {
        Path = d.Path; Modified = d.Modified; Lines = d.Lines; Row = d.Row; Col = d.Col; _top = d.Top; _left = d.Left;
        _anchor = d.Anchor; ColumnMode = d.ColumnMode; _enc = d.Enc; _eol = d.Eol; _undo = d.Undo; _redo = d.Redo;
        _disk = d.Disk; _seen = d.Seen;
        _lastKind = null; WrapOn = d.Wrap; SetLang(false); View = d.View; _editView = d.EditView; Touch();
    }

    // ---------- word / line operations ----------
    static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_' || Tui.CharWidth.IsMark(c);     // "é" as e + accent

    // The caret moves and Delete/Backspace remove whole characters as a reader sees them (Unicode grapheme clusters):
    // a letter with its combining marks, a surrogate pair, an emoji with its modifiers or joined parts, a flag.
    internal static int NextStop(string l, int col) =>
        col >= l.Length ? l.Length : col + Math.Max(1, System.Globalization.StringInfo.GetNextTextElementLength(l, col));

    internal static int PrevStop(string l, int col)
    {
        if (col <= 0) return 0;
        // a cluster never starts inside an ASCII run: start from the last ASCII character some way back
        int p = Math.Max(0, col - 64);
        while (p > 0 && l[p] >= '\u0080') p--;
        while (true)
        {
            int n = NextStop(l, p);
            if (n >= col) return p;
            p = n;
        }
    }

    void WordRight()
    {
        var l = Lines[Row];
        if (Col >= l.Length) { if (Row < Lines.Count - 1) { Row++; Col = 0; } return; }
        int i = Col;
        if (IsWordChar(l[i])) while (i < l.Length && IsWordChar(l[i])) i++;
        else while (i < l.Length && !IsWordChar(l[i]) && !char.IsWhiteSpace(l[i])) i++;
        while (i < l.Length && char.IsWhiteSpace(l[i])) i++;
        Col = i;
    }

    void WordLeft()
    {
        if (Col == 0) { if (Row > 0) { Row--; Col = Lines[Row].Length; } return; }
        var l = Lines[Row];
        int i = Col;
        while (i > 0 && char.IsWhiteSpace(l[i - 1])) i--;
        if (i > 0)
        {
            if (IsWordChar(l[i - 1])) while (i > 0 && IsWordChar(l[i - 1])) i--;
            else while (i > 0 && !IsWordChar(l[i - 1]) && !char.IsWhiteSpace(l[i - 1])) i--;
        }
        Col = i;
    }

    public void DeleteLinePublic() => DeleteLine();
    public void DuplicateLinePublic() => DuplicateLine();

    void DeleteLine()
    {
        if (ReadOnlyView) return;
        Snapshot("delline");
        _anchor = null;
        if (Lines.Count == 1) Lines[0] = "";
        else { Lines.RemoveAt(Row); if (Row >= Lines.Count) Row = Lines.Count - 1; }
        Col = Math.Min(Col, Lines[Row].Length);
        Touch(true);
    }

    void DuplicateLine()
    {
        if (ReadOnlyView) return;
        Snapshot("dupline");
        _anchor = null;
        Lines.Insert(Row + 1, Lines[Row]); Row++;
        Touch(true);
    }

    // ---------- mouse ----------
    (int Row, int Col) HitTest(int x, int y)
    {
        if (WrapOn)
        {
            var seg = SegmentAt(Math.Clamp(_top + y, 0, TotalRows - 1));
            int dcol = Math.Min(seg.Start + Math.Max(0, x - HangOf(seg)), MaxCol(seg));
            return (seg.Line, DispFor(seg.Line)?.ToSource(dcol) ?? dcol);
        }
        int r = Math.Clamp(_top + y, 0, Lines.Count - 1);
        int dc = Math.Clamp(_left + x, 0, TextForLayout(r).Length);
        return (r, DispFor(r)?.ToSource(dc) ?? dc);
    }

    bool _dragging;
    long _lastClickTicks;
    (int X, int Y) _lastClickAt = (-1, -1);

    /// <summary>Mouse in screen coordinates. Returns true when the editor used it.</summary>
    public bool HandleMouse(MouseInput m)
    {
        int x = m.X - TextArea.X, y = m.Y - Area.Y;     // a click on a line number: the start of the line
        bool inside = Area.Contains(m.X, m.Y);
        switch (m.Kind)
        {
            case MouseKind.WheelUp or MouseKind.WheelDown when inside && ReadingView:
                ScrollReading(_top + (m.Kind == MouseKind.WheelUp ? -3 : 3));
                return true;
            case MouseKind.WheelUp or MouseKind.WheelDown when inside:
                _top = Math.Clamp(_top + (m.Kind == MouseKind.WheelUp ? -3 : 3), 0, Math.Max(0, TotalRows - 1));
                return true;
            case MouseKind.Down when m.Button == 0 && inside:
                {
                    long now = Environment.TickCount64;
                    bool doubleClick = (x, y) == _lastClickAt && now - _lastClickTicks < 400;
                    _lastClickTicks = now; _lastClickAt = doubleClick ? (-1, -1) : (x, y);
                    var (r, c) = HitTest(x, y);
                    if (doubleClick)
                    {
                        var l = Lines[r]; int a = c, b = c;
                        while (a > 0 && IsWordChar(l[a - 1])) a--;
                        while (b < l.Length && IsWordChar(l[b])) b++;
                        Row = r; _anchor = (r, a); Col = b; ColumnMode = false; _dragging = false; Touch(); return true;
                    }
                    (Row, Col) = (r, c); _anchor = null; _dragging = true; _lastKind = null; _goalX = -1; Touch(); return true;
                }
            case MouseKind.Drag when _dragging:
                _anchor ??= (Row, Col);
                (Row, Col) = HitTest(Math.Clamp(x, 0, TextArea.W - 1), Math.Clamp(y, 0, Area.H - 1)); Touch(); return true;
            case MouseKind.Up when _dragging:
                _dragging = false; return true;
        }
        return false;
    }

    // ---------- drawing ----------
    // ---------- timing (View > Show timing) ----------
    public bool ShowTiming { get; set; }
    public double LastDrawMs { get; private set; }
    public double LastKeyToDrawMs { get; private set; }
    long _keyTicks;

    // BLUEEDIT_TIMELOG=<file>: wall-clock stamps of "key arrived" (K) and "frame drawn" (D), for measuring the channel around the editor.
    static readonly string? TimeLog = Environment.GetEnvironmentVariable("BLUEEDIT_TIMELOG");
    static void Stamp(char tag) { if (TimeLog != null) File.AppendAllText(TimeLog, $"{tag} {DateTime.UtcNow.Ticks}\n"); }

    /// <summary>Draws the visible part of the document into <see cref="Area"/>.</summary>
    public void Draw(Screen s)
    {
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        DrawLines(s);
        long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
        LastDrawMs = (t1 - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        Stamp('D');
        if (_keyTicks != 0) { LastKeyToDrawMs = (t1 - _keyTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency; _keyTicks = 0; }
    }

    void DrawLines(Screen s)
    {
        var ta = TextArea;
        int w = ta.W, h = ta.H, ox = ta.X, oy = ta.Y;
        int total = TotalRows;
        int cachedLine = -1; Tok[] toks = Array.Empty<Tok>(); bool[]? bad = null, gram = null; byte[]? diag = null; Disp? disp = null; string text = "";
        _grammarFrame.Clear();
        var diagnostics = CurrentDiagnostics();
        var brackets = MatchingBrackets();
        for (int y = 0; y < h; y++)
        {
            int vr = _top + y;
            if (vr >= total) { DrawGutter(s, y, -1, false); s.Fill(ox, oy + y, w, 1, ' ', _pal[0]); continue; }

            int li, startCol, endCol, hang = 0;
            bool firstRow = true;
            if (WrapOn) { var sg = SegmentAt(vr); li = sg.Line; startCol = sg.Start; endCol = sg.Start + sg.Len; hang = HangOf(sg); firstRow = sg.Index == 0; }
            else { li = vr; startCol = _left; endCol = int.MaxValue; }
            DrawGutter(s, y, li, firstRow);
            if (hang > 0) s.Fill(ox, oy + y, hang, 1, ' ', _pal[0]);       // hanging indent of a continuation row

            var line = Lines[li];
            if (li != cachedLine)
            {
                toks = TokensFor(li);
                bad = BadFor(li, line, _states![li], toks);             // TokensFor has built _states
                gram = GrammarMaskFor(li);
                diag = diagnostics.Count > 0 ? DiagnosticMaskFor(li, diagnostics) : null;
                disp = DispFor(li); text = disp?.Text ?? line;
                cachedLine = li;
            }
            int cells = Math.Clamp(Math.Min(endCol, text.Length) - startCol, 0, w - hang);
            for (int x = 0; x < cells; x++)
            {
                int ci = startCol + x;
                int src = disp != null ? disp.Map[ci] : ci;
                var style = _pal[(int)(disp?.Over[ci] ?? toks[src])];
                if (gram != null && src < gram.Length && gram[src]) style = _grammarStyle;
                if (diag != null && src < diag.Length && diag[src] != 0) style = diag[src] == 1 ? _errorStyle : _warningStyle;
                if (bad != null && src < bad.Length && bad[src]) style = _badStyle;      // a typo wins over grammar
                if (brackets is { } bp && (bp.A == (li, src) || bp.B == (li, src))) style = style with { Attr = style.Attr | Attr.Bold | Attr.Underline };
                if (IsSelected(li, src)) style = _selStyle;
                s.Set(ox + hang + x, oy + y, text[ci], disp?.Marks?[ci], style);   // tabs and control characters become blanks
            }
            if (hang + cells < w) s.Fill(ox + hang + cells, oy + y, w - hang - cells, 1, ' ', _pal[0]);
            if (disp?.Picture is { } pic && disp.RowWidth > 0)          // a typeset formula over the blanks of its row
                s.Picture(ox + Disp.PictureIndent, oy + y, pic, disp.PictureRow + startCol / disp.RowWidth, _pal[(int)Tok.Math]);
        }
    }

    /// <summary>Screen position of the caret, or null when it is hidden (reading mode) or scrolled out of the area.</summary>
    public (int X, int Y)? CursorPos
    {
        get
        {
            if (ReadingView) return null;
            var (vr, x) = CursorVisual();
            var ta = TextArea;
            int sx = ta.X + x, sy = ta.Y + vr - _top;
            return ta.Contains(sx, sy) ? (sx, sy) : null;
        }
    }

    // Reading mode: the page starts at row `top` (the last page full), the hidden caret on its first row, so the
    // status line, a search and leaving reading mode go on from what is shown
    void ScrollReading(int top)
    {
        _top = Math.Clamp(top, 0, Math.Max(0, TotalRows - Math.Max(1, Area.H)));
        if (WrapOn)
        {
            var seg = SegmentAt(_top);
            Row = seg.Line;
            Col = DispFor(seg.Line)?.ToSource(seg.Start) ?? seg.Start;
        }
        else { Row = Math.Min(_top, Lines.Count - 1); Col = 0; }
        _anchor = null;
        CursorMoved?.Invoke();
    }

    void EnsureVisible()
    {
        int h = Math.Max(1, Area.H), w = Math.Max(1, TextArea.W);
        var (vr, _) = CursorVisual();
        if (!WrapOn) vr = Row;
        if (vr < _top) _top = vr;
        if (vr >= _top + h) _top = vr - h + 1;
        _top = Math.Clamp(_top, 0, Math.Max(0, TotalRows - 1));
        if (WrapOn) _left = 0;
        else
        {
            int dc = DisplayCol(Row, Col);
            if (dc < _left) _left = dc;
            if (dc >= _left + w) _left = dc - w + 1;
        }
    }

    // Caret movement: a Markdown line switching between raw and concealed changes the wrap layout.
    void TouchMove()
    {
        Col = Math.Min(Col, Lines[Row].Length);
        LiveRowChanged();
        EnsureVisible();
        CursorMoved?.Invoke();
    }

    // Only the line the caret left (markup hidden again) and the one it entered (markup shown) change their layout, and
    // the lines of a display formula it left or entered (picture ↔ source).
    void LiveRowChanged()
    {
        if (Row == _liveRow) return;
        if (Concealing && View == MdView.Live)
        {
            RelayoutLines(_liveRow, Row);
            var left = FormulaLines(_liveRow);
            var entered = FormulaLines(Row);
            if (left != entered)
                foreach (var (first, last) in new[] { left, entered }.OfType<(int, int)>())
                    for (int li = first; li <= last; li++) if (li != Row && li != _liveRow) RelayoutLines(li, li);
        }
        _liveRow = Row;
    }

    void Touch(bool edit = false)
    {
        if (edit) { EditVersion++; Modified = true; _states = null; _rowOf = null; _goalX = -1; _lastEditTicks = Environment.TickCount64; }
        LiveRowChanged();
        Col = Math.Min(Col, Lines[Row].Length);
        EnsureVisible(); CursorMoved?.Invoke();
    }

    // ---------- keys ----------
    public double LastHandlerMs { get; private set; }

    public bool HandleKey(KeyInput key)
    {
        long h0 = System.Diagnostics.Stopwatch.GetTimestamp();
        bool r = HandleKeyCore(key);
        LastHandlerMs = (System.Diagnostics.Stopwatch.GetTimestamp() - h0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        if (r && key.Key == Key.Character && (key.Mods & (Mods.Ctrl | Mods.Alt)) == 0 && (IsIdentChar(key.Character) || key.Character is '.' or '(' or ',' or ')')) WordTyped?.Invoke(key.Character);
        return r;
    }

    /// <summary>Short readable name of a key, e.g. "Ctrl+Shift+Right" or "Alt+h".</summary>
    public static string Describe(KeyInput k)
    {
        var sb = new StringBuilder();
        if (k.Ctrl) sb.Append("Ctrl+");
        if (k.Alt) sb.Append("Alt+");
        if (k.Shift) sb.Append("Shift+");
        sb.Append(k.Key == Key.Character ? k.Character.ToString() : k.Key.ToString());
        return sb.ToString();
    }

    /// <summary>A command's failure, for a bug report: error.log in BlueEdit's data folder (%LOCALAPPDATA%\BlueEdit,
    /// ~/.local/share/BlueEdit), never next to the program, where it may not write (Program Files, /usr/bin).</summary>
    static void LogError(Exception ex)
    {
        var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), "BlueEdit");
        try { Directory.CreateDirectory(dir); File.AppendAllText(System.IO.Path.Combine(dir, "error.log"), $"{DateTime.Now:s} {ex}\n\n"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    bool HandleKeyCore(KeyInput key)
    {
        Stamp('K');
        _keyTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        LastKey = Describe(key);
        CursorMoved?.Invoke();
        foreach (var (k, run) in Shortcuts)
            if (key == k)
            {
                try { run(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
                {
                    LastKey = "ERROR " + ex.GetType().Name + ": " + ex.Message;
                    LogError(ex);
                    CursorMoved?.Invoke();
                }
                return true;
            }
        int page = Math.Max(1, Area.H - 1);
        var kk = key.Key;
        bool noCtrlAlt = (key.Mods & (Mods.Ctrl | Mods.Alt)) == 0;
        if (kk is not (Key.Up or Key.Down or Key.PageUp or Key.PageDown)) _goalX = -1;

        // reading mode: the keys scroll the page, as in a viewer. The caret is not shown there, so moving it scrolled
        // nothing until it reached the edge, and the first arrow after the mouse wheel jumped back to it (the user,
        // 10 October 2026)
        if (ReadingView && !key.Alt && !key.Shift || ReadingView && key.Is(' ', Mods.Shift))
        {
            int? to = kk switch
            {
                Key.Up => _top - 1,
                Key.Down => _top + 1,
                Key.PageUp => _top - page,
                Key.PageDown => _top + page,
                Key.Home => 0,
                Key.End => int.MaxValue,
                Key.Character when key.Character == ' ' => key.Shift ? _top - page : _top + page,
                _ => null,
            };
            if (to is { } t) { ScrollReading(t); return true; }
        }

        // movement keys, with Shift extending the selection
        bool isMove = kk is Key.Up or Key.Down or Key.Left or Key.Right or Key.Home or Key.End or Key.PageUp or Key.PageDown;
        if (isMove && !key.Alt)
        {
            _lastKind = null;
            if (key.Shift) _anchor ??= (Row, Col); else _anchor = null;
            switch (kk)
            {
                case Key.Up: MoveVertical(-1); break;
                case Key.Down: MoveVertical(1); break;
                case Key.Left:
                    if (key.Ctrl) WordLeft();
                    else if (Col > 0) Col = PrevStop(Lines[Row], Col); else if (Row > 0) { Row--; Col = Lines[Row].Length; }
                    break;
                case Key.Right:
                    if (key.Ctrl) WordRight();
                    else if (Col < Lines[Row].Length) Col = NextStop(Lines[Row], Col); else if (Row < Lines.Count - 1) { Row++; Col = 0; }
                    break;
                case Key.Home: if (key.Ctrl) { Row = 0; Col = 0; } else LineHome(); break;
                case Key.End:
                    if (key.Ctrl) { Row = Lines.Count - 1; Col = Lines[Row].Length; } else LineEnd();
                    break;
                case Key.PageUp: MoveVertical(-page); break;
                case Key.PageDown: MoveVertical(page); break;
            }
            TouchMove(); return true;
        }

        // the classic clipboard keys of DOS, Far and Windows: Ctrl+Insert copies (in reading mode too), Shift+Insert
        // pastes, Shift+Delete cuts a selection (without one it deletes a character, as Delete)
        if (kk == Key.Insert && key.Mods == Mods.Ctrl) { Copy(); return true; }
        if (!ReadOnlyView && kk == Key.Insert && key.Mods == Mods.Shift) { Paste(); return true; }
        if (!ReadOnlyView && kk == Key.Delete && key.Mods == Mods.Shift && HasSelection) { Cut(); return true; }

        // Esc with "escapeQuits": cancel what there is to cancel first (selection, reading view), then close.
        if (kk == Key.Escape && key.Mods == Mods.None && EscapeAction != null && (EscapeCloses?.Invoke() ?? true))
        {
            if (HasSelection) { _anchor = null; Touch(); return true; }
            if (ReadingView && !ReadOnlyFile) { ToggleReading(); return true; }
            EscapeAction();
            return true;
        }

        if (ReadOnlyView)
        {
            if (key.Is('c', Mods.Ctrl)) Copy();
            else if (key.Is('a', Mods.Ctrl)) SelectAll();
            return true;                          // reading mode: no editing
        }

        if (key.Mods == Mods.Alt && kk is Key.Up or Key.Down) { MoveLines(kk == Key.Up ? -1 : 1); return true; }

        if (key.Ctrl && !key.Alt && kk is Key.Backspace or Key.Delete)
        {
            Snapshot("delword");
            if (HasSelection) DeleteSelection();
            else
            {
                int r0 = Row, c0 = Col;
                if (kk == Key.Backspace) WordLeft(); else WordRight();
                _anchor = (r0, c0); DeleteSelection();   // delete the span between old and new cursor
            }
            Touch(true); return true;
        }

        if (kk == Key.Character && key.Mods == Mods.Ctrl)
        {
            switch (key.Character)
            {
                case 'l': DeleteLine(); return true;
                case '/': case '_': ToggleComment(); return true;      // terminals send Ctrl+/ as Ctrl+_ (0x1F)
                case 'd': DuplicateLine(); return true;
                case 'c': Copy(); return true;
                case 'x': Cut(); return true;
                case 'v': Paste(); return true;
                case 'a': SelectAll(); return true;
                case 'z': Undo(); return true;
                case 'y': Redo(); return true;
            }
        }

        if (noCtrlAlt)
        {
            switch (kk)
            {
                case Key.Enter:
                    {
                        Snapshot("enter");
                        DeleteSelection();
                        var l = Lines[Row]; int indent = AutoIndent ? l.TakeWhile(c => c == ' ').Count() : 0;
                        var (inner, closerOwnLine) = AutoIndent ? BlockOpens(l[..Col], l[Col..]) : (false, false);
                        int newIndent = inner ? indent + TabSize : indent;
                        string rest = closerOwnLine || inner ? l[Col..].TrimStart(' ') : l[Col..];
                        Lines[Row] = inner ? l[..Col].TrimEnd(' ') : l[..Col];
                        if (closerOwnLine)
                        {
                            // between "{" and "}": the caret on a line of its own inside, the closer at the opener's indent
                            Lines.Insert(Row + 1, new string(' ', newIndent));
                            Lines.Insert(Row + 2, new string(' ', indent) + rest);
                        }
                        else Lines.Insert(Row + 1, new string(' ', newIndent) + rest);
                        Row++; Col = newIndent; Touch(true); return true;
                    }
                case Key.Backspace when BackspacePair():
                    return true;
                case Key.Backspace:
                    Snapshot(HasSelection ? "delsel" : "back");
                    if (HasSelection) DeleteSelection();
                    else if (Col > 0) { int from = PrevStop(Lines[Row], Col); Lines[Row] = Lines[Row].Remove(from, Col - from); Col = from; }
                    else if (Row > 0) { Col = Lines[Row - 1].Length; Lines[Row - 1] += Lines[Row]; Lines.RemoveAt(Row); Row--; }
                    Touch(true); return true;
                case Key.Delete:
                    Snapshot(HasSelection ? "delsel" : "del");
                    if (HasSelection) DeleteSelection();
                    else if (Col < Lines[Row].Length) Lines[Row] = Lines[Row].Remove(Col, NextStop(Lines[Row], Col) - Col);
                    else if (Row < Lines.Count - 1) { Lines[Row] += Lines[Row + 1]; Lines.RemoveAt(Row + 1); }
                    Touch(true); return true;
                case Key.Tab when key.Shift:
                    UnindentBlock(); return true;
                case Key.Tab when SelectionSpansLines:
                    IndentBlock(); return true;
                case Key.Tab:
                    Snapshot("tab");
                    DeleteSelection();
                    Lines[Row] = Lines[Row].Insert(Col, new string(' ', TabSize)); Col += TabSize; Touch(true); return true;
                case Key.Escape:
                    _anchor = null; Touch(); return true;
                case Key.Character when key.Character >= ' ' && TypeCloserDedented(key.Character):
                    return true;
                case Key.Character when key.Character >= ' ' && TypePaired(key.Character):
                    return true;
                case Key.Character when key.Character >= ' ':
                    Snapshot(HasSelection ? "typesel" : "type");
                    DeleteSelection();
                    Lines[Row] = Lines[Row].Insert(Col, key.Character.ToString()); Col++;
                    if (key.Character != ' ') WrapWhileTyping();
                    Touch(true); return true;
            }
        }
        return false;
    }

    /// <summary>Text pasted by the terminal (bracketed paste) goes in as one block: no auto-indent, one undo step.</summary>
    public void InsertText(string text)
    {
        if (ReadOnlyView || text.Length == 0) return;
        Snapshot("paste");
        DeleteSelection();
        InsertLines(text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'));
        Touch(true);
    }

    void InsertLines(string[] parts)
    {
        if (parts.Length == 1) { Lines[Row] = Lines[Row].Insert(Col, parts[0]); Col += parts[0].Length; return; }
        var tail = Lines[Row][Col..];
        Lines[Row] = Lines[Row][..Col] + parts[0];
        for (int i = 1; i < parts.Length; i++) Lines.Insert(Row + i, parts[i]);
        Row += parts.Length - 1;
        Col = parts[^1].Length;
        Lines[Row] += tail;
    }
}
