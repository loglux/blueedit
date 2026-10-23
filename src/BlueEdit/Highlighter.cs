using System.Text.RegularExpressions;

enum Tok { Normal, Keyword, String, Comment, Number, Heading, Code, Emphasis, Math }

/// <summary>A span that may run over lines. <paramref name="AtLineStart"/>: it opens only as the first thing on its line
/// (Markdown's <c>\[</c>, which elsewhere on a line is an escaped bracket).</summary>
sealed record Block(string Start, string End, Tok Kind, bool AtLineStart = false);

sealed class Lang
{
    public string Name = "";
    public HashSet<string> Keywords = new();
    public bool IgnoreCase;
    public string[] LineComments = Array.Empty<string>();
    public Block[] Blocks = Array.Empty<Block>();
    public string Quotes = "\"'";
    public bool DoubledQuoteEscape;   // Pascal / SQL / batch style 'it''s'
    public (Regex Rx, Tok Kind)[] Extra = Array.Empty<(Regex, Tok)>();   // regex-driven rules (HTML tags, Markdown...)
    public bool NoNumbers;
    /// <summary>More words for Ctrl+Space besides the keywords: standard cmdlets, built-in functions, commands.</summary>
    public string[] Words = Array.Empty<string>();
    /// <summary>Words may contain hyphens (PowerShell's Get-ChildItem): one word for completion.</summary>
    public bool HyphenWords;
    /// <summary>A line comment starts only where a word could (line start, after a space or ; | &amp; ( ): shell's
    /// <c>$#</c>, <c>${#a}</c> and a URL's <c>#anchor</c> are not comments.</summary>
    public bool CommentAtWordStart;
    /// <summary>Spans the block scan steps over: Markdown's inline $…$, so that "$a$$b$" is two formulas, not an
    /// opening $$ that makes the rest of the file a formula (#128).</summary>
    public Regex? Opaque;

    public bool MultiLine => Blocks.Length > 0;
}

static class Languages
{
    static HashSet<string> W(string s, bool ic = false) =>
        new(s.Split(' ', StringSplitOptions.RemoveEmptyEntries), ic ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    static readonly Block CBlock = new("/*", "*/", Tok.Comment);

    static readonly Lang CSharp = new()
    {
        Name = "C#", LineComments = new[] { "//" }, Blocks = new[] { CBlock },
        Keywords = W("abstract as base bool break byte case catch char checked class const continue decimal default delegate do double " +
                     "else enum event explicit extern false finally fixed float for foreach goto if implicit in int interface internal is lock " +
                     "long namespace new null object operator out override params private protected public readonly ref return sbyte sealed " +
                     "short sizeof static string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using var virtual " +
                     "void volatile while async await record init required get set yield partial where"),
    };

    static readonly Lang C = new()
    {
        Name = "C/C++", LineComments = new[] { "//" }, Blocks = new[] { CBlock },
        Keywords = W("auto break case char const continue default do double else enum extern float for goto if inline int long register " +
                     "return short signed sizeof static struct switch typedef union unsigned void volatile while class namespace template " +
                     "typename public private protected virtual new delete this try catch throw using bool true false nullptr constexpr " +
                     "include define ifdef ifndef endif pragma"),
    };

    static readonly Lang Java = new()
    {
        Name = "Java", LineComments = new[] { "//" }, Blocks = new[] { CBlock },
        Keywords = W("abstract assert boolean break byte case catch char class const continue default do double else enum extends final " +
                     "finally float for goto if implements import instanceof int interface long native new null package private protected " +
                     "public return short static strictfp super switch synchronized this throw throws transient true false try void volatile while var"),
    };

    static readonly Lang JavaScript = new()
    {
        Name = "JavaScript", LineComments = new[] { "//" }, Blocks = new[] { CBlock }, Quotes = "\"'`",
        Keywords = W("async await break case catch class const continue debugger default delete do else export extends false finally for " +
                     "function if import in instanceof let new null of return super switch this throw true try typeof undefined var void " +
                     "while with yield interface type enum implements namespace readonly abstract declare"),
    };

    static readonly Lang Go = new()
    {
        Name = "Go", LineComments = new[] { "//" }, Blocks = new[] { CBlock }, Quotes = "\"'`",
        Keywords = W("break case chan const continue default defer else fallthrough for func go goto if import interface map package " +
                     "range return select struct switch type var true false nil"),
    };

    static readonly Lang Rust = new()
    {
        Name = "Rust", LineComments = new[] { "//" }, Blocks = new[] { CBlock }, Quotes = "\"",
        Keywords = W("as async await break const continue crate dyn else enum extern false fn for if impl in let loop match mod move mut " +
                     "pub ref return self Self static struct super trait true type unsafe use where while"),
    };

    const string PythonBuiltins =
        "abs all any ascii bin bool breakpoint bytearray bytes callable chr classmethod compile complex delattr dict dir divmod " +
        "enumerate eval exec filter float format frozenset getattr globals hasattr hash help hex id input int isinstance issubclass " +
        "iter len list locals map max memoryview min next object oct open ord pow print property range repr reversed round set " +
        "setattr slice sorted staticmethod str sum super tuple type vars zip __name__ __main__ __init__ __file__ " +
        "Exception ValueError TypeError KeyError IndexError RuntimeError OSError FileNotFoundError NotImplementedError " +
        "StopIteration AttributeError ImportError ZeroDivisionError";

    static readonly Lang Python = new()
    {
        Name = "Python", LineComments = new[] { "#" },
        Blocks = new[] { new Block("\"\"\"", "\"\"\"", Tok.String), new Block("'''", "'''", Tok.String) },
        Keywords = W("and as assert async await break class continue def del elif else except False finally for from global if import in " +
                     "is lambda None nonlocal not or pass raise return True try while with yield self match case"),
        Words = PythonBuiltins.Split(' '),
        Extra = new[]
        {
            (new Regex(@"(?<![.\w])(?:" + PythonBuiltins.Replace(' ', '|') + @")\b", RegexOptions.Compiled), Tok.Heading),   // built-ins
            (new Regex(@"^\s*@[\w.]+", RegexOptions.Compiled), Tok.Emphasis),                                                  // @decorators
        },
    };

    const string PowerShellCmdlets =
        "Get-ChildItem Get-Content Set-Content Add-Content Clear-Content Get-Item Set-Item Remove-Item Copy-Item Move-Item New-Item " +
        "Rename-Item Test-Path Join-Path Split-Path Resolve-Path Get-Location Set-Location Push-Location Pop-Location Get-Process " +
        "Stop-Process Start-Process Wait-Process Get-Service Start-Service Stop-Service Restart-Service Get-Command Get-Help Get-Member " +
        "Select-Object Where-Object ForEach-Object Sort-Object Group-Object Measure-Object Compare-Object Tee-Object Select-String " +
        "Out-File Out-Null Out-String Out-Host Out-GridView Write-Host Write-Output Write-Error Write-Warning Write-Verbose Write-Progress " +
        "Read-Host Format-Table Format-List ConvertTo-Json ConvertFrom-Json ConvertTo-Csv ConvertFrom-Csv Import-Csv Export-Csv " +
        "Invoke-WebRequest Invoke-RestMethod Invoke-Expression Invoke-Command Invoke-Item New-Object Get-Date Start-Sleep Get-Variable " +
        "Set-Variable Import-Module Export-ModuleMember Get-Module Get-ItemProperty Set-ItemProperty Get-Clipboard Set-Clipboard " +
        "Get-FileHash Expand-Archive Compress-Archive Get-Random Get-Credential Start-Job Get-Job Receive-Job Wait-Job Add-Type " +
        "Get-WinEvent Get-CimInstance Test-Connection";

    static readonly Lang PowerShell = new()
    {
        Name = "PowerShell", LineComments = new[] { "#" }, Blocks = new[] { new Block("<#", "#>", Tok.Comment) }, IgnoreCase = true,
        Keywords = W("begin break catch class continue data do dynamicparam else elseif end exit filter finally for foreach from function " +
                     "if in param process return switch throw trap try until using var while workflow", true),
        Words = PowerShellCmdlets.Split(' '),
        HyphenWords = true,
        Extra = new[]
        {
            (new Regex(@"(?<![\w$-])[A-Za-z]+-[A-Za-z][A-Za-z0-9]*", RegexOptions.Compiled), Tok.Heading),                     // Verb-Noun
            (new Regex(@"\$(?:\{[^}]*\}|[\w:?]+)", RegexOptions.Compiled), Tok.Emphasis),                                      // $variables
            (new Regex(@"(?<![\w-])-(?:eq|ne|gt|ge|lt|le|like|notlike|match|notmatch|contains|notcontains|in|notin|replace|split|join|" +
                       @"and|or|not|xor|is|isnot|as|f|c?eq|c?ne|c?like|c?match|i?eq)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), Tok.Keyword),
            (new Regex(@"\[[A-Za-z][\w.]*(?:\[\])?\]", RegexOptions.Compiled), Tok.Keyword),                                    // [string], [int[]]
        },
    };

    static readonly Lang Batch = new()
    {
        Name = "Batch", LineComments = new[] { "REM ", "::" }, Quotes = "\"", IgnoreCase = true,
        Keywords = W("echo set if else goto call exit for in do not exist defined errorlevel pause cd dir copy move del type start " +
                     "setlocal endlocal shift pushd popd cls title choice timeout mkdir rmdir ren", true),
        Words = "enabledelayedexpansion findstr xcopy robocopy where tasklist taskkill nul".Split(' '),
        Extra = new[]
        {
            (new Regex(@"%~[a-z]*[0-9]|%[0-9*]|%%~?[a-z]|%[\w~:.,=\-\\]+%|![\w]+!", RegexOptions.Compiled | RegexOptions.IgnoreCase), Tok.Emphasis),
            (new Regex(@"^\s*:[^:\s]\S*", RegexOptions.Compiled), Tok.Heading),                                                // :labels
            (new Regex(@"^\s*@?echo\s+(?:on|off)\b|^\s*@", RegexOptions.Compiled | RegexOptions.IgnoreCase), Tok.Keyword),     // @echo off
            (new Regex(@"^\s*@?rem\b.*$", RegexOptions.Compiled | RegexOptions.IgnoreCase), Tok.Comment),                     // rem, also bare
        },
    };

    static readonly Lang Sql = new()
    {
        Name = "SQL", LineComments = new[] { "--" }, Blocks = new[] { CBlock }, IgnoreCase = true, DoubledQuoteEscape = true,
        Keywords = W("select from where and or not in is null insert into values update set delete create alter drop table index view " +
                     "join inner left right outer on group by order having limit as distinct union all case when then else end primary key " +
                     "foreign references default constraint begin commit rollback like between exists count sum avg min max", true),
    };

    static readonly Lang Pascal = new()
    {
        Name = "Pascal", LineComments = new[] { "//" }, IgnoreCase = true, DoubledQuoteEscape = true, Quotes = "'",
        Blocks = new[] { new Block("{", "}", Tok.Comment), new Block("(*", "*)", Tok.Comment) },
        Keywords = W("and array begin case const div do downto else end file for function goto if in label mod nil not of or packed " +
                     "procedure program record repeat set then to type until var while with unit uses interface implementation " +
                     "string integer boolean char real byte word longint true false", true),
    };

    static readonly Lang Json = new()
    {
        Name = "JSON", Quotes = "\"", Keywords = W("true false null"),
    };

    static readonly Lang Html = new()
    {
        Name = "HTML/XML", Blocks = new[] { new Block("<!--", "-->", Tok.Comment) },
        Extra = new[] { (new Regex(@"</?[A-Za-z][\w:.-]*|/?>", RegexOptions.Compiled), Tok.Keyword) },
    };

    // $VAR, ${VAR...}, $(command), $1, $@, $#, $?
    static readonly Regex ShellVariable = new(@"\$(?:\{[^}]*\}|\([^)]*\)|[A-Za-z_]\w*|[0-9@#?*!$-])", RegexOptions.Compiled);

    static readonly Lang Shell = new()
    {
        Name = "Shell", LineComments = new[] { "#" }, CommentAtWordStart = true,
        Keywords = W("if then else elif fi for while until do done case esac in function select time return break continue " +
                     "local export readonly declare typeset unset shift exit source alias set trap eval exec"),
        Words = ("echo printf read cd pwd ls cp mv rm mkdir rmdir ln chmod chown cat grep sed awk find xargs sort uniq head tail " +
                 "wc cut tr tee test true false sleep kill ps curl wget tar gzip ssh scp sudo apt git dotnet python3 bash sh " +
                 "basename dirname realpath mktemp date env which command getopts wait trap nohup").Split(' '),
        Extra = new[]
        {
            (ShellVariable, Tok.Emphasis),
            (new Regex(@"^#!.*$", RegexOptions.Compiled), Tok.Comment),                                                         // #!/bin/bash
            (new Regex(@"^\s*(?:function\s+)?[A-Za-z_][\w-]*\s*\(\)", RegexOptions.Compiled), Tok.Heading),                    // name() {
        },
    };

    static readonly Lang Yaml = new()
    {
        Name = "YAML", LineComments = new[] { "#" }, CommentAtWordStart = true,
        Keywords = W("true false null yes no on off True False Null"),
        Extra = new[]
        {
            (new Regex(@"(?<=^\s*(?:-\s+)*)(?!-\s)[^\s#'""{\[\]}:][^#:]*?(?=:(?:\s|$))", RegexOptions.Compiled), Tok.Keyword),  // key:
            (new Regex(@"(?<=^\s*(?:-\s+)*)(?:""[^""]*""|'[^']*')(?=:(?:\s|$))", RegexOptions.Compiled), Tok.Keyword),  // "key":
            (new Regex(@"(?<=\s|^)[&*][\w-]+", RegexOptions.Compiled), Tok.Emphasis),                                         // &anchor, *alias
            (new Regex(@"^(?:---|\.\.\.)\s*$", RegexOptions.Compiled), Tok.Heading),                                          // document markers
            (new Regex(@"\$\{\{[^}]*\}\}", RegexOptions.Compiled), Tok.Emphasis),                                             // GitHub ${{ }}
        },
    };

    static readonly Lang Makefile = new()
    {
        Name = "Makefile", LineComments = new[] { "#" },
        Keywords = W("ifeq ifneq ifdef ifndef else endif include sinclude define endef export unexport override vpath"),
        Extra = new[]
        {
            (new Regex(@"^[^\s#=:][^#=:]*?:(?!=)", RegexOptions.Compiled), Tok.Heading),                                       // target:
            (new Regex(@"\$(?:\([^)]*\)|\{[^}]*\}|[@<^?*%+|])", RegexOptions.Compiled), Tok.Emphasis),                       // $(VAR) $@ $<
            (new Regex(@"^\.[A-Z_]+", RegexOptions.Compiled), Tok.Keyword),                                                     // .PHONY
        },
    };

    static readonly Lang Dockerfile = new()
    {
        Name = "Dockerfile", LineComments = new[] { "#" }, CommentAtWordStart = true,
        Extra = new[]
        {
            (new Regex(@"^\s*(?:FROM|RUN|CMD|LABEL|MAINTAINER|EXPOSE|ENV|ADD|COPY|ENTRYPOINT|VOLUME|USER|WORKDIR|ARG|ONBUILD|" +
                       @"STOPSIGNAL|HEALTHCHECK|SHELL)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), Tok.Keyword),
            (new Regex(@"(?<=\s)AS(?=\s+\w)", RegexOptions.Compiled | RegexOptions.IgnoreCase), Tok.Keyword),                 // FROM x AS build
            (ShellVariable, Tok.Emphasis),
        },
    };

    static readonly Lang Ini = new()
    {
        Name = "INI", LineComments = new[] { ";", "#" }, Quotes = "\"",
        Extra = new[] { (new Regex(@"^\s*\[[^\]]*\]", RegexOptions.Compiled), Tok.Keyword) },
    };


    // $…$ as Obsidian draws it: no space just inside the dollars, not \$; "$a$$b$" is two formulas (#128)
    static readonly Regex InlineMath = new(@"(?<!\\)\$[^$\s][^$]*?(?<!\s)\$", RegexOptions.Compiled);

    static readonly Lang Markdown = new()
    {
        Name = "Markdown", Quotes = "", NoNumbers = true,
        Blocks = new[] { new Block("```", "```", Tok.Code), new Block("<!--", "-->", Tok.Comment), new Block("$$", "$$", Tok.Math),
            new Block("\\[", "\\]", Tok.Math, AtLineStart: true) },
        Opaque = InlineMath,
        Extra = new[]
        {
            (InlineMath, Tok.Math),
            (new Regex(@"~~[^~]+~~", RegexOptions.Compiled), Tok.Comment),
            (new Regex(@"(?<![\w*])\*[^*\s][^*]*?\*(?![\w*])", RegexOptions.Compiled), Tok.Emphasis),
            (new Regex(@"\[\[[^\]]+\]\]", RegexOptions.Compiled), Tok.String),
            (new Regex(@"(?<!\w)#[\p{L}][\w/-]*", RegexOptions.Compiled), Tok.Keyword),
            (new Regex(@"^\s*[-*+]\s\[[ xX]\]", RegexOptions.Compiled), Tok.Keyword),
            (new Regex(@"\|", RegexOptions.Compiled), Tok.Keyword),
            (new Regex(@"^\s{0,3}#{1,6}\s.*$", RegexOptions.Compiled), Tok.Heading),
            (new Regex(@"^\s*>.*$", RegexOptions.Compiled), Tok.Comment),
            (new Regex(@"^\s*([-*+]|\d+[.)])\s", RegexOptions.Compiled), Tok.Keyword),
            (new Regex(@"^\s*([-*_]\s*){3,}$", RegexOptions.Compiled), Tok.Keyword),
            (new Regex(@"`[^`]+`", RegexOptions.Compiled), Tok.Code),
            (new Regex(@"(\*\*|__)[^*_]+(\*\*|__)", RegexOptions.Compiled), Tok.Emphasis),
            (new Regex(@"\[[^\]]*\]\([^)]*\)", RegexOptions.Compiled), Tok.String),
        },
    };

    static readonly Lang LaTeX = new()
    {
        Name = "LaTeX", Quotes = "", NoNumbers = true, LineComments = new[] { "%" },
        Blocks = new[] { new Block("\\[", "\\]", Tok.Math), new Block("$$", "$$", Tok.Math) },
        Extra = new[]
        {
            (new Regex(@"\\[A-Za-z@]+\*?|\\.", RegexOptions.Compiled), Tok.Keyword),
            (new Regex(@"(?<!\\)\$[^$]+\$", RegexOptions.Compiled), Tok.Math),
            (new Regex(@"\\(?:begin|end)\{[^}]*\}", RegexOptions.Compiled), Tok.Heading),
            (new Regex(@"\\(?:part|chapter|(?:sub)*section|paragraph)\*?\{[^}]*\}", RegexOptions.Compiled), Tok.Heading),
            (new Regex(@"\\(?:ref|eqref|cite[a-z]*|label)\{[^}]*\}", RegexOptions.Compiled), Tok.String),
        },
    };

    static readonly Dictionary<string, Lang> ByExt = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = CSharp,
        [".c"] = C, [".h"] = C, [".cpp"] = C, [".cc"] = C, [".hpp"] = C, [".cxx"] = C,
        [".java"] = Java,
        [".js"] = JavaScript, [".ts"] = JavaScript, [".jsx"] = JavaScript, [".tsx"] = JavaScript, [".mjs"] = JavaScript,
        [".go"] = Go,
        [".rs"] = Rust,
        [".py"] = Python, [".pyw"] = Python, [".pyi"] = Python,
        [".ps1"] = PowerShell, [".psm1"] = PowerShell, [".psd1"] = PowerShell,
        [".bat"] = Batch, [".cmd"] = Batch,
        [".sql"] = Sql,
        [".pas"] = Pascal, [".pp"] = Pascal, [".dpr"] = Pascal,
        [".json"] = Json,
        [".html"] = Html, [".htm"] = Html, [".xml"] = Html, [".csproj"] = Html, [".config"] = Html,
        [".tex"] = LaTeX, [".sty"] = LaTeX, [".cls"] = LaTeX, [".bib"] = LaTeX,
        [".md"] = Markdown, [".markdown"] = Markdown,
        [".ini"] = Ini, [".cfg"] = Ini, [".conf"] = Ini, [".toml"] = Ini,
        [".sh"] = Shell, [".bash"] = Shell, [".zsh"] = Shell, [".ksh"] = Shell,
        [".yml"] = Yaml, [".yaml"] = Yaml,
        [".mk"] = Makefile, [".dockerfile"] = Dockerfile,
    };

    /// <summary>Files known by their whole name rather than an extension.</summary>
    static readonly Dictionary<string, Lang> ByFileName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Makefile"] = Makefile, ["GNUmakefile"] = Makefile, ["makefile"] = Makefile,
        ["Dockerfile"] = Dockerfile, ["Containerfile"] = Dockerfile,
        [".bashrc"] = Shell, [".bash_profile"] = Shell, [".profile"] = Shell, [".zshrc"] = Shell, [".bash_aliases"] = Shell,
    };

    public static Lang? ForPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var name = System.IO.Path.GetFileName(path);
        if (ByFileName.TryGetValue(name, out var byName)) return byName;
        if (name.StartsWith("Dockerfile.", StringComparison.OrdinalIgnoreCase)) return Dockerfile;      // Dockerfile.dev
        return ByExt.TryGetValue(System.IO.Path.GetExtension(path), out var l) ? l : null;
    }

    /// <summary>The language of a script by its first line, "#!/bin/bash" or "#!/usr/bin/env python3": for files
    /// without an extension. Null when it is not a #! line or the interpreter is unknown.</summary>
    public static Lang? ForShebang(string firstLine)
    {
        if (!firstLine.StartsWith("#!", StringComparison.Ordinal)) return null;
        var parts = firstLine[2..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        var program = System.IO.Path.GetFileName(parts[0]);
        if (program == "env") program = parts.Skip(1).FirstOrDefault(p => !p.StartsWith('-')) ?? "";
        program = program.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '.');      // python3.12 -> python
        return program switch
        {
            "sh" or "bash" or "zsh" or "ksh" or "dash" or "ash" => Shell,
            "python" => Python,
            "pwsh" or "powershell" => PowerShell,
            "node" or "deno" or "bun" => JavaScript,
            "make" => Makefile,
            _ => null,
        };
    }

    static readonly Dictionary<string, Lang> ByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["csharp"] = CSharp, ["c#"] = CSharp, ["cs"] = CSharp,
        ["c"] = C, ["cpp"] = C, ["c++"] = C, ["h"] = C,
        ["java"] = Java, ["kotlin"] = Java,
        ["javascript"] = JavaScript, ["js"] = JavaScript, ["typescript"] = JavaScript, ["ts"] = JavaScript, ["jsx"] = JavaScript, ["tsx"] = JavaScript,
        ["go"] = Go, ["golang"] = Go,
        ["rust"] = Rust, ["rs"] = Rust,
        ["python"] = Python, ["py"] = Python,
        ["powershell"] = PowerShell, ["pwsh"] = PowerShell, ["ps"] = PowerShell, ["ps1"] = PowerShell,
        ["batch"] = Batch, ["bat"] = Batch, ["cmd"] = Batch,
        ["sql"] = Sql,
        ["pascal"] = Pascal, ["delphi"] = Pascal,
        ["json"] = Json, ["jsonc"] = Json,
        ["html"] = Html, ["xml"] = Html, ["svg"] = Html,
        ["latex"] = LaTeX, ["tex"] = LaTeX,
        ["markdown"] = Markdown, ["md"] = Markdown,
        ["ini"] = Ini, ["toml"] = Ini, ["conf"] = Ini,
        ["sh"] = Shell, ["bash"] = Shell, ["shell"] = Shell, ["zsh"] = Shell, ["console"] = Shell, ["shellscript"] = Shell,
        ["yaml"] = Yaml, ["yml"] = Yaml,
        ["makefile"] = Makefile, ["make"] = Makefile, ["dockerfile"] = Dockerfile, ["docker"] = Dockerfile,
    };

    /// <summary>The language named after the opening ``` of a Markdown code block ("cs", "python", "c++", "{.py}"),
    /// or null when unknown.</summary>
    public static Lang? ForFence(string info)
    {
        var name = info.Trim().TrimStart('{', '.').Split(' ', '{', '}', ',')[0].Trim();
        if (name.Length == 0) return null;
        return ByName.TryGetValue(name, out var l) ? l : ByExt.GetValueOrDefault("." + name);
    }
}

static class Highlighter
{
    /// <summary>
    /// Classifies one line. <paramref name="state"/> is 0 or (index+1) of the multi-line block the line starts inside;
    /// the state the next line starts in is returned through <paramref name="next"/>.
    /// </summary>
    public static Tok[] Classify(string line, Lang? lang, int state, out int next)
    {
        var t = new Tok[line.Length];
        next = 0;
        if (lang == null) return t;
        var cmp = lang.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        int i = 0;

        foreach (var (rx, kind) in lang.Extra)
            foreach (Match m in rx.Matches(line)) Array.Fill(t, kind, m.Index, m.Length);

        if (state > 0)
        {
            var b = lang.Blocks[state - 1];
            int e = line.IndexOf(b.End, cmp);
            if (e < 0) { Array.Fill(t, b.Kind); next = state; return t; }
            Array.Fill(t, b.Kind, 0, e + b.End.Length);
            i = e + b.End.Length;
        }

        var opaque = lang.Opaque != null && line.Contains('$', StringComparison.Ordinal)
            ? lang.Opaque.Matches(line).ToDictionary(m => m.Index, m => m.Length) : null;
        while (i < line.Length)
        {
            char c = line[i];

            if (StartsWithAny(line, i, lang.LineComments, cmp) && !(i > 0 && line[i - 1] == '\\' && lang.Name == "LaTeX")
                && !(lang.CommentAtWordStart && i > 0 && !char.IsWhiteSpace(line[i - 1]) && ";|&(".IndexOf(line[i - 1]) < 0))
            {
                Array.Fill(t, Tok.Comment, i, line.Length - i);
                break;
            }

            if (opaque != null && opaque.TryGetValue(i, out int skip)) { i += skip; continue; }

            int bi = BlockAt(line, i, lang, cmp);
            if (bi >= 0)
            {
                var b = lang.Blocks[bi];
                int e = line.IndexOf(b.End, i + b.Start.Length, cmp);
                int end = e < 0 ? line.Length : e + b.End.Length;
                Array.Fill(t, b.Kind, i, end - i);
                if (e < 0) { next = bi + 1; break; }
                i = end;
                continue;
            }

            if (lang.Quotes.Contains(c))
            {
                int j = i + 1;
                while (j < line.Length)
                {
                    if (!lang.DoubledQuoteEscape && line[j] == '\\') { j += 2; continue; }
                    if (line[j] == c)
                    {
                        if (lang.DoubledQuoteEscape && j + 1 < line.Length && line[j + 1] == c) { j += 2; continue; }
                        j++; break;
                    }
                    j++;
                }
                j = Math.Min(j, line.Length);
                Array.Fill(t, Tok.String, i, j - i);
                i = j;
                continue;
            }

            if (!lang.NoNumbers && char.IsDigit(c) && (i == 0 || !IsWord(line[i - 1])))
            {
                int j = i + 1;
                while (j < line.Length && (char.IsLetterOrDigit(line[j]) || line[j] == '.' || line[j] == '_')) j++;
                Array.Fill(t, Tok.Number, i, j - i);
                i = j;
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                int j = i + 1;
                while (j < line.Length && IsWord(line[j])) j++;
                if (lang.Keywords.Contains(line.Substring(i, j - i))) Array.Fill(t, Tok.Keyword, i, j - i);
                i = j;
                continue;
            }

            i++;
        }
        return t;
    }

    // Unchanged lines are looked up instead of re-tokenised: after an edit only the edited line costs anything.
    // concurrent: a background thread (or parallel tests) must not corrupt it
    static readonly System.Collections.Concurrent.ConcurrentDictionary<(Lang, string, int), (Tok[] Toks, int Next)> Cache = new();

    public static Tok[] ClassifyCached(string line, Lang? lang, int state, out int next)
    {
        if (lang == null || line.Length == 0) return Classify(line, lang, state, out next);
        var key = (lang, line, state);
        if (Cache.TryGetValue(key, out var hit)) { next = hit.Next; return hit.Toks; }
        var toks = Classify(line, lang, state, out next);
        if (Cache.Count > 60000) Cache.Clear();
        Cache[key] = (toks, next);
        return toks;
    }

    /// <summary>Block-comment state at the start of every line (only meaningful for languages with multi-line blocks).</summary>
    public static int[] States(IReadOnlyList<string> lines, Lang? lang)
    {
        var s = new int[lines.Count + 1];
        if (lang == null || !lang.MultiLine) return s;
        int st = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            s[i] = st;
            ClassifyCached(lines[i], lang, st, out st);
        }
        s[lines.Count] = st;
        return s;
    }

    static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_';

    static bool StartsWithAny(string s, int i, string[] prefixes, StringComparison cmp)
    {
        foreach (var p in prefixes)
            if (string.CompareOrdinal(s, i, p, 0, p.Length) == 0 ||
                (cmp != StringComparison.Ordinal && string.Compare(s, i, p, 0, p.Length, cmp) == 0))
                return true;
        return false;
    }

    static int BlockAt(string s, int i, Lang lang, StringComparison cmp)
    {
        for (int k = 0; k < lang.Blocks.Length; k++)
            if (string.Compare(s, i, lang.Blocks[k].Start, 0, lang.Blocks[k].Start.Length, cmp) == 0
                && (!lang.Blocks[k].AtLineStart || s.AsSpan(0, i).IsWhiteSpace())) return k;
        return -1;
    }
}
