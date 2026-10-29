namespace TimeSink.Core.Theming;

public enum ShellTab { Today, Week, Month, Year, All }

public static class ShellTabs
{
    public static ShellTab Parse(string? stored)
        => Enum.TryParse<ShellTab>(stored, ignoreCase: false, out var tab) && Enum.IsDefined(tab) ? tab : ShellTab.Today;
}
