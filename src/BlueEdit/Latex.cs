using System.Text;

/// <summary>
/// Turns a LaTeX math fragment into plain Unicode text: \alpha -> α, x^2 -> x², \frac{1}{2} -> ½, \sqrt{x} -> √x ...
/// It is an approximation for a text terminal. Anything it does not understand is left as written, so nothing is lost.
/// </summary>
static class Latex
{
    // ---- symbols; the second set gets spaces around it (relations and binary operators) ----
    internal static readonly Dictionary<string, string> Plain = new()
    {
        ["alpha"] = "α", ["beta"] = "β", ["gamma"] = "γ", ["delta"] = "δ", ["epsilon"] = "ε", ["varepsilon"] = "ϵ", ["zeta"] = "ζ",
        ["eta"] = "η", ["theta"] = "θ", ["vartheta"] = "ϑ", ["iota"] = "ι", ["kappa"] = "κ", ["lambda"] = "λ", ["mu"] = "μ", ["nu"] = "ν",
        ["xi"] = "ξ", ["pi"] = "π", ["varpi"] = "ϖ", ["rho"] = "ρ", ["varrho"] = "ϱ", ["sigma"] = "σ", ["varsigma"] = "ς", ["tau"] = "τ",
        ["upsilon"] = "υ", ["phi"] = "φ", ["varphi"] = "ϕ", ["chi"] = "χ", ["psi"] = "ψ", ["omega"] = "ω",
        ["Gamma"] = "Γ", ["Delta"] = "Δ", ["Theta"] = "Θ", ["Lambda"] = "Λ", ["Xi"] = "Ξ", ["Pi"] = "Π", ["Sigma"] = "Σ",
        ["Upsilon"] = "Υ", ["Phi"] = "Φ", ["Psi"] = "Ψ", ["Omega"] = "Ω",
        ["infty"] = "∞", ["partial"] = "∂", ["nabla"] = "∇", ["emptyset"] = "∅", ["varnothing"] = "∅", ["forall"] = "∀", ["exists"] = "∃",
        ["neg"] = "¬", ["lnot"] = "¬", ["sum"] = "∑", ["prod"] = "∏", ["coprod"] = "∐", ["int"] = "∫", ["iint"] = "∬", ["iiint"] = "∭",
        ["oint"] = "∮", ["bigcup"] = "⋃", ["bigcap"] = "⋂", ["ldots"] = "…", ["dots"] = "…", ["cdots"] = "⋯", ["vdots"] = "⋮", ["ddots"] = "⋱",
        ["prime"] = "′", ["hbar"] = "ℏ", ["ell"] = "ℓ", ["Re"] = "ℜ", ["Im"] = "ℑ", ["aleph"] = "ℵ", ["angle"] = "∠", ["degree"] = "°",
        ["circ"] = "∘", ["bullet"] = "•", ["star"] = "⋆", ["top"] = "⊤", ["bot"] = "⊥", ["checkmark"] = "✓",
        ["sin"] = "sin", ["cos"] = "cos", ["tan"] = "tan", ["cot"] = "cot", ["sec"] = "sec", ["csc"] = "csc", ["log"] = "log", ["ln"] = "ln",
        ["exp"] = "exp", ["lim"] = "lim", ["max"] = "max", ["min"] = "min", ["sup"] = "sup", ["inf"] = "inf", ["det"] = "det", ["dim"] = "dim",
        ["arcsin"] = "arcsin", ["arccos"] = "arccos", ["arctan"] = "arctan", ["sinh"] = "sinh", ["cosh"] = "cosh", ["tanh"] = "tanh",
        ["gcd"] = "gcd", ["deg"] = "deg", ["ker"] = "ker", ["Pr"] = "Pr", ["mod"] = "mod", ["lg"] = "lg",
        ["triangle"] = "△", ["square"] = "□", ["therefore"] = "∴", ["because"] = "∵", ["nexists"] = "∄", ["varangle"] = "∡",
        ["imath"] = "ı", ["jmath"] = "ȷ", ["beth"] = "ℶ", ["bigsqcup"] = "⨆",
        ["lbrace"] = "{", ["rbrace"] = "}", ["langle"] = "⟨", ["rangle"] = "⟩", ["lfloor"] = "⌊", ["rfloor"] = "⌋", ["lceil"] = "⌈", ["rceil"] = "⌉",
        ["|"] = "‖", ["Vert"] = "‖", ["vert"] = "|", ["mid"] = "|", ["backslash"] = "\\",
    };

    internal static readonly Dictionary<string, string> Spaced = new()
    {
        ["le"] = "≤", ["leq"] = "≤", ["ge"] = "≥", ["geq"] = "≥", ["leqslant"] = "⩽", ["geqslant"] = "⩾", ["doteq"] = "≐",
        ["Longrightarrow"] = "⟹", ["Longleftarrow"] = "⟸", ["Longleftrightarrow"] = "⟺", ["longrightarrow"] = "⟶",
        ["longleftarrow"] = "⟵", ["longleftrightarrow"] = "⟷", ["longmapsto"] = "⟼", ["npreceq"] = "⋠", ["nsupseteq"] = "⊉",
        ["nsubseteq"] = "⊈", ["ne"] = "≠", ["neq"] = "≠", ["approx"] = "≈", ["equiv"] = "≡",
        ["sim"] = "∼", ["simeq"] = "≃", ["cong"] = "≅", ["propto"] = "∝", ["ll"] = "≪", ["gg"] = "≫", ["in"] = "∈", ["notin"] = "∉",
        ["ni"] = "∋", ["subset"] = "⊂", ["subseteq"] = "⊆", ["supset"] = "⊃", ["supseteq"] = "⊇", ["cup"] = "∪", ["cap"] = "∩",
        ["setminus"] = "∖", ["land"] = "∧", ["wedge"] = "∧", ["lor"] = "∨", ["vee"] = "∨", ["oplus"] = "⊕", ["otimes"] = "⊗",
        ["pm"] = "±", ["mp"] = "∓", ["times"] = "×", ["cdot"] = "·", ["div"] = "÷", ["ast"] = "∗",
        ["to"] = "→", ["rightarrow"] = "→", ["leftarrow"] = "←", ["leftrightarrow"] = "↔", ["Rightarrow"] = "⇒", ["Leftarrow"] = "⇐",
        ["Leftrightarrow"] = "⇔", ["iff"] = "⇔", ["implies"] = "⇒", ["mapsto"] = "↦", ["uparrow"] = "↑", ["downarrow"] = "↓",
        ["perp"] = "⊥", ["parallel"] = "∥", ["vdash"] = "⊢", ["models"] = "⊨",
    };

    internal static readonly Dictionary<char, char> Sup = Map("′′∘°°°0⁰1¹2²3³4⁴5⁵6⁶7⁷8⁸9⁹+⁺-⁻=⁼(⁽)⁾aᵃbᵇcᶜdᵈeᵉfᶠgᵍhʰiⁱjʲkᵏlˡmᵐnⁿoᵒpᵖrʳsˢtᵗuᵘvᵛwʷxˣyʸzᶻAᴬBᴮDᴰEᴱGᴳHᴴIᴵJᴶKᴷLᴸMᴹNᴺOᴼPᴾRᴿTᵀUᵁWᵂ");
    /// <summary>"formulaSymbols": "basic" — only characters most fonts have: no ₕ ₖ ₗ ₘ ₙ ₚ ₛ ₜ (Unicode 6.0, in none of
    /// Consolas, Cascadia Mono, Lucida Console), and ≤ ≥ for ⩽ ⩾ (\leqslant, the same relation in another cut, in none of
    /// them either). A terminal that does not take a missing character from another font (MobaXterm, ConEmu with its
    /// default alternative font) shows a box for each (seen 11 October 2026; docs/terminal-fonts.md).</summary>
    internal static bool BasicSymbols { get; set; }

    /// <summary>A symbol as shown: in basic symbols, ⩽ ⩾ as ≤ ≥.</summary>
    internal static string Symbol(string s) => BasicSymbols ? s.Replace('⩽', '≤').Replace('⩾', '≥') : s;
    static readonly HashSet<char> LateSubscripts = new("hklmnpst");

    /// <summary>The script form of a character, if it has one to use.</summary>
    internal static bool TryScript(char kind, char ch, out char m)
    {
        m = default;
        if (kind != '^' && BasicSymbols && LateSubscripts.Contains(ch)) return false;
        return (kind == '^' ? Sup : Sub).TryGetValue(ch, out m);
    }

    internal static readonly Dictionary<char, char> Sub = Map("0₀1₁2₂3₃4₄5₅6₆7₇8₈9₉+₊-₋=₌(₍)₎aₐeₑhₕiᵢjⱼkₖlₗmₘnₙoₒpₚrᵣsₛtₜuᵤvᵥxₓ");

    // ---- spacing, as TeX does it: the source's own spaces are not what is shown (a converted book writes
    // "a^{2}+\frac{2 a}{n}<2", seen 11 October 2026). A relation (= < >) gets a space on both sides, + and - too unless they
    // are a sign (first, or after another operator or an opening bracket); in exponents and indices it is all tight ----

    static readonly System.Buffers.SearchValues<char> Operators = System.Buffers.SearchValues.Create("=<>+-±∓×·÷∗≤≥⩽⩾≠≈≡∼≃≅∝≪≫∈∉∋⊂⊆⊃⊇∪∩∖∧∨⊕⊗→←↔⇒⇐⇔⟹⟸⟺⟶⟵⟷⟼≐⋠⊉⊈:");

    /// <summary>A literal character of the formula as shown: relations and binary + and - with a space on both
    /// sides; <paramref name="before"/> is what is shown before it.</summary>
    internal static string SpacedChar(char c, ReadOnlySpan<char> before)
    {
        if (c is '=' or '<' or '>') return " " + c + " ";
        if (c is not ('+' or '-')) return c.ToString();
        before = before.TrimEnd();
        bool sign = before.IsEmpty || before[^1] is '(' or '[' or '{' or ',' or ';' or '|' || Operators.Contains(before[^1]);
        return sign ? c.ToString() : " " + c + " ";
    }

    /// <summary>Whether a space of the source is kept: not between a number and a letter ("2 a" is 2a, as TeX shows it),
    /// otherwise yes (it keeps "sin x" apart; around operators the doubled spaces are collapsed).</summary>
    internal static bool KeepSpace(ReadOnlySpan<char> before, char next) =>
        !(before.Length > 0 && char.IsAsciiDigit(before[^1]) && char.IsLetter(next));

    static Dictionary<char, char> Map(string pairs)
    {
        var d = new Dictionary<char, char>();
        for (int i = 0; i + 1 < pairs.Length; i += 2) d[pairs[i]] = pairs[i + 1];
        return d;
    }

    static readonly Dictionary<string, string> Vulgar = new()
    {
        ["1/2"] = "½", ["1/3"] = "⅓", ["2/3"] = "⅔", ["1/4"] = "¼", ["3/4"] = "¾", ["1/5"] = "⅕", ["2/5"] = "⅖", ["3/5"] = "⅗",
        ["4/5"] = "⅘", ["1/6"] = "⅙", ["5/6"] = "⅚", ["1/7"] = "⅐", ["1/8"] = "⅛", ["3/8"] = "⅜", ["5/8"] = "⅝", ["7/8"] = "⅞",
        ["1/9"] = "⅑", ["1/10"] = "⅒",
    };

    internal static readonly Dictionary<char, char> Blackboard = new()
    {
        ['C'] = 'ℂ', ['H'] = 'ℍ', ['N'] = 'ℕ', ['P'] = 'ℙ', ['Q'] = 'ℚ', ['R'] = 'ℝ', ['Z'] = 'ℤ',
    };

    // ---- parser ----
    sealed class Parser
    {
        readonly string s; int i;
        int _tight;                                                // inside an exponent, index or one-row fraction: no spacing
        public Parser(string text, bool tight = false) { s = text; _tight = tight ? 1 : 0; }

        public string All() => Seq(null);

        string Seq(char? stop)
        {
            var sb = new StringBuilder();
            while (i < s.Length && (stop == null || s[i] != stop))
            {
                char c = s[i];
                if (c == '\\') sb.Append(Command());
                else if (c == '{') { i++; sb.Append(Seq('}')); if (i < s.Length) i++; }
                else if (c == '^' || c == '_') { i++; sb.Append(Script(c, TightArg())); }
                else if (c == '~' || c == '&') { sb.Append(' '); i++; }
                else if (c == '\'') { sb.Append('′'); i++; }
                else if (c == ' ') { i++; if (i < s.Length && KeepSpace(sb.ToString(), s[i])) sb.Append(' '); }
                else { sb.Append(_tight > 0 ? c.ToString() : SpacedChar(c, sb.ToString())); i++; }
            }
            return sb.ToString();
        }

        string TightArg() { _tight++; try { return Arg(); } finally { _tight--; } }

        void SkipSpaces() { while (i < s.Length && s[i] == ' ') i++; }

        // one argument: {group}, a command, or a single character
        string Arg()
        {
            SkipSpaces();
            if (i >= s.Length) return "";
            if (s[i] == '{') { i++; var g = Seq('}'); if (i < s.Length) i++; return g; }
            if (s[i] == '\\') return Command();
            return s[i++].ToString();
        }

        // the argument of 	ext: words, shown as written ("right-angled" keeps its hyphen tight)
        string TextArg()
        {
            SkipSpaces();
            if (i >= s.Length || s[i] != '{') return Arg();
            int depth = 0, st = i + 1;
            for (; i < s.Length; i++)
            {
                if (s[i] == '{') depth++;
                else if (s[i] == '}' && --depth == 0) { i++; return s[st..(i - 1)]; }
            }
            return s[st..];
        }

        static string Script(char kind, string arg)
        {
            var sb = new StringBuilder();
            foreach (char ch in arg)
            {
                if (ch == ' ') continue;
                if (TryScript(kind, ch, out var m)) sb.Append(m);
                else return arg.Length == 1 ? kind + arg : kind + "(" + arg + ")";      // no Unicode form: keep readable
            }
            return sb.ToString();
        }

        // one factor: letters and digits, script digits included ("x³/3", not "(x³)/3")
        static bool IsAtom(string x) => x.Length > 0 && x.All(ch => char.IsLetterOrDigit(ch) || char.IsNumber(ch) || ch is '′' or '.');
        static string Wrap(string x) => IsAtom(x) ? x : "(" + x + ")";

        // a factor comes next: a letter, a digit, a bracket, a Greek letter, a root or a fraction. "\frac{6}{7}x" written
        // "6/7x" read as 6/(7x)
        bool FactorNext()
        {
            int j = i;
            while (j < s.Length && s[j] == ' ') j++;
            if (j >= s.Length) return false;
            if (char.IsLetterOrDigit(s[j]) || s[j] is '(' or '{' or '[') return true;
            if (s[j] != '\\') return false;
            int k = j + 1;
            while (k < s.Length && char.IsLetter(s[k])) k++;
            var name = s[(j + 1)..k];
            return name is "left" or "sqrt" or "frac" or "dfrac" or "tfrac"
                || (Plain.TryGetValue(name, out var sym) && sym.Length > 0 && char.IsLetter(sym[0]));
        }

        string Command()
        {
            i++;                                                   // backslash
            if (i >= s.Length) return "\\";
            string name;
            if (char.IsLetter(s[i]))
            {
                int st = i; while (i < s.Length && char.IsLetter(s[i])) i++;
                name = s[st..i];
            }
            else name = s[i++].ToString();

            switch (name)
            {
                case "frac": case "dfrac": case "tfrac": case "cfrac":
                    {
                        string n = TightArg(), d = TightArg();
                        if (Vulgar.TryGetValue(n + "/" + d, out var v)) return v;
                        var f = Wrap(n) + "/" + Wrap(d);
                        return FactorNext() ? "(" + f + ")" : f;
                    }
                case "binom": { string n = Arg(), k = Arg(); return "C(" + n + "," + k + ")"; }
                case "sqrt":
                    {
                        string index = "";
                        SkipSpaces();
                        if (i < s.Length && s[i] == '[') { int e = s.IndexOf(']', i); if (e > 0) { index = s[(i + 1)..e]; i = e + 1; } }
                        string r = Arg();
                        string root = index switch { "" or "2" => "√", "3" => "∛", "4" => "∜", _ => Script('^', index) + "√" };
                        return root + Wrap(r);
                    }
                case "text": case "textrm": case "textit": case "textbf": case "mbox": case "textnormal": return TextArg();
                case "mathrm": case "mathbf": case "mathit": case "mathsf": case "mathtt": case "mathcal": case "mathfrak":
                case "mathscr": case "operatorname": case "boldsymbol": case "bm":
                    return Arg();
                case "mathbb": { var a = Arg(); return string.Concat(a.Select(ch => Blackboard.TryGetValue(ch, out var b) ? b : ch)); }
                case "overline": case "bar": return "bar(" + Arg() + ")";
                case "hat": case "widehat": return "hat(" + Arg() + ")";
                case "vec": return "vec(" + Arg() + ")";
                case "dot": return "dot(" + Arg() + ")";
                case "ddot": return "ddot(" + Arg() + ")";
                case "tilde": case "widetilde": return "tilde(" + Arg() + ")";
                case "underline": return "_" + Arg() + "_";
                case "left": case "right": case "big": case "Big": case "bigg": case "Bigg": case "bigl": case "bigr":
                case "Bigl": case "Bigr": case "biggl": case "biggr": case "displaystyle": case "textstyle": case "limits": case "nolimits":
                    SkipSpaces(); if (i < s.Length && s[i] == '.' && name is "left" or "right") i++;   // "\left." = invisible delimiter
                    return "";
                case ",": case ";": case ":": case " ": case "quad": case "qquad": case "enspace": return " ";
                case "!": return "";
                case "\\": return " ; ";
                case "{": return "{"; case "}": return "}";
                case "%": return "%"; case "$": return "$"; case "&": return "&"; case "#": return "#"; case "_": return "_";
                case "label": case "tag": case "nonumber": case "notag": Arg(); return "";
            }

            if (Spaced.TryGetValue(name, out var rel)) { rel = Symbol(rel); return _tight > 0 ? rel : " " + rel + " "; }
            if (Plain.TryGetValue(name, out var sym)) return sym;
            // unknown command: show as written, keeping its braced argument
            if (i < s.Length && s[i] == '{')
            {
                i++; var g = Seq('}'); if (i < s.Length) i++;
                return "\\" + name + "{" + g + "}";
            }
            return "\\" + name;
        }
    }

    /// <summary>The formula in one row of Unicode; <paramref name="tight"/>: without spacing around operators, as an
    /// exponent or index is set.</summary>
    public static string ToUnicode(string tex, bool tight = false)
    {
        var text = new Parser(tex, tight).All();
        // collapse the doubled spaces that padding around operators can create
        var sb = new StringBuilder(text.Length);
        foreach (char c in text) { if (c == ' ' && sb.Length > 0 && sb[^1] == ' ') continue; sb.Append(c); }
        return sb.ToString().Trim();
    }
}
