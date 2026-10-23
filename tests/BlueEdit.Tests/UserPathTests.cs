using Xunit;

/// <summary>Tools > Command be in any console (UserPath.cs): the user's PATH value edited as text, the registry left
/// to the sandbox run.</summary>
public class UserPathTests
{
    [Fact]
    public void The_folder_goes_at_the_end_and_the_other_entries_stay_as_written()
    {
        Assert.Equal(@"%USERPROFILE%\bin;C:\Tools;C:\be", UserPath.Edit(@"%USERPROFILE%\bin;C:\Tools;", @"C:\be", true));
        Assert.Equal(@"C:\be", UserPath.Edit("", @"C:\be", true));
    }

    [Fact]
    public void A_folder_already_there_is_kept_once_however_it_is_written()
    {
        Assert.Equal(@"C:\Tools;C:\be", UserPath.Edit(@"C:\BE\;C:\Tools;""C:\be""", @"C:\be", true));
        Assert.Equal(@"C:\Tools", UserPath.Edit(@"C:\be;C:\Tools;c:\Be\", @"C:\be", false));
    }

    [Fact]
    public void A_variable_in_an_entry_is_compared_expanded()
    {
        if (!OperatingSystem.IsWindows()) return;      // %USERPROFILE% is a Windows variable
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.True(UserPath.Same(@"%USERPROFILE%\bin", Path.Combine(home, "bin")));
        Assert.False(UserPath.Same(@"C:\be\sub", @"C:\be"));
    }
}
