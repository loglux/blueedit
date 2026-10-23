using System.Text;

/// <summary>
/// Lays a display formula out over several rows of text for the reading view (#49), as sympy's pretty printer does:
/// a fraction over a ─ bar, a root with a bar over what it covers, limits above and below ∑ and lim, brackets as tall
/// as what they hold (⎛⎜⎝), aligned rows, cases and matrices as a grid. Exponents and indices stay one row: superscript
/// characters when Unicode has them all, otherwise the script written out (Latex.ToUnicode) a row above or below.
/// Every character takes one column. What it cannot read it shows as written, as Latex.ToUnicode does.
/// </summary>
static class LatexLayout
{
    /// <summary>Rows of text of one width; <see cref="Base"/> is the row the formula's baseline runs through.</summary>
    internal sealed class Box
    {
        public readonly string[] Rows;
        public readonly int Base;
        public readonly int Width;

        public Box(string[] rows, int @base)
        {
            Width = rows.Length == 0 ? 0 : rows.Max(r => r.Length);
            Rows = rows.Select(r => r.PadRight(Width)).ToArray();
            Base = @base;
        }

        public bool IsFraction { get; init; }
        public int Height => Rows.Length;
        public int Ascent => Base;                      // rows above the baseline row
        public int Descent => Height - 1 - Base;        // rows below it
        public static Box Text(string s) => new(new[] { s }, 0);
    }

    /// <summary>The formula as rows of text (trailing spaces trimmed), or null when it fits on one row anyway: the
    /// caller then shows the one-line Unicode form.</summary>
    public static string[]? Rows(string tex)
    {
        var box = Lay(tex);
        if (box.Height <= 1) return null;
        return box.Rows.Select(r => r.TrimEnd()).ToArray();
    }

    internal static Box Lay(string tex)
    {
        var p = new Parser(tex);
        var (box, tag) = p.Top();
        return tag == null ? box : HCat(new[] { box, Box.Text("   (" + tag + ")") });
    }

    // ---- composing boxes ----

    // side by side, baselines on one row
    static Box HCat(Box[] parts)
    {
        if (parts.Length == 1) return parts[0];
        if (parts.Length == 0) return Box.Text("");
        int asc = parts.Max(p => p.Ascent), desc = parts.Max(p => p.Descent);
        var rows = new StringBuilder[asc + desc + 1];
        for (int r = 0; r < rows.Length; r++) rows[r] = new StringBuilder();
        foreach (var p in parts)
            for (int r = 0; r < rows.Length; r++)
            {
                int pr = r - (asc - p.Base);
                rows[r].Append(pr >= 0 && pr < p.Height ? p.Rows[pr] : new string(' ', p.Width));
            }
        return new Box(rows.Select(r => r.ToString()).ToArray(), asc);
    }

    // -1 left, 0 centre, 1 right
    static Box Pad(Box b, int width, int align)
    {
        int extra = width - b.Width;
        if (extra <= 0) return b;
        int left = align < 0 ? 0 : align > 0 ? extra : extra / 2;
        return new Box(b.Rows.Select(r => new string(' ', left) + r + new string(' ', extra - left)).ToArray(), b.Base);
    }

    // one above the other; the baseline is that of parts[baseOf]
    static Box Stack(Box[] parts, int align, int baseOf)
    {
        int width = parts.Max(p => p.Width);
        var rows = new List<string>();
        int @base = 0;
        for (int k = 0; k < parts.Length; k++)
        {
            var p = Pad(parts[k], width, align);
            if (k == baseOf) @base = rows.Count + p.Base;
            rows.AddRange(p.Rows);
        }
        return new Box(rows.ToArray(), @base);
    }

    static Box Fraction(Box num, Box den)
    {
        var b = Stack(new[] { num, Box.Text(new string('─', Math.Max(num.Width, den.Width))), den }, 0, 1);
        return new Box(b.Rows, b.Base) { IsFraction = true };
    }

    // a space between a fraction and a factor written right next to it: "─x" and "─(46.7)" ran the bar into the next
    // character (seen on the user's worked integral, 10 October 2026)
    static List<Box> Spaced(List<Box> items)
    {
        var res = new List<Box>(items.Count);
        for (int k = 0; k < items.Count; k++)
        {
            if (k > 0 && (items[k].IsFraction && Ends(items[k - 1]) || items[k - 1].IsFraction && Starts(items[k]))) res.Add(Box.Text(" "));
            res.Add(items[k]);
        }
        return res;

        static bool Starts(Box b) => b.Rows[b.Base].TrimStart() is { Length: > 0 } t && t.Length == b.Rows[b.Base].Length
            && (char.IsLetterOrDigit(t[0]) || t[0] is '(' or '[' or '√' or '⎛' or '⎡' || b.IsFraction);
        static bool Ends(Box b) => b.Rows[b.Base].TrimEnd() is { Length: > 0 } t && t.Length == b.Rows[b.Base].Length
            && (char.IsLetterOrDigit(t[^1]) || t[^1] is ')' or ']' or '-' or '+' or '±' or '∓' || b.IsFraction);   // "-─" read as one bar
    }

    static Box Root(Box radicand, string index)
    {
        string mark = index switch { "" or "2" => "√", "3" => "∛", "4" => "∜", _ => (Compact('^', index) ?? "(" + index + ")") + "√" };
        // one row: no bar on a row of its own, which floated above the sign (the √ glyph does not reach the top of its
        // cell; the user, 10 October 2026), but √3 or √(b² - 4ac), as the one-line form writes it
        if (radicand.Height == 1)
        {
            var t = radicand.Rows[0];
            return Box.Text(mark + (t.Length == 1 || t.All(char.IsDigit) ? t : "(" + t + ")"));
        }
        var bar = Box.Text(" " + new string('─', radicand.Width));
        var side = new string[radicand.Height];
        for (int r = 0; r < side.Length; r++) side[r] = r == side.Length - 1 ? mark : new string(' ', mark.Length - 1) + "│";
        var body = HCat(new[] { new Box(side, radicand.Base), radicand });
        return Stack(new[] { Pad(bar, body.Width, 1), body }, 1, 1);
    }

    // a mark over (or under) what it covers: \vec, \overline, \hat ...
    static Box Accent(Box x, string kind)
    {
        int w = Math.Max(1, x.Width);
        string mark = kind switch
        {
            "vec" or "overrightarrow" => w == 1 ? "→" : new string('─', w - 1) + "→",
            "overleftarrow" => w == 1 ? "←" : "←" + new string('─', w - 1),
            "overline" or "bar" => new string('─', w),
            "hat" or "widehat" => "^",
            "tilde" or "widetilde" => "~",
            "dot" => "˙",
            "ddot" => "¨",
            _ => new string('─', w),
        };
        return kind == "underline" ? Stack(new[] { x, Box.Text(mark) }, 0, 0) : Stack(new[] { Box.Text(mark), x }, 0, 1);
    }

    // ⌠⎮⌡ over ascent + descent + 1 rows, side by side for \iint and \iiint
    static Box TallIntegral(int count, int ascent, int descent)
    {
        var rows = new string[ascent + descent + 1];
        for (int r = 0; r < rows.Length; r++) rows[r] = new string(r == 0 ? '⌠' : r == rows.Length - 1 ? '⌡' : '⎮', count);
        return new Box(rows, ascent);
    }

    // a bracket as tall as what it holds
    static Box Delimiter(string d, int height, int @base)
    {
        if (d.Length == 0) return new Box(Enumerable.Repeat("", Math.Max(1, height)).ToArray(), @base);
        if (height <= 1) return Box.Text(d);
        (char Top, char Mid, char Bottom, char Centre) pieces = d switch
        {
            "(" => ('⎛', '⎜', '⎝', '⎜'),
            ")" => ('⎞', '⎟', '⎠', '⎟'),
            "[" => ('⎡', '⎢', '⎣', '⎢'),
            "]" => ('⎤', '⎥', '⎦', '⎥'),
            "{" => ('⎧', '⎪', '⎩', '⎨'),
            "}" => ('⎫', '⎪', '⎭', '⎬'),
            "⌊" => ('⎢', '⎢', '⎣', '⎢'),
            "⌋" => ('⎥', '⎥', '⎦', '⎥'),
            "⌈" => ('⎡', '⎢', '⎢', '⎢'),
            "⌉" => ('⎤', '⎥', '⎥', '⎥'),
            "‖" => ('‖', '‖', '‖', '‖'),
            "|" => ('│', '│', '│', '│'),
            _ => (' ', ' ', ' ', d[0]),
        };
        var rows = new string[height];
        int centre = @base > 0 && @base < height - 1 ? @base : height / 2;
        for (int r = 0; r < height; r++)
            rows[r] = (r == 0 ? pieces.Top : r == height - 1 ? pieces.Bottom : r == centre ? pieces.Centre : pieces.Mid).ToString();
        if (d is "⟨" or "⟩") for (int r = 0; r < height; r++) rows[r] = r == centre ? d : " ";
        return new Box(rows, @base);
    }

    // superscript or subscript characters for all of it, or null
    static string? Compact(char kind, string text)
    {
        var sb = new StringBuilder();
        foreach (char ch in text)
        {
            if (ch == ' ') continue;
            if (!Latex.TryScript(kind, ch, out var m)) return null;
            sb.Append(m);
        }
        return sb.Length == 0 ? null : sb.ToString();
    }

    // scripts to the right of a box: in script characters on its baseline when it is one row and Unicode has them all,
    // otherwise written out a row above (below) it
    static Box Scripts(Box b, string? sup, string? sub, bool raised = false)
    {
        string? up = sup == null ? null : Latex.ToUnicode(sup, tight: true), down = sub == null ? null : Latex.ToUnicode(sub, tight: true);
        if (b.Height == 1 && !raised)
        {
            string? cu = up == null ? "" : Compact('^', up), cd = down == null ? "" : Compact('_', down);
            if (cu != null && cd != null) return HCat(new[] { b, Box.Text(cd + cu) });
        }
        int lift = up == null ? 0 : Math.Max(1, b.Ascent), drop = down == null ? 0 : Math.Max(1, b.Descent);
        var rows = new string[lift + drop + 1];
        for (int r = 0; r < rows.Length; r++) rows[r] = "";
        if (up != null) rows[0] = up;
        if (down != null) rows[^1] = down;
        return HCat(new[] { b, new Box(rows, lift) });
    }

    // limits above and below (∑, lim)
    static Box Limits(Box op, string? sup, string? sub)
    {
        var parts = new List<Box>();
        if (sup != null) parts.Add(Limit(sup));
        parts.Add(op);
        if (sub != null) parts.Add(Limit(sub));
        return Stack(parts.ToArray(), 0, sup != null ? 1 : 0);
    }

    // one row, or the rows of \substack{i<n \\ j<m}
    static Box Limit(string src) =>
        src.Contains(@"\substack", StringComparison.Ordinal) ? Lay(src) : Box.Text(Latex.ToUnicode(src, tight: true));

    static readonly HashSet<string> LimitOps = new(StringComparer.Ordinal)
    {
        "sum", "prod", "coprod", "bigcup", "bigcap", "bigoplus", "bigotimes", "lim", "liminf", "limsup", "max", "min",
        "sup", "inf", "argmax", "argmin",
    };

    static readonly Dictionary<string, string> Delimiters = new(StringComparer.Ordinal)
    {
        ["{"] = "{", ["}"] = "}", ["lbrace"] = "{", ["rbrace"] = "}", ["langle"] = "⟨", ["rangle"] = "⟩", ["|"] = "‖",
        ["Vert"] = "‖", ["lVert"] = "‖", ["rVert"] = "‖", ["vert"] = "|", ["lvert"] = "|", ["rvert"] = "|", ["mid"] = "|",
        ["lfloor"] = "⌊", ["rfloor"] = "⌋", ["lceil"] = "⌈", ["rceil"] = "⌉", ["lbrack"] = "[", ["rbrack"] = "]",
    };

    // ---- the parser ----

    sealed class Parser
    {
        readonly string s;
        int i;
        string? _tag;
        bool _limitsNext;                     // the command just read takes its scripts above and below (\underbrace)

        public Parser(string text) { s = text; }

        enum Stop { End, Brace, Right, Cell }

        public (Box Box, string? Tag) Top()
        {
            var box = Grid(null, top: true);
            return (box, _tag);
        }

        bool At(string lit) => string.CompareOrdinal(s, i, lit, 0, lit.Length) == 0 &&
            (lit.Length < 2 || !char.IsLetter(lit[^1]) || i + lit.Length >= s.Length || !char.IsLetter(s[i + lit.Length]));

        void SkipSpaces() { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        // a sequence up to the stop; the stop itself is not consumed
        Box Seq(Stop stop)
        {
            var items = new List<Box>();
            var run = new StringBuilder();
            bool lastLimits = false;          // the item before is ∑ or lim: its scripts go above and below
            var integrals = new List<(int At, string Name, string? Sup, string? Sub)>();

            void Flush()
            {
                if (run.Length == 0) return;
                var t = new StringBuilder(run.Length);
                foreach (char c in run.ToString()) if (!(c == ' ' && t.Length > 0 && t[^1] == ' ')) t.Append(c);
                items.Add(Box.Text(t.ToString()));
                run.Clear();
            }

            // what is shown before the next character: the run, or the last row of the item before it
            string Before() => run.ToString().Trim().Length > 0 ? run.ToString() : items.Count > 0 ? items[^1].Rows[items[^1].Base] : "";

            while (i < s.Length)
            {
                char c = s[i];
                if (stop == Stop.Brace && c == '}') break;
                if (stop == Stop.Cell && (c == '&' || At(@"\\") || At(@"\end"))) break;
                if (stop == Stop.Right && At(@"\right")) break;
                if (stop == Stop.End && At(@"\\")) break;
                if (c == '^' || c == '_')
                {
                    string? sup = null, sub = null;
                    while (i < s.Length && (s[i] == '^' || s[i] == '_'))
                    {
                        bool up = s[i] == '^';
                        i++;
                        var arg = ArgSource();
                        if (up) sup = arg; else sub = arg;
                        // the spaces after it stay before an operator ("x^2 + 1"), not before a letter or a command
                        // ("\cos^2 \alpha" is cos²α in LaTeX) or another script
                        int j = i;
                        while (j < s.Length && s[j] == ' ') j++;
                        if (j < s.Length && (s[j] is '^' or '_' or '\\' || char.IsLetterOrDigit(s[j]))) i = j;
                    }
                    Box b;
                    if (run.Length > 0) { Flush(); }
                    b = items.Count > 0 ? items[^1] : Box.Text("");
                    if (items.Count > 0) items.RemoveAt(items.Count - 1);
                    items.Add(lastLimits ? Limits(b, sup, sub) : Scripts(b, sup, sub));
                    lastLimits = false;
                    continue;
                }
                lastLimits = false;
                if (c == '{') { i++; Flush(); items.Add(Seq(Stop.Brace)); if (i < s.Length) i++; continue; }
                if (c == '}') { i++; continue; }                         // unbalanced
                if (c == '~') { run.Append(' '); i++; continue; }
                if (c == '\'') { run.Append('′'); i++; continue; }
                if (char.IsWhiteSpace(c)) { i++; if (i < s.Length && Latex.KeepSpace(Before(), s[i])) run.Append(' '); continue; }
                if (c != '\\') { run.Append(Latex.SpacedChar(c, Before())); i++; continue; }

                // a command
                int at = i;
                var name = Name();
                if (LimitOps.Contains(name))
                {
                    Flush();
                    items.Add(Box.Text(Latex.Plain.TryGetValue(name, out var op) ? op : name));
                    SkipSpaces();
                    if (At(@"\limits")) i += 7;
                    else if (At(@"\nolimits")) { i += 9; continue; }
                    lastLimits = true;
                    continue;
                }
                if (name is "int" or "iint" or "iiint" or "oint")
                {
                    // limits beside it, a row above and below, and a space before the integrand: "∫ₐ₊₁ᵇf(x)" was hard to read
                    Flush();
                    var sign = Box.Text(Latex.Plain[name]);
                    string? sup = null, sub = null;
                    SkipSpaces();
                    if (At(@"\limits") || At(@"\nolimits")) Name();
                    while (i < s.Length && (s[i] == '^' || s[i] == '_'))
                    {
                        bool up = s[i] == '^';
                        i++;
                        if (up) sup = ArgSource(); else sub = ArgSource();
                        SkipSpaces();
                    }
                    items.Add(sup == null && sub == null ? sign : HCat(new[] { Scripts(sign, sup, sub, raised: true), Box.Text(" ") }));
                    integrals.Add((items.Count - 1, name, sup, sub));
                    continue;
                }
                var box = Command(name, at);
                bool limits = _limitsNext;
                _limitsNext = false;
                if (box == null) continue;
                if (limits) { Flush(); items.Add(box); lastLimits = true; continue; }
                if (box.Height == 1 && box.Base == 0) run.Append(box.Rows[0]);
                else { Flush(); items.Add(box); }
            }
            Flush();
            // an integral sign as tall as its integrand (⌠⎮⌡), three rows at least: the integrand runs up to the next relation
            // ("= …") or the next integral, or the end of the group
            for (int k = integrals.Count - 1; k >= 0; k--)
            {
                var (at, name, sup, sub) = integrals[k];
                bool Next(int j) => k + 1 < integrals.Count && j == integrals[k + 1].At;
                int end = at + 1 < items.Count && !Next(at + 1) ? at + 2 : at + 1;     // " x dx = 1" is still its integrand
                while (end < items.Count && !Next(end) && !Relation(items[end])) end++;
                if (end == at + 1 || name == "oint") continue;
                var integrand = items.Skip(at + 1).Take(end - at - 1).ToList();
                // three rows at least, as sympy draws it: a one-row ∫ looked small next to its integrand
                int asc = Math.Max(1, integrand.Max(p => p.Ascent)), desc = Math.Max(1, integrand.Max(p => p.Descent));
                var sign = TallIntegral(name.Length - 2, asc, desc);
                items[at] = sup == null && sub == null ? HCat(new[] { sign, Box.Text(" ") }) : HCat(new[] { Scripts(sign, sup, sub, raised: true), Box.Text(" ") });
            }
            return HCat(Spaced(items).ToArray());
        }

        // a one-row piece with a relation in it ("dx = "): where an integrand ends, as far as its height goes
        static bool Relation(Box b) => b.Height == 1 && b.Rows[0].AsSpan().IndexOfAny(Relations) >= 0;

        static readonly System.Buffers.SearchValues<char> Relations = System.Buffers.SearchValues.Create("=<>≤≥≈≠≡→∼");

        // the name after a backslash: letters, or one other character
        string Name()
        {
            i++;
            if (i >= s.Length) return "";
            if (!char.IsLetter(s[i])) return s[i++].ToString();
            int st = i;
            while (i < s.Length && char.IsLetter(s[i])) i++;
            return s[st..i];
        }

        // the source of one argument: {group}, a command, or a character
        string ArgSource()
        {
            SkipSpaces();
            if (i >= s.Length) return "";
            if (s[i] == '{')
            {
                int depth = 0, st = i + 1;
                for (; i < s.Length; i++)
                {
                    if (s[i] == '\\') { i++; continue; }
                    if (s[i] == '{') depth++;
                    else if (s[i] == '}' && --depth == 0) { i++; return s[st..(i - 1)]; }
                }
                return s[st..];
            }
            if (s[i] == '\\') { int st = i; Name(); return s[st..i]; }
            return s[i++].ToString();
        }

        Box Arg()
        {
            SkipSpaces();
            if (i < s.Length && s[i] == '{') { i++; var b = Seq(Stop.Brace); if (i < s.Length) i++; return b; }
            return new Parser(ArgSource()).Seq(Stop.End);
        }

        string Delim()
        {
            SkipSpaces();
            if (i >= s.Length) return "";
            if (s[i] == '\\') { var n = Name(); return Delimiters.TryGetValue(n, out var d) ? d : n; }
            char c = s[i++];
            return c == '.' ? "" : c.ToString();
        }

        Box? Command(string name, int at)
        {
            switch (name)
            {
                case "frac": case "dfrac": case "tfrac": case "cfrac":
                    { var n = Arg(); var d = Arg(); return Fraction(n, d); }
                case "binom": case "dbinom": case "tbinom":
                    {
                        var n = Arg(); var k = Arg();
                        var inner = Stack(new[] { n, k }, 0, 0);
                        return HCat(new[] { Delimiter("(", inner.Height, inner.Base), inner, Delimiter(")", inner.Height, inner.Base) });
                    }
                case "sqrt":
                    {
                        SkipSpaces();
                        string index = "";
                        if (i < s.Length && s[i] == '[') { int e = s.IndexOf(']', i); if (e > 0) { index = s[(i + 1)..e]; i = e + 1; } }
                        return Root(Arg(), index);
                    }
                case "left":
                    {
                        var open = Delim();
                        var inner = Seq(Stop.Right);
                        string close = "";
                        if (At(@"\right")) { i += 6; close = Delim(); }
                        return HCat(new[] { Delimiter(open, inner.Height, inner.Base), inner, Delimiter(close, inner.Height, inner.Base) });
                    }
                case "right": Delim(); return null;                       // without its \left
                case "middle": { var d = Delim(); return Box.Text(d); }
                case "begin": return Environment();
                case "end": ArgSource(); return null;
                case "text": case "textrm": case "textit": case "textbf": case "mbox": case "textnormal":
                    return Box.Text(ArgSource());
                case "mathrm": case "mathbf": case "mathit": case "mathsf": case "mathtt": case "mathcal": case "mathfrak":
                case "mathscr": case "operatorname": case "boldsymbol": case "bm":
                    return Arg();
                case "mathbb": return Box.Text(string.Concat(ArgSource().Select(ch => Latex.Blackboard.TryGetValue(ch, out var b) ? b : ch)));
                case "vec": case "overrightarrow": case "overleftarrow": case "overline": case "bar": case "hat": case "widehat":
                case "tilde": case "widetilde": case "dot": case "ddot": case "underline":
                    return Accent(Arg(), name);
                case "overset": case "stackrel": { var over = Arg(); var x = Arg(); return Stack(new[] { over, x }, 0, 1); }
                case "underset": { var under = Arg(); var x = Arg(); return Stack(new[] { x, under }, 0, 0); }
                case "underbrace": case "overbrace":
                    {
                        var x = Arg();
                        int w = Math.Max(3, x.Width);
                        string brace = name == "underbrace" ? "╰" + new string('─', (w - 3) / 2) + "┬" + new string('─', w - 3 - (w - 3) / 2) + "╯"
                                                            : "╭" + new string('─', (w - 3) / 2) + "┴" + new string('─', w - 3 - (w - 3) / 2) + "╮";
                        _limitsNext = true;                               // its label (_{...} or ^{...}) goes under or over
                        return name == "underbrace" ? Stack(new[] { x, Box.Text(brace) }, 0, 0) : Stack(new[] { Box.Text(brace), x }, 0, 1);
                    }
                case "boxed":
                    {
                        var x = Arg();
                        var rows = new List<string> { "┌" + new string('─', x.Width + 2) + "┐" };
                        rows.AddRange(x.Rows.Select(r => "│ " + r + " │"));
                        rows.Add("└" + new string('─', x.Width + 2) + "┘");
                        return new Box(rows.ToArray(), x.Base + 1);
                    }
                case "xrightarrow": case "xleftarrow":
                    {
                        var label = Arg();
                        int w = label.Width + 2;
                        var arrow = name == "xrightarrow" ? new string('─', w - 1) + "→" : "←" + new string('─', w - 1);
                        return Stack(new[] { label, Box.Text(arrow) }, 0, 1);
                    }
                case "substack": return new Parser(ArgSource()).Grid(null, top: true);
                case "tag":
                    if (i < s.Length && s[i] == '*') i++;
                    _tag = Latex.ToUnicode(ArgSource());
                    return null;
                case "label": ArgSource(); return null;
                case "nonumber": case "notag": case "displaystyle": case "textstyle": case "limits": case "nolimits":
                case "big": case "Big": case "bigg": case "Bigg": case "bigl": case "bigr": case "Bigl": case "Bigr":
                case "biggl": case "biggr": case "Biggl": case "Biggr": case "!":
                    return null;
                case ",": case ";": case ":": case " ": case "enspace": case "thinspace": return Box.Text(" ");
                case "quad": return Box.Text("  ");
                case "qquad": return Box.Text("    ");
                case "\\": return Box.Text(" ");                                   // a row break where none can be
                case "hline": return null;
            }
            // symbols and everything else: as the one-line form has them (unknown commands as written)
            if (Latex.Spaced.TryGetValue(name, out var rel)) return Box.Text(" " + Latex.Symbol(rel) + " ");
            if (Latex.Plain.TryGetValue(name, out var sym)) return Box.Text(sym);
            if (i < s.Length && s[i] == '{') ArgSource();
            return Box.Text(Latex.ToUnicode(s[at..i]));
        }

        Box Environment()
        {
            var name = ArgSource();
            string spec = name == "array" ? ArgSource() : "";
            var grid = Grid(name, top: false, spec);
            return name switch
            {
                "cases" or "dcases" => HCat(new[] { Delimiter("{", grid.Height, grid.Base), Box.Text(" "), grid }),
                "rcases" => HCat(new[] { grid, Box.Text(" "), Delimiter("}", grid.Height, grid.Base) }),
                "pmatrix" => Around("(", grid, ")"),
                "bmatrix" => Around("[", grid, "]"),
                "Bmatrix" => Around("{", grid, "}"),
                "vmatrix" => Around("|", grid, "|"),
                "Vmatrix" => Around("‖", grid, "‖"),
                _ => grid,
            };

            Box Around(string open, Box g, string close) =>
                HCat(new[] { Delimiter(open, g.Height, g.Base), g, Delimiter(close, g.Height, g.Base) });
        }

        // rows split by \\, cells by &, up to \end{...} (or the end of the formula at the top)
        Box Grid(string? env, bool top, string spec = "")
        {
            var rows = new List<List<Box>>();
            var cur = new List<Box>();
            var ruled = new HashSet<int>();                // rows with a \hline above them
            while (true)
            {
                SkipSpaces();
                while (At(@"\hline")) { i += 6; SkipSpaces(); if (cur.Count == 0) ruled.Add(rows.Count); }
                cur.Add(Seq(top ? Stop.End : Stop.Cell));
                if (i >= s.Length) break;
                if (s[i] == '&') { i++; continue; }
                if (At(@"\\"))
                {
                    i += 2;
                    SkipSpaces();
                    if (i < s.Length && s[i] == '[') { int e = s.IndexOf(']', i); if (e > 0) i = e + 1; }   // \\[2pt]
                    rows.Add(cur); cur = new List<Box>();
                    continue;
                }
                if (At(@"\end")) { i += 4; ArgSource(); break; }
                if (top && i < s.Length) { i++; continue; }                         // a stray character: go on
            }
            rows.Add(cur);
            while (rows.Count > 1 && rows[^1].All(b => b.Width == 0 || b.Rows.All(r => r.Trim().Length == 0))) rows.RemoveAt(rows.Count - 1);
            if (rows.Count == 1 && rows[0].Count == 1) return Trim(rows[0][0]);

            int cols = rows.Max(r => r.Count);
            var widths = new int[cols];
            foreach (var r in rows)
                for (int k = 0; k < r.Count; k++) r[k] = Trim(r[k]);
            foreach (var r in rows)
                for (int k = 0; k < r.Count; k++) widths[k] = Math.Max(widths[k], r[k].Width);
            var built = new List<Box>();
            foreach (var r in rows)
            {
                var parts = new List<Box>();
                for (int k = 0; k < cols; k++)
                {
                    int gap = Gap(env, k);
                    if (gap > 0) parts.Add(Box.Text(new string(' ', gap)));
                    parts.Add(Pad(k < r.Count ? r[k] : Box.Text(""), widths[k], Align(env, spec, k)));
                }
                built.Add(HCat(parts.ToArray()));
            }
            int width = built.Max(b => b.Width);
            var all = new List<Box>();
            for (int r = 0; r <= built.Count; r++)
            {
                if (ruled.Contains(r)) all.Add(Box.Text(new string('─', width)));
                if (r < built.Count) all.Add(built[r]);
            }
            var stacked = Stack(all.ToArray(), -1, 0);
            return new Box(stacked.Rows, all.Count == 1 ? built[0].Base : (stacked.Height - 1) / 2);
        }

        // spaces before column k: aligned pairs (right & left) close, the pairs apart
        static int Gap(string? env, int k) => k == 0 ? 0 : IsAligned(env) ? (k % 2 == 1 ? 1 : 3) : env is "cases" or "dcases" or "rcases" ? 3 : 2;

        // an array's columns as its spec says ({l|cr}); aligned pairs right then left; cases left; the rest centred
        static int Align(string? env, string spec, int k)
        {
            if (IsAligned(env)) return k % 2 == 0 ? 1 : -1;
            if (env is "cases" or "dcases" or "rcases") return -1;
            var letters = spec.Where(c => c is 'l' or 'c' or 'r').ToArray();
            return k < letters.Length ? letters[k] switch { 'l' => -1, 'r' => 1, _ => 0 } : 0;
        }

        static bool IsAligned(string? env) => env is "aligned" or "align" or "align*" or "alignat" or "alignat*" or "split"
            or "eqnarray" or "eqnarray*" or "flalign" or "flalign*";

        // a cell or the whole formula without the spaces around it
        static Box Trim(Box b)
        {
            if (b.Width == 0) return b;
            int left = b.Rows.Min(r => r.Length - r.TrimStart().Length);
            int right = b.Rows.Min(r => r.Length - r.TrimEnd().Length);
            if (left + right >= b.Width) return new Box(b.Rows.Select(_ => "").ToArray(), b.Base);
            return new Box(b.Rows.Select(r => r.Substring(left, b.Width - left - right)).ToArray(), b.Base);
        }
    }
}
