using System.Text.RegularExpressions;

/// <summary>
/// Errors that keep a formula from being typeset, found without a model (#192): a brace, \left, \begin without its
/// other half, a command without its arguments, a double superscript, an &amp; outside an alignment. Each is an error
/// MathJax (Obsidian's renderer) and LaTeX stop on, so none is a guess; the messages follow MathJax's. Commands are not
/// checked against a list: a document may define its own. <see cref="Style"/>: the rules of writing, as warnings.
/// </summary>
static class LatexCheck
{
    /// <summary>One finding: where in the formula's text, how long, and what is wrong; a rule of writing (<see cref="Style"/>)
    /// also has its name and the text to put in its place.</summary>
    internal readonly record struct Problem(int Offset, int Length, string Message, string Rule = "", string? Fix = null);

    // commands that take this many arguments (an optional [..] of \sqrt is skipped)
    static readonly Dictionary<string, int> Arguments = new(StringComparer.Ordinal)
    {
        ["frac"] = 2, ["dfrac"] = 2, ["tfrac"] = 2, ["cfrac"] = 2, ["binom"] = 2, ["dbinom"] = 2, ["tbinom"] = 2,
        ["sqrt"] = 1, ["overline"] = 1, ["underline"] = 1, ["hat"] = 1, ["widehat"] = 1, ["bar"] = 1, ["vec"] = 1,
        ["tilde"] = 1, ["widetilde"] = 1, ["dot"] = 1, ["ddot"] = 1, ["overrightarrow"] = 1, ["overleftarrow"] = 1,
        ["overbrace"] = 1, ["underbrace"] = 1, ["mathbf"] = 1, ["mathrm"] = 1, ["mathit"] = 1, ["mathbb"] = 1,
        ["mathcal"] = 1, ["mathfrak"] = 1, ["mathsf"] = 1, ["mathtt"] = 1, ["boldsymbol"] = 1, ["operatorname"] = 1,
    };

    // commands whose argument is text, not maths: its inside is not checked
    static readonly HashSet<string> TextArgument = new(StringComparer.Ordinal)
        { "text", "textrm", "textbf", "textit", "textsf", "texttt", "mbox", "hbox", "operatorname", "label", "tag" };

    // what may follow \left, \right, \middle as a delimiter command
    static readonly HashSet<string> Delimiters = new(StringComparer.Ordinal)
    {
        "{", "}", "|", "langle", "rangle", "lvert", "rvert", "lVert", "rVert", "vert", "Vert", "lfloor", "rfloor",
        "lceil", "rceil", "uparrow", "downarrow", "updownarrow", "Uparrow", "Downarrow", "Updownarrow", "backslash",
        "lbrace", "rbrace", "lbrack", "rbrack", "ulcorner", "urcorner", "llcorner", "lrcorner", "lgroup", "rgroup",
        "lmoustache", "rmoustache",
    };

    sealed class Group(char kind, int at, int length, string name = "")
    {
        public readonly char Kind = kind;            // '{', 'l' (\left), 'e' (\begin)
        public readonly int At = at, Length = length;
        public readonly string Name = name;
        public bool ScriptArgument;                  // a {…} that is the argument of ^ or _
        public bool Sup, Sub;                        // the current base has a superscript, a subscript
        public readonly Stack<(string Command, int At, int Length, int Left)> Wanted = new();
    }

    /// <summary>The errors of one formula; <paramref name="math"/> is its text with or without its delimiters ($, $$,
    /// \( \), \[ \]).</summary>
    internal static List<Problem> Check(string math)
    {
        var problems = new List<Problem>();
        var (start, end) = Inside(math);
        var stack = new Stack<Group>();
        stack.Push(new Group('$', start, 0));

        // a token that is a base or an argument: an argument is taken off what is wanted, a base starts afresh
        void Atom(bool group = false)
        {
            var g = stack.Peek();
            if (g.Wanted.TryPop(out var w))
            {
                if (w.Left > 1) g.Wanted.Push(w with { Left = w.Left - 1 });
                return;
            }
            if (!group) g.Sup = g.Sub = false;
        }

        // a closer, a script or the end where an argument was still wanted
        void Missing(Group g)
        {
            while (g.Wanted.TryPop(out var w))
                problems.Add(w.Command is "^" or "_"
                    ? new Problem(w.At, w.Length, Ui.T("Missing superscript or subscript argument"))
                    : new Problem(w.At, w.Length, Ui.T("Missing argument for \\{0}", w.Command)));
        }

        void Unclosed(Group g)
        {
            Missing(g);
            problems.Add(g.Kind switch
            {
                '{' => new Problem(g.At, 1, Ui.T("Missing close brace")),
                'l' => new Problem(g.At, g.Length, Ui.T("Missing \\right")),
                _ => new Problem(g.At, g.Length, Ui.T("Missing \\end{{{0}}}", g.Name)),
            });
        }

        int i = start;
        while (i < end)
        {
            char c = math[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '{')
            {
                bool argument = stack.Peek().Wanted.Count > 0;
                Atom(group: true);
                var g = new Group('{', i, 1) { ScriptArgument = argument };
                stack.Push(g);
                i++;
                continue;
            }
            if (c == '}')
            {
                if (!stack.Any(g => g.Kind == '{')) { problems.Add(new Problem(i, 1, Ui.T("Extra close brace or missing open brace"))); i++; continue; }
                while (stack.Peek().Kind != '{') Unclosed(stack.Pop());
                var closed = stack.Pop();
                Missing(closed);
                if (!closed.ScriptArgument) { var p = stack.Peek(); if (p.Wanted.Count == 0) p.Sup = p.Sub = false; }
                i++;
                continue;
            }
            if (c is '^' or '_')
            {
                var g = stack.Peek();
                if (g.Wanted.Count > 0) { Missing(g); }
                if (c == '^' && g.Sup) problems.Add(new Problem(i, 1, Ui.T("Double exponent: use braces to clarify")));
                if (c == '_' && g.Sub) problems.Add(new Problem(i, 1, Ui.T("Double subscripts: use braces to clarify")));
                if (c == '^') g.Sup = true; else g.Sub = true;
                g.Wanted.Push((c.ToString(), i, 1, 1));
                i++;
                continue;
            }
            if (c == '&')
            {
                if (!stack.Any(g => g.Kind == 'e')) problems.Add(new Problem(i, 1, Ui.T("Misplaced &")));
                Missing(stack.Peek());
                stack.Peek().Sup = stack.Peek().Sub = false;
                i++;
                continue;
            }
            if (c == '%') { while (i < end && math[i] != '\n') i++; continue; }      // a comment
            if (c != '\\') { Atom(); i++; continue; }

            // a command: \name, or \ and one other character (\{ \, \\)
            int at = i++;
            if (i >= end) { problems.Add(new Problem(at, 1, Ui.T("Missing command after \\"))); break; }
            int nameStart = i;
            if (char.IsAsciiLetter(math[i])) while (i < end && char.IsAsciiLetter(math[i])) i++;
            else i++;
            var name = math[nameStart..i];
            int len = i - at;
            switch (name)
            {
                case "\\":
                    Missing(stack.Peek());
                    stack.Peek().Sup = stack.Peek().Sub = false;
                    continue;
                case "left":
                case "middle":
                case "right":
                {
                    int d = SkipSpaces(math, i, end);
                    string? delim = d >= end ? null
                        : math[d] == '\\' ? Command(math, d, end)
                        : math[d] is '{' or '}' ? null : math[d].ToString();
                    bool known = delim != null && (math[d] != '\\' || Delimiters.Contains(delim));
                    if (!known) problems.Add(new Problem(at, len, Ui.T("Missing or unrecognized delimiter for \\{0}", name)));
                    if (known || d < end && math[d] is '{' or '}') i = d + (math[d] == '\\' ? 1 + delim!.Length : 1);   // "\left{": the brace is meant as one
                    if (name == "left") { Atom(group: true); stack.Push(new Group('l', at, len)); continue; }
                    if (!stack.Any(g => g.Kind == 'l')) { problems.Add(new Problem(at, len, Ui.T("Extra \\{0}", name))); continue; }
                    while (stack.Peek().Kind != 'l') Unclosed(stack.Pop());
                    if (name == "right") { Missing(stack.Pop()); var p = stack.Peek(); if (p.Wanted.Count == 0) p.Sup = p.Sub = false; }
                    else Missing(stack.Peek());
                    continue;
                }
                case "begin":
                case "end":
                {
                    int b = SkipSpaces(math, i, end);
                    int close = b < end && math[b] == '{' ? math.IndexOf('}', b) : -1;
                    if (close < 0 || close >= end) { problems.Add(new Problem(at, len, Ui.T("Missing argument for \\{0}", name))); continue; }
                    var env = math[(b + 1)..close].Trim();
                    i = close + 1;
                    len = i - at;
                    if (name == "begin") { Atom(group: true); stack.Push(new Group('e', at, len, env)); continue; }
                    if (!stack.Any(g => g.Kind == 'e')) { problems.Add(new Problem(at, len, Ui.T("Extra \\end{{{0}}}", env))); continue; }
                    while (stack.Peek().Kind != 'e') Unclosed(stack.Pop());
                    var opened = stack.Pop();
                    Missing(opened);
                    if (opened.Name != env) problems.Add(new Problem(at, len, Ui.T("\\begin{{{0}}} ended with \\end{{{1}}}", opened.Name, env)));
                    continue;
                }
            }
            Atom();
            if (TextArgument.Contains(name) && SkipSpaces(math, i, end) is var brace && brace < end && math[brace] == '{')
            {
                int depth = 0, k = brace;
                for (; k < end; k++)
                {
                    if (math[k] == '\\') { k++; continue; }
                    if (math[k] == '{') depth++;
                    else if (math[k] == '}' && --depth == 0) break;
                }
                if (k >= end) { problems.Add(new Problem(brace, 1, Ui.T("Missing close brace"))); break; }
                i = k + 1;
                continue;
            }
            int n = Arguments.TryGetValue(name, out var count) ? count : TextArgument.Contains(name) ? 1 : 0;     // \text x: one token
            if (n > 0)
            {
                if (name == "sqrt" && SkipSpaces(math, i, end) is var o && o < end && math[o] == '[')
                {
                    int close = math.IndexOf(']', o);
                    if (close > 0 && close < end) i = close + 1;
                }
                stack.Peek().Wanted.Push((name, at, len, n));
            }
        }
        while (stack.Count > 1) Unclosed(stack.Pop());
        Missing(stack.Pop());
        problems.Sort((a, b) => a.Offset.CompareTo(b.Offset));
        return problems;
    }

    /// <summary>The names of the rules of writing, for F7 &gt; Never show this and Tools &gt; Ignored rules.</summary>
    internal static readonly string[] Rules =
        { "latex-function", "latex-function-braces", "latex-times", "latex-operator", "latex-dots", "latex-unit" };

    static readonly string[] Functions =
    {
        "arcsin", "arccos", "arctan", "sinh", "cosh", "tanh", "sin", "cos", "tan", "cot", "sec", "csc", "log", "ln", "exp",
        "lim", "max", "min", "sup", "inf", "det", "gcd",
    };
    static readonly Regex FunctionRx = new(@"(?<![\\A-Za-z])(?:" + string.Join('|', Functions) + @")(?![A-Za-z])", RegexOptions.CultureInvariant);
    static readonly Regex FunctionBraceRx = new(@"\\(?:" + string.Join('|', Functions) + @")\s*(\{)", RegexOptions.CultureInvariant);
    // <-, != are left alone: x<-1 and n!=120 are right
    static readonly Regex OperatorRx = new(@"<=>|<->|<=|>=|->|=>", RegexOptions.CultureInvariant);
    static readonly Dictionary<string, string> OperatorCommands = new(StringComparer.Ordinal)
        { ["<=>"] = @"\Leftrightarrow", ["<->"] = @"\leftrightarrow", ["<="] = @"\le", [">="] = @"\ge", ["->"] = @"\to", ["=>"] = @"\Rightarrow" };
    // units of more than one letter after a number: a single m, s, g after a number is as often a variable (2m)
    static readonly Regex UnitRx = new(@"(?<=\d)((?:\s|\\[,;: ])*)(kWh|kHz|MHz|GHz|kPa|mol|min|Hz|Pa|kJ|kW|mm|cm|km|kg|mg|ml|mL|ms)(?![A-Za-z])",
        RegexOptions.CultureInvariant);
    // commands whose argument is text or an upright name: the rules do not look inside
    static readonly HashSet<string> Upright = new(StringComparer.Ordinal)
        { "text", "textrm", "textbf", "textit", "textsf", "texttt", "textup", "mbox", "hbox", "operatorname", "mathrm", "label", "tag", "begin", "end" };

    /// <summary>
    /// Rules of writing, as chktex has (#192, step 2): what typesets, but not as meant. <c>cos x</c> without the
    /// backslash comes out as three italic letters, <c>\cos{(x)}</c> with a space after cos, <c>2*3</c> with an asterisk,
    /// <c>&lt;=</c> and <c>-&gt;</c> as two characters, <c>...</c> as full stops, <c>5 cm</c> in italics. Heuristics: each
    /// finding names its rule, which F7 can switch off; each comes with its fix.
    /// </summary>
    internal static List<Problem> Style(string math)
    {
        var problems = new List<Problem>();
        var (start, end) = Inside(math);
        var plain = PlainMath(math, start, end);
        bool Plain(int at, int length)
        {
            for (int k = at; k < at + length; k++) if (k < start || k >= end || !plain[k]) return false;
            return true;
        }
        // a command's name written right before a letter would run into it: \le x, not \lex
        string Spaced(string command, int after) => after < end && char.IsAsciiLetter(math[after]) ? command + " " : command;

        var units = new List<(int At, int Length)>();
        foreach (Match m in UnitRx.Matches(math, start))
        {
            var gap = m.Groups[1];
            var unit = m.Groups[2];
            if (!Plain(unit.Index, unit.Length) || unit.Index + unit.Length > end) continue;
            units.Add((unit.Index, unit.Length));
            var fix = (gap.Value.Contains('\\', StringComparison.Ordinal) ? gap.Value : "\\,") + $"\\text{{{unit.Value}}}";
            problems.Add(new Problem(gap.Index, gap.Length + unit.Length, Ui.T("A unit is set in italics, as a product of letters: {0}", fix), "latex-unit", fix));
        }
        foreach (Match m in FunctionRx.Matches(math, start))
        {
            if (m.Index + m.Length > end || !Plain(m.Index, m.Length) || units.Any(u => u.At == m.Index)) continue;
            int b = m.Index - 1;
            while (b >= start && math[b] == ' ') b--;
            if (b >= start && (math[b] is '^' or '_' || math[b] == '{' && b > start && math[b - 1] is '^' or '_')) continue;   // x_{max}: a label
            problems.Add(new Problem(m.Index, m.Length, Ui.T("{0} without a backslash is set as the letters {0} in italics: \\{0}", m.Value),
                "latex-function", "\\" + m.Value));
        }
        foreach (Match m in FunctionBraceRx.Matches(math, start))
        {
            int open = m.Groups[1].Index;
            if (!Plain(m.Index, 1) || Closing(math, open, end) is not int close) continue;
            var inner = math[(open + 1)..close].Trim();
            if (inner.Length < 2 || inner[0] != '(' || inner[^1] != ')' || !OneGroup(inner)) continue;
            var command = math[m.Index..open].TrimEnd();
            problems.Add(new Problem(open, close + 1 - open, Ui.T("The braces put a space between {0} and its bracket: {1}{2}", command[1..], command, inner),
                "latex-function-braces", inner));
        }
        for (int i = start; i < end; i++)
        {
            if (math[i] != '*' || !plain[i]) continue;
            int p = i - 1, n = i + 1;
            while (p >= start && char.IsWhiteSpace(math[p])) p--;
            while (n < end && char.IsWhiteSpace(math[n])) n++;
            char before = p >= start ? math[p] : ' ', after = n < end ? math[n] : ' ';
            // between two operands, one a number: not z^*, nor a footnote's star after a formula ($x>0 *$)
            if (!(char.IsAsciiLetterOrDigit(before) || before is ')' or '}') || !(char.IsAsciiLetterOrDigit(after) || after is '(' or '\\')
                || !char.IsAsciiDigit(before) && !char.IsAsciiDigit(after)) continue;
            problems.Add(new Problem(i, 1, Ui.T("* is set as an asterisk: \\times (or \\cdot)"), "latex-times", Spaced(@"\times", i + 1)));
        }
        foreach (Match m in OperatorRx.Matches(math, start))
        {
            if (m.Index + m.Length > end || !Plain(m.Index, m.Length)) continue;
            var command = OperatorCommands[m.Value];
            problems.Add(new Problem(m.Index, m.Length, Ui.T("{0} is set as {1} separate characters: {2}", m.Value, m.Length, command), "latex-operator",
                Spaced(command, m.Index + m.Length)));
        }
        // not "$...$" alone: text about formulas, not one
        for (int i = math.IndexOf("...", start, StringComparison.Ordinal); i >= 0 && i + 3 <= end; i = math.IndexOf("...", i + 3, StringComparison.Ordinal))
            if (Plain(i, 3) && math[start..end].Trim() != "...")
                problems.Add(new Problem(i, 3, Ui.T("... is set as three full stops: \\dots"), "latex-dots", Spaced(@"\dots", i + 3)));
        problems.Sort((a, b) => a.Offset.CompareTo(b.Offset));
        return problems;
    }

    // which characters are maths the rules look at: not a comment, not the argument of \text and the like
    static bool[] PlainMath(string math, int start, int end)
    {
        var plain = new bool[math.Length];
        for (int i = start; i < end; i++) plain[i] = true;
        for (int i = start; i < end; i++)
        {
            if (math[i] == '%' && (i == 0 || math[i - 1] != '\\'))
            {
                while (i < end && math[i] != '\n') plain[i++] = false;
                continue;
            }
            if (math[i] != '\\') continue;
            var name = Command(math, i, end);
            if (!Upright.Contains(name)) { i += Math.Max(name.Length, 1); continue; }
            int b = SkipSpaces(math, i + 1 + name.Length, end);
            if (b < end && math[b] == '*') b = SkipSpaces(math, b + 1, end);
            if (b >= end || math[b] != '{' || Closing(math, b, end) is not int close) continue;
            for (int k = b; k <= close; k++) plain[k] = false;
            i = close;
        }
        return plain;
    }

    // the brace closing the one at s[open]
    static int? Closing(string s, int open, int end)
    {
        int depth = 0;
        for (int k = open; k < end; k++)
        {
            if (s[k] == '\\') { k++; continue; }
            if (s[k] == '{') depth++;
            else if (s[k] == '}' && --depth == 0) return k;
        }
        return null;
    }

    // "(x+1)": the first bracket closes at the end, not "(a)(b)"
    static bool OneGroup(string s)
    {
        int depth = 0;
        for (int k = 0; k < s.Length; k++)
        {
            if (s[k] == '(') depth++;
            else if (s[k] == ')' && --depth == 0 && k < s.Length - 1) return false;
        }
        return depth == 0;
    }

    // the formula without its delimiters
    static (int Start, int End) Inside(string m)
    {
        foreach (var (open, close) in new[] { ("$$", "$$"), ("\\[", "\\]"), ("\\(", "\\)"), ("$", "$") })
            if (m.StartsWith(open, StringComparison.Ordinal))
                return (open.Length, m.Length >= open.Length + close.Length && m.EndsWith(close, StringComparison.Ordinal) ? m.Length - close.Length : m.Length);
        return (0, m.Length);
    }

    static int SkipSpaces(string s, int i, int end)
    {
        while (i < end && char.IsWhiteSpace(s[i])) i++;
        return i;
    }

    // the name of the command at s[at] == '\\'
    static string Command(string s, int at, int end)
    {
        int i = at + 1;
        if (i >= end) return "";
        if (!char.IsAsciiLetter(s[i])) return s[i].ToString();
        while (i < end && char.IsAsciiLetter(s[i])) i++;
        return s[(at + 1)..i];
    }
}
