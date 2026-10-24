using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

/// <summary>
/// tools/installer/blueedit.iss offers the models and code tools by the names be --install-proofing and
/// be --install-tools take: a name the editor no longer knows would fail at the end of an install, and one it learnt
/// would not be offered. Each has its task and its line in English and Russian.
/// </summary>
public sealed class InstallerTests
{
    static string Root([CallerFilePath] string file = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", ".."));
    static readonly string Script = File.ReadAllText(Path.Combine(Root(), "tools", "installer", "blueedit.iss"));

    static string[] Defined(string name) =>
        Regex.Match(Script, $@"^#define {name} ""([^""]*)""", RegexOptions.Multiline).Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void The_installer_offers_the_models_the_editor_installs() =>
        Assert.Equal(ProofingTools.Models.Select(m => m.Key), Defined("Models"));

    [Fact]
    public void The_installer_offers_the_code_tools_the_editor_installs() =>
        Assert.Equal(CodeTools.All.Select(t => t.Name), Defined("CodeTools"));

    [Fact]
    public void Each_code_tool_has_a_task_and_its_words()
    {
        foreach (var name in Defined("CodeTools"))
        {
            var task = name.Replace('-', '_');
            Assert.Matches($@"(?m)^Name: code\\{Regex.Escape(task)}; Description: \{{cm:{Regex.Escape(task)}\}}", Script);
            Assert.Matches($@"(?m)^en\.{Regex.Escape(task)}=\S", Script);
            Assert.Matches($@"(?m)^ru\.{Regex.Escape(task)}=\S", Script);
        }
    }

    // the grammar page lists the models with a name and a line under the list each, in both languages
    [Fact]
    public void Each_model_has_its_words_on_the_grammar_page()
    {
        foreach (var name in Defined("Models"))
            foreach (var message in new[] { name.Replace('-', '_'), name.Replace('-', '_') + "_note" })
            {
                Assert.Matches($@"(?m)^en\.{Regex.Escape(message)}=\S", Script);
                Assert.Matches($@"(?m)^ru\.{Regex.Escape(message)}=\S", Script);
            }
    }

    // the page's memory figures are the code's: what a note says a model wants is what be --video-card checks
    [Fact]
    public void The_notes_say_the_memory_the_editor_checks()
    {
        foreach (var m in ProofingTools.Models)
            Assert.Matches($@"(?m)^en\.{Regex.Escape(m.Key.Replace('-', '_'))}_note=.*~{m.MemoryGb} GB", Script);
    }

    [Fact]
    public void Every_message_is_in_both_languages()
    {
        var en = Regex.Matches(Script, @"(?m)^en\.(\w+)=").Select(m => m.Groups[1].Value).ToList();
        var ru = Regex.Matches(Script, @"(?m)^ru\.(\w+)=").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(en, ru);
    }
}
