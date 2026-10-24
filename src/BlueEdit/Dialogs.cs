using Tui;

/// <summary>The editor's dialogs, built on the Tui widgets. Each call blocks until the dialog closes.</summary>
static class Dialogs
{
    public static string? Prompt(TuiApp app, string title, string label, string initial = "", bool replaceOnType = false) =>
        app.RunModal(new PromptDialog(title, label, initial, replaceOnType)).Result;

    public enum Choice { Save, Discard, Cancel }

    public static Choice ConfirmUnsaved(TuiApp app, string name) =>
        (Choice)app.RunModal(new ButtonDialog(Ui.T("Unsaved changes"), Ui.T("Save changes to {0}?", name), new[] { Ui.T("Save"), Ui.T("Discard"), Ui.T("Cancel") }, 2)).Result;

    /// <summary>A question with buttons; returns the chosen button, or <paramref name="cancel"/> on Esc.</summary>
    public static int Ask(TuiApp app, string title, string message, string[] buttons, int cancel) =>
        app.RunModal(new ButtonDialog(title, message, buttons, cancel)).Result;

    /// <summary>One of a list; <paramref name="buttons"/> (actions, not entries) give <c>items.Count + their index</c>.
    /// <paramref name="current"/>: the entry that is the setting now, marked "(now)": the highlight alone, where the
    /// list opens, did not say it was the one in use.</summary>
    public static int? Pick(TuiApp app, string title, IList<string> items, int selected = 0, IList<string>? buttons = null, int current = -1) =>
        app.RunModal(new ListDialog(title, items.Select((x, i) => i == current ? x + "   " + Ui.T("(now)") : x).ToList(), selected,
            buttons: buttons?.ToList())).Result;

    /// <summary>A finding: its fixes as buttons, the explanation, the other actions and who found it. Returns a fix's
    /// index, then an action's (after the fixes), or null on Esc.</summary>
    public static int? Fix(TuiApp app, string title, IList<string> fixes, string message, IList<string> actions, string info,
        string? none = null, bool table = false) =>
        app.RunModal(new FixDialog(title, fixes.ToList(), message, actions.ToList(), info, none, table)).Result;

    public static void ShowText(TuiApp app, string title, IList<string> lines) => app.RunModal(new TextDialog(title, lines.ToList()));

    public static string? OpenFile(TuiApp app, string? startDir) => app.RunModal(new FileDialog(Ui.T("Open"), startDir)).Path;

    /// <summary>The Save as dialog (folders, a name field, F7 for a new folder), then the questions it leaves: overwrite
    /// a file that exists, make a typed path's folder that does not. Null when cancelled.</summary>
    public static string? SaveFile(TuiApp app, string? current)
    {
        var dir = current != null ? Path.GetDirectoryName(current) : null;
        var name = current != null ? Path.GetFileName(current) : "untitled.txt";
        while (true)
        {
            var path = app.RunModal(new FileDialog(Ui.T("Save as"), dir, name)).Path;
            if (path == null) return null;
            dir = Path.GetDirectoryName(path);
            name = Path.GetFileName(path);
            if (dir != null && !Directory.Exists(dir))
            {
                if (Ask(app, Ui.T("Save as"), Ui.T("There is no folder {0}.\nMake it?", dir), new[] { Ui.T("Make it"), Ui.T("Cancel") }, 1) != 0) continue;
                try { Directory.CreateDirectory(dir); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    Ask(app, Ui.T("Save as"), Ui.T("Could not make the folder: ") + ex.Message, new[] { Ui.T("OK") }, 0);
                    continue;
                }
            }
            if (File.Exists(path) && !string.Equals(path, current, StringComparison.OrdinalIgnoreCase)
                && Ask(app, Ui.T("Save as"), Ui.T("{0} already exists.\nReplace it?", name), new[] { Ui.T("Replace"), Ui.T("Cancel") }, 1) != 0) continue;
            return path;
        }
    }
}
