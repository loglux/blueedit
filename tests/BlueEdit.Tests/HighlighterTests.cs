using Xunit;

public class HighlighterTests
{
    // one letter per character: . normal, K keyword, S string, C comment, N number, H heading, c code, E emphasis, M math
    static string Render(string line, string path, int state = 0)
    {
        var t = Highlighter.Classify(line, Languages.ForPath(path), state, out _);
        return new string(t.Select(x => ".KSCNHcEM"[(int)x]).ToArray());
    }

    [Fact]
    public void Csharp_keywords_numbers_and_comments() =>
        Assert.Equal("KKK.....NN..CCCCCCC", Render("int x = 42; // note", "a.cs"));

    [Fact]
    public void Csharp_string_with_escaped_quote() =>
        Assert.Equal("CCCCCCC.KKK.....SSSSSS.", Render("line */ var s = \"a\\\"b\";", "a.cs", state: 1));

    [Fact]
    public void Block_comment_state_spans_lines()
    {
        var lang = Languages.ForPath("a.cs");
        var states = Highlighter.States(new[] { "x /* open", "still comment", "end */ y", "z" }, lang);
        Assert.Equal(new[] { 0, 1, 1, 0, 0 }, states);
    }

    [Fact]
    public void Markdown_two_formulas_side_by_side_open_no_block_and_a_bracket_block_opens_only_at_a_line_start()
    {
        // #128: "$a$$b$" read as an opening $$ made the rest of the file a formula
        var lang = Languages.ForPath("a.md");
        Assert.Equal(new[] { 0, 0, 0 }, Highlighter.States(new[] { "Two $a$$b$ and words.", "next" }, lang));
        Assert.Equal("....MMMMMM...........", Render("Two $a$$b$ and words.", "a.md"));
        var math = Array.FindIndex(lang!.Blocks, b => b.Start == @"\[") + 1;
        Assert.Equal(new[] { 0, math, math, 0, 0 }, Highlighter.States(new[] { @"\[", "x = 3", @"\]", "text", "" }, lang)[..5]);
        Assert.Equal(new[] { 0, 0, 0 }, Highlighter.States(new[] { @"see \[ for more", "text" }, lang));   // an escaped bracket
    }

    [Fact]
    public void Python_triple_quoted_string_spans_lines()
    {
        var lang = Languages.ForPath("a.py");
        var states = Highlighter.States(new[] { "x = \"\"\"doc", "more", "end\"\"\" + 1" }, lang);
        Assert.Equal(1, states[1]);
        Assert.Equal(1, states[2]);
        Assert.Equal(0, states[3]);
    }

    [Fact]
    public void Pascal_doubled_quote_is_one_string() =>
        Assert.Equal("KKKKK.......SSSSSSS.", Render("begin write('it''s')", "a.pas"));
    [Fact]
    public void Markdown_heading_inline_math_and_emphasis()
    {
        Assert.Equal("HHHHHHH", Render("# Title", "a.md"));
        var line = Render("text $a^2$ **bold**", "a.md");
        Assert.Contains('M', line);
        Assert.Contains('E', line);
    }

    [Fact]
    public void Markdown_dollar_prices_are_not_math() => Assert.DoesNotContain('M', Render("Cost is $5 and $10 total.", "a.md"));

    [Fact]
    public void Markdown_fence_state_covers_code_lines()
    {
        var lang = Languages.ForPath("a.md");
        var states = Highlighter.States(new[] { "text", "```cs", "int x;", "```", "after" }, lang);
        Assert.Equal(0, states[1]);
        Assert.NotEqual(0, states[2]);
        Assert.NotEqual(0, states[3]);
        Assert.Equal(0, states[4]);
    }

    [Fact]
    public void Latex_commands_math_and_escaped_percent()
    {
        var line = Render(@"Price 5\% and $x^2$ \section{A} % note", "a.tex");
        Assert.Contains('M', line);
        Assert.Contains('H', line);
        Assert.EndsWith("CCCCCC", line);                 // the real comment at the end
        Assert.Equal('K', line[7]);                      // '\%' is a command-like escape, not a comment start
        Assert.NotEqual('C', line[8]);
    }

    // ---------- PowerShell, Python, batch ----------
    [Fact]
    public void Powershell_cmdlets_variables_operators_and_types() =>
        Assert.Equal("HHHHHHHHHHHHH.EEEEEEEEE.KKK.KKKKKKKK", Render("Get-ChildItem $env:TEMP -eq [string]", "a.ps1"));

    [Fact]
    public void Powershell_keywords_ignore_case() => Assert.StartsWith("KKKKKKK", Render("ForEach ($x in $y)", "a.ps1"));

    [Fact]
    public void Python_builtins_and_decorators_but_not_methods_of_the_same_name()
    {
        Assert.Equal("HHHHH.HHH....", Render("print(len(x))", "a.py"));
        Assert.Equal("EEEEEEEEEE", Render("@dataclass", "a.py"));
        Assert.Equal(".........", Render("x.print()", "a.py"));
    }

    [Fact]
    public void Batch_variables_labels_echo_off_and_rem()
    {
        Assert.Equal("KKKKKKKKK", Render("@echo off", "a.bat"));
        Assert.Equal("KKK...EEEEEE.EEEEE", Render("set X=%PATH%;%~dp0", "a.cmd"));
        Assert.Equal("HHHHH", Render(":loop", "a.bat"));
        Assert.Equal("CCC", Render("rem", "a.bat"));
        Assert.DoesNotContain('E', Render("echo 50% done and 20% more", "a.bat"));   // not a %variable%
        Assert.Equal("EEE", Render("%%i", "a.bat"));
    }

    [Theory]
    [InlineData("a.psd1", "PowerShell")]
    [InlineData("a.pyw", "Python")]
    [InlineData("a.pyi", "Python")]
    public void More_extensions(string path, string name) => Assert.Equal(name, Languages.ForPath(path)!.Name);

    [Fact]
    public void Unknown_extension_gets_no_highlighting() => Assert.Null(Languages.ForPath("notes.xyz"));

    [Fact]
    public void Shell_keywords_variables_strings_and_comments()
    {
        Assert.Equal("KK.EEEE.KKKK", Render("if $dir then", "a.sh"));
        Assert.Equal(".....SSSSSSS..CCCCCC", Render("echo \"hi $x\"  # note", "a.sh"));
        Assert.Equal(".....EE.EEEEEEE", Render("echo $# ${#arr}", "a.sh"));                     // not comments
        Assert.Equal("...........EEEEEE", Render("cd \"$1\" && $(pwd)", "a.sh").Replace('S', '.'));
        Assert.Equal("HHHHHHHHH", Render("install()", "a.bash"));
        Assert.Equal("CCCCCCCCCCC", Render("#!/bin/bash", "a.sh"));
    }

    [Fact]
    public void Yaml_keys_comments_anchors_and_github_expressions()
    {
        Assert.Equal("KKKK..........", Render("name: check ci", "a.yml"));
        Assert.Equal("....KKKKKKK..SSSSSSSSSSS", Render("  - runs-on: \"ubuntu-24\"", "a.yaml"));
        Assert.Equal("KKK..KKKK..CCCCC", Render("url: true  # x y", "a.yml"));
        Assert.Equal("KKK..............", Render("url: http://a#b c", "a.yml"));      // # inside a value is not a comment
        Assert.Equal(".....EEEEEEEEEEEEEEEEEEE", Render("run: ${{ github.token }}", "a.yml").Replace('K', '.'));
        Assert.Equal("HHH", Render("---", "a.yml"));
    }

    [Fact]
    public void Makefile_targets_variables_and_directives()
    {
        Assert.Equal("HHHHHH....", Render("build: a.c", "Makefile"));
        Assert.Equal("........EEEEE.EE", Render("\tgcc -o $(CC) $@", "GNUmakefile"));            // a recipe line
        Assert.Equal("KKKKKKH............", Render(".PHONY: check clean", "x.mk"));
        Assert.Equal("KKKKK......", Render("ifdef DEBUG", "Makefile"));
        Assert.DoesNotContain('H', Render("CC := gcc", "Makefile"));                       // an assignment, not a target
    }

    [Fact]
    public void Dockerfile_instructions_and_variables()
    {
        Assert.Equal("KKKK...............KK......", Render("FROM mcr.io/sdk:10 AS build", "Dockerfile").Replace('N', '.'));
        Assert.Equal("KKK......EEEEE", Render("RUN echo $HOME", "Dockerfile.dev"));
        Assert.Equal("CCCCC", Render("# x y", "app.dockerfile"));
    }

    [Theory]
    [InlineData("#!/bin/bash", "Shell")]
    [InlineData("#!/bin/sh -e", "Shell")]
    [InlineData("#!/usr/bin/env bash", "Shell")]
    [InlineData("#!/usr/bin/env python3", "Python")]
    [InlineData("#!/usr/bin/python3.12", "Python")]
    [InlineData("#!/usr/bin/env -S pwsh -NoProfile", "PowerShell")]
    [InlineData("#!/usr/bin/env node", "JavaScript")]
    public void A_script_without_an_extension_is_known_by_its_shebang(string line, string name) => Assert.Equal(name, Languages.ForShebang(line)!.Name);

    [Theory]
    [InlineData("# heading")]
    [InlineData("#!/usr/bin/env unknown-thing")]
    [InlineData("")]
    public void Not_a_known_shebang(string line) => Assert.Null(Languages.ForShebang(line));

    [Theory]
    [InlineData("deploy.sh", "Shell")]
    [InlineData(".bashrc", "Shell")]
    [InlineData("check.yml", "YAML")]
    [InlineData("Makefile", "Makefile")]
    [InlineData("Dockerfile", "Dockerfile")]
    [InlineData("Cargo.toml", "INI")]
    public void New_file_types(string path, string name) => Assert.Equal(name, Languages.ForPath(path)!.Name);

    [Theory]
    [InlineData("bash", "Shell")]
    [InlineData("sh", "Shell")]
    [InlineData("yaml", "YAML")]
    [InlineData("dockerfile", "Dockerfile")]
    public void New_code_block_languages(string fence, string name) => Assert.Equal(name, Languages.ForFence(fence)!.Name);

    [Theory]
    [InlineData("a.cs", "C#")]
    [InlineData("A.PY", "Python")]
    [InlineData("a.md", "Markdown")]
    [InlineData("paper.tex", "LaTeX")]
    [InlineData("a.ps1", "PowerShell")]
    public void Language_is_chosen_by_extension_ignoring_case(string path, string name) => Assert.Equal(name, Languages.ForPath(path)!.Name);

    [Fact]
    public void Cache_returns_identical_results()
    {
        var lang = Languages.ForPath("a.cs");
        var a = Highlighter.ClassifyCached("int x = 1; // c", lang, 0, out var n1);
        var b = Highlighter.ClassifyCached("int x = 1; // c", lang, 0, out var n2);
        Assert.Same(a, b);
        Assert.Equal(n1, n2);
    }
}
