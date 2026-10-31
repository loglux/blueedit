using Xunit;

public class JumpHistoryTests
{
    static Place At(int row, string? path = "a.md") => new(path, row, 0);

    [Fact]
    public void Back_returns_to_where_the_jump_left_and_forward_undoes_it()
    {
        var h = new JumpHistory();
        h.Record(At(200), At(15));
        Assert.Equal(At(200), h.Back(At(15)));
        Assert.Equal(At(15), h.Forward(At(200)));
        Assert.Equal(At(200), h.Back(At(15)));
    }

    [Fact]
    public void Back_goes_through_several_jumps_in_turn_also_across_files()
    {
        var h = new JumpHistory();
        h.Record(At(1), At(50));
        h.Record(At(50), At(7, "b.md"));
        Assert.Equal(At(50), h.Back(At(7, "b.md")));
        Assert.Equal(At(1), h.Back(At(50)));
        Assert.Null(h.Back(At(1)));
    }

    [Fact]
    public void There_is_nothing_at_first()
    {
        var h = new JumpHistory();
        Assert.Null(h.Back(At(1)));
        Assert.Null(h.Forward(At(1)));
    }

    [Fact]
    public void A_jump_that_went_nowhere_is_not_recorded()
    {
        var h = new JumpHistory();
        h.Record(At(5), At(5));
        Assert.Null(h.Back(At(5)));
    }

    [Fact]
    public void A_new_jump_after_going_back_drops_the_forward_places()
    {
        var h = new JumpHistory();
        h.Record(At(1), At(50));
        h.Back(At(50));
        h.Record(At(1), At(9));
        Assert.Null(h.Forward(At(9)));
    }

    [Fact]
    public void A_run_of_one_kind_counts_as_one_jump()
    {
        var h = new JumpHistory();
        h.Record(At(10), At(100), "search");
        h.Record(At(100), At(200), "search");
        h.Record(At(200), At(300), "search");
        Assert.Equal(At(10), h.Back(At(300)));
        Assert.Null(h.Back(At(10)));
    }

    [Fact]
    public void Moving_away_between_two_searches_ends_the_run()
    {
        var h = new JumpHistory();
        h.Record(At(10), At(100), "search");
        h.Record(At(105), At(200), "search");                    // the caret moved, or text was typed, at the first match
        Assert.Equal(At(105), h.Back(At(200)));
        Assert.Equal(At(10), h.Back(At(105)));
    }

    [Fact]
    public void Another_kind_of_jump_ends_the_run_and_jumps_without_a_kind_never_merge()
    {
        var h = new JumpHistory();
        h.Record(At(10), At(100), "search");
        h.Record(At(100), At(200), "problem");
        Assert.Equal(At(100), h.Back(At(200)));

        var g = new JumpHistory();
        g.Record(At(1), At(2));
        g.Record(At(2), At(3));
        Assert.Equal(At(2), g.Back(At(3)));
    }

    [Fact]
    public void Going_back_ends_a_run()
    {
        var h = new JumpHistory();
        h.Record(At(10), At(100), "search");
        h.Back(At(100));
        h.Record(At(100), At(200), "search");
        Assert.Equal(At(100), h.Back(At(200)));
    }

    [Fact]
    public void A_place_equal_to_where_one_is_is_stepped_over()
    {
        var h = new JumpHistory();
        h.Record(At(1), At(50));
        Assert.Null(h.Back(At(1)));                              // the caret walked back to line 1 by itself
    }

    [Fact]
    public void Only_the_latest_hundred_places_are_kept()
    {
        var h = new JumpHistory();
        for (int i = 0; i < 150; i++) h.Record(At(i), At(i + 1));
        int steps = 0;
        var here = At(150);
        while (h.Back(here) is { } p) { here = p; steps++; }
        Assert.Equal(100, steps);
        Assert.Equal(At(50), here);
    }
}
