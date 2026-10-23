using Xunit;

/// <summary>A test of something only Windows has (its spell checker, Office, Edge): skipped elsewhere.</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Windows only";
    }
}
