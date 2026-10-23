using Tui;

// Line numbers: a column at the left of the area, as wide as the last line's number plus a space. The text is drawn,
// wrapped and hit-tested in what is left (TextArea); a wrapped line is numbered on its first row only.
partial class EditorView
{
    bool _lineNumbers;

    /// <summary>Line numbers for every file (the "lineNumbers" setting; Alt+G turns them on or off and saves it).</summary>
    public bool LineNumbersOn
    {
        get => _lineNumbers;
        set { if (value == _lineNumbers) return; _lineNumbers = value; Touch(); }
    }

    // At least this many columns stay for the text: in a narrower area the numbers are not shown.
    const int MinTextWidth = 20;

    int GutterWidth
    {
        get
        {
            if (!LineNumbersOn) return 0;
            int g = Lines.Count.ToString(System.Globalization.CultureInfo.InvariantCulture).Length + 1;
            return Area.W - g >= MinTextWidth ? g : 0;
        }
    }

    /// <summary>The part of <see cref="Area"/> the text is drawn in: right of the line numbers.</summary>
    Rect TextArea { get { int g = GutterWidth; return new(Area.X + g, Area.Y, Area.W - g, Area.H); } }

    // The number of a line on its first visual row, blanks on the rest; the caret's line in the text colour.
    void DrawGutter(Screen s, int y, int li, bool first)
    {
        int g = GutterWidth;
        if (g == 0) return;
        var style = li == Row ? _pal[(int)Tok.Normal] : _pal[(int)Tok.Comment];
        var label = first && li >= 0 ? (li + 1).ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(g - 1) + " " : new string(' ', g);
        for (int x = 0; x < g; x++) s.Set(Area.X + x, Area.Y + y, label[x], style);
    }
}
