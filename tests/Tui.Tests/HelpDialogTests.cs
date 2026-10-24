using Tui;
using Xunit;

public class HelpDialogTests
{
    static readonly KeyInput Esc = new(Key.Escape), Down = new(Key.Down), PgDn = new(Key.PageDown);

    static readonly HelpSection[] Sections =
    [
        new("Files", [new("F2, Ctrl+S", "Save"), new("Shift+F2", "Save as..."), new("", ""),
            new("", "In Open and Save as: F7 makes a new folder, Alt+F1 lists the drives, Backspace goes up a folder.")]),
        new("Search", [new("Ctrl+F", "Find"), .. Enumerable.Range(1, 60).Select(i => new HelpRow("", $"line {i}"))]),
    ];

    [Fact]
    public void A_section_has_its_keys_in_a_column_and_its_text_wrapped_beside_them()
    {
        var lines = HelpDialog.Layout(Sections[0], 40);
        Assert.Equal((0, "Files", 2), lines[0].Single());
        Assert.Equal([(0, "F2, Ctrl+S", 1), (12, "Save", 0)], lines[2]);
        Assert.Equal([(0, "Shift+F2", 1), (12, "Save as...", 0)], lines[3]);
        Assert.Empty(lines[4]);
        Assert.All(lines.Skip(5), l => Assert.True(l.Single().Text.Length <= 40));
        Assert.Equal("In Open and Save as: F7 makes a new", lines[5].Single().Text);
    }

    [Fact]
    public void A_key_too_long_for_the_column_has_its_text_on_the_next_line()
    {
        var lines = HelpDialog.Layout(new HelpSection("S", [new("F1", "Help"), new(new string('k', 30), "what it does")]), 60);
        Assert.Equal((0, new string('k', 30), 1), lines[3].Single());
        Assert.Equal((4, "what it does", 0), lines[4].Single());
    }

    [Fact]
    public void Up_and_down_choose_the_section_page_down_scrolls_it_esc_closes()
    {
        var term = new FakeTerminal().Keys(Down, PgDn, Esc);
        var d = new TuiApp(term).RunModal(new HelpDialog("Help", Sections));
        Assert.Equal(1, d.Section);
        Assert.True(d.Scroll > 0);
        Assert.Contains("Search", term.All(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_window_shows_the_contents_and_the_chosen_section()
    {
        var term = new FakeTerminal().Keys(Esc);
        new TuiApp(term).RunModal(new HelpDialog("Help", Sections, 0));
        var rows = term.All().Split('\n');
        Assert.Contains(rows, r => r.Contains(" Files ", StringComparison.Ordinal) && r.Contains("│ Files", StringComparison.Ordinal));
        Assert.Contains(rows, r => r.Contains("┃ Search  │", StringComparison.Ordinal));
        Assert.Contains(rows, r => r.Contains("│ F2, Ctrl+S  Save", StringComparison.Ordinal));
    }
}
