using Tui;
using Xunit;

/// <summary>File > Info's counts (#100).</summary>
public sealed class InfoTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "be-info-" + Guid.NewGuid().ToString("N"));
    public InfoTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    EditorView Open(string text, string name)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, text);
        var e = new EditorView();
        e.Load(p);
        return e;
    }

    [Fact]
    public void Words_are_runs_with_a_letter_or_a_digit()
    {
        Assert.Equal(6, EditorView.CountWords("It's 2026 — a well-known test, yes."));
        Assert.Equal(0, EditorView.CountWords("— * - ..."));
    }

    [Fact]
    public void Plain_text_counts_lines_paragraphs_words_and_characters()
    {
        var t = Open("one two three\nfour\n\nfive six\n", "a.txt").Stats();
        Assert.Equal(4, t.Lines);                       // the final line break does not make a fifth
        Assert.Equal(2, t.Paragraphs);
        Assert.Equal(6, t.Words);
        Assert.Equal(6, t.ProseWords);
        Assert.Equal("one two three\nfour\n\nfive six".Length, t.Chars);
        Assert.Equal("onetwothreefourfivesix".Length, t.CharsNoSpaces);
    }

    [Fact]
    public void Markdown_counts_the_words_of_the_text_without_markup_formulas_or_code()
    {
        var md = "# Title here\n\nThe **value** of $x + 1$ is [shown](https://example.com) below.\n\n```python\nprint('a b c')\nx = 1\n```\n\n$$\ny = 2x\n$$\n";
        var t = Open(md, "a.md").Stats();
        // the words of the text: Title here / The value of is shown below
        Assert.Equal(8, t.ProseWords);
        Assert.Equal(2, t.Formulas);                    // $x + 1$ and the display formula, once
        Assert.Equal(2, t.CodeLines);
        Assert.True(t.Words > t.ProseWords);            // all runs count markup, code and formulas too
    }

    [Fact]
    public void A_selection_is_counted_on_its_own()
    {
        var e = Open("one two\nthree four five\nsix\n", "a.txt");
        e.SetArea(new Rect(0, 0, 80, 10));
        e.HandleKey(new KeyInput(Key.Down, '\0', Mods.Shift));
        e.HandleKey(new KeyInput(Key.Down, '\0', Mods.Shift));
        var t = e.Stats(selection: true);
        Assert.Equal(5, t.Words);                       // the first two lines (the third line's start: nothing of it)
        Assert.Equal(6, e.Stats().Words);
    }
}
