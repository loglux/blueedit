using System.Text.RegularExpressions;

// Markdown code blocks: the lines between ```lang and ``` are highlighted as that language. Worked out once per
// highlighting pass (together with _states) and looked up per line while drawing.
partial class EditorView
{
    static readonly Regex FenceOpen = new(@"^\s*```\s*(.*)$", RegexOptions.Compiled);

    Lang?[] _fenceLang = Array.Empty<Lang?>();     // language of the code block a line is inside, if known
    int[] _fenceState = Array.Empty<int>();         // that language's block state at the line's start
    int[]? _fenceFor;                               // the _states the arrays were built for

    void EnsureFences()
    {
        _states ??= Highlighter.States(Lines, _lang);
        if (ReferenceEquals(_fenceFor, _states)) return;
        _fenceFor = _states;
        int n = Lines.Count;
        _fenceLang = new Lang?[n];
        _fenceState = new int[n];
        if (!IsMarkdown) return;
        int fenceBlock = Array.FindIndex(_lang!.Blocks, b => b.Start == "```") + 1;   // state value inside ``` blocks
        Lang? current = null;
        int inner = 0;
        for (int i = 0; i < n; i++)
        {
            int st = _states[i];
            if (st == fenceBlock)
            {
                if (Lines[i].Contains("```", StringComparison.Ordinal)) { current = null; continue; }   // closing fence
                if (current == null) continue;
                _fenceLang[i] = current;
                _fenceState[i] = inner;
                Highlighter.ClassifyCached(Lines[i], current, inner, out inner);
            }
            else if (st == 0 && _states[i + 1] == fenceBlock && FenceOpen.Match(Lines[i]) is { Success: true } m)
            {
                current = Languages.ForFence(m.Groups[1].Value);
                inner = 0;
            }
        }
    }

    /// <summary>Tokens of a line: in a Markdown code block with a known language, that language's.</summary>
    Tok[] TokensFor(int li)
    {
        EnsureFences();
        if (li < _fenceLang.Length && _fenceLang[li] is { } inner)
            return Highlighter.ClassifyCached(Lines[li], inner, _fenceState[li], out _);
        return Highlighter.ClassifyCached(Lines[li], _lang, _states![li], out _);
    }

    // ---------- tables ----------
    Tables.Table?[] _tableOf = Array.Empty<Tables.Table?>();
    Disp?[] _tableDisp = Array.Empty<Disp?>();
    int[]? _tablesFor;

    /// <summary>The aligned form of a line of a Markdown table, or null when the line is not in one.</summary>
    Disp? TableDisp(int li)
    {
        _states ??= Highlighter.States(Lines, _lang);
        if (!ReferenceEquals(_tablesFor, _states))
        {
            _tablesFor = _states;
            var states = _states;
            _tableOf = Tables.Find(Lines, i => states[i] == 0);
            _tableDisp = new Disp?[Lines.Count];
        }
        if (li >= _tableOf.Length || _tableOf[li] is not { } t) return null;
        return _tableDisp[li] ??= Tables.Render(Lines[li], t, li);
    }

    /// <summary>The language a line is written in: the file's, or inside a Markdown code block that block's.</summary>
    Lang? LineLanguage(int li)
    {
        EnsureFences();
        return li < _fenceLang.Length && _fenceLang[li] is { } inner ? inner : _lang;
    }

    /// <summary>The language a line's code block is written in (Markdown), or null.</summary>
    internal string? FenceLanguageAt(int li) { EnsureFences(); return li < _fenceLang.Length ? _fenceLang[li]?.Name : null; }
}
