using Xunit;

public sealed class ClipboardTests
{
    [Fact]
    public void Without_desktop_tools_a_copy_goes_to_the_terminal_as_osc_52()
    {
        var sent = new List<string>();
        var c = new UnixClipboard(sent.Add, desktopTools: false);
        c.SetText("Привет\r\nworld");
        Assert.Equal("\u001b]52;c;" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("Привет\nworld")) + "\u0007", Assert.Single(sent));
        Assert.Null(c.GetText());                       // terminals do not answer reads: paste uses the editor's own copy
        Assert.Equal("OSC 52", c.Kind);
    }

    [Fact]
    public void The_wayland_socket_is_found_where_the_variables_say()
    {
        Assert.Equal("/run/user/1000/wayland-0".Replace('/', Path.DirectorySeparatorChar),
            UnixClipboard.WaylandSocket("wayland-0", "/run/user/1000/".Replace('/', Path.DirectorySeparatorChar)));
        Assert.Null(UnixClipboard.WaylandSocket("wayland-0", null));
        Assert.Null(UnixClipboard.WaylandSocket(null, "/run/user/1000"));
        if (OperatingSystem.IsWindows()) return;
        Assert.Equal("/mnt/wslg/runtime-dir/wayland-0", UnixClipboard.WaylandSocket("/mnt/wslg/runtime-dir/wayland-0", "/run/user/1000"));
        // WSLg's socket, where there is one: a socket counts as an existing file
        if (Directory.Exists("/mnt/wslg/runtime-dir")) Assert.True(File.Exists("/mnt/wslg/runtime-dir/wayland-0"));
    }

    [Fact]
    public void Desktop_tools_copy_and_paste_round_trip_where_there_are_any()
    {
        if (OperatingSystem.IsWindows()) return;
        var c = new UnixClipboard(_ => Assert.Fail("a desktop tool was expected to take the copy"));
        if (c.Kind == "OSC 52") return;                 // no desktop here (a console, CI): nothing to check
        var text = "BlueEdit " + Guid.NewGuid().ToString("N") + "\nвторая строка";
        c.SetText(text);
        Assert.Equal(text, c.GetText());
    }
}
