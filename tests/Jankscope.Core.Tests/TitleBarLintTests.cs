using System.Text.RegularExpressions;

namespace Jankscope.Core.Tests;

/// <summary>
/// Every window of the app must follow the colour theme in its title bar (GUI-run report, defect 9). The opt-in is one line in each window's
/// constructor, so a new window that forgets it would keep a white title bar on the dark theme without any test noticing: this rule notices.
/// </summary>
public class TitleBarLintTests
{
    private static readonly Regex WindowClass = new(@"\bclass\s+\w+\s*:\s*Window\b", RegexOptions.CultureInvariant);

    /// <summary>The app source files that declare a window class but never call <c>TitleBarTheme.Follow(this)</c>.</summary>
    private static IReadOnlyList<string> WindowsThatDoNotFollow(IEnumerable<(string Rel, string Text)> sources) =>
        sources
            .Select(s => (s.Rel, Code: CSharpScanner.Scan(s.Text).Code))
            .Where(s => WindowClass.IsMatch(s.Code) && !Regex.IsMatch(s.Code, @"\bTitleBarTheme\s*\.\s*Follow\s*\(\s*this\s*\)"))
            .Select(s => s.Rel)
            .ToList();

    [Fact]
    public void Every_window_in_the_app_asks_for_a_title_bar_that_follows_the_theme()
    {
        var sources = Repo.Files("src/Jankscope.App", "*.cs").Select(f => (Repo.Rel(f), File.ReadAllText(f))).ToList();

        var windows = sources.Count(s => WindowClass.IsMatch(CSharpScanner.Scan(s.Item2).Code));
        Assert.True(windows >= 3, $"only {windows} window classes were found; the rule would pass vacuously");
        Assert.Empty(WindowsThatDoNotFollow(sources));
    }

    [Fact]
    public void The_rule_can_fail_a_window_that_forgets_and_ignores_a_mention_in_a_comment_or_string()
    {
        const string Forgets = "public partial class NewWindow : Window { public NewWindow() { InitializeComponent(); } }";
        const string OnlyInAComment = "public partial class NewWindow : Window { public NewWindow() { InitializeComponent(); /* TitleBarTheme.Follow(this); */ } }";
        const string OnlyInAString = "public partial class NewWindow : Window { string s = \"TitleBarTheme.Follow(this)\"; public NewWindow() { } }";
        const string Follows = "public partial class NewWindow : Window { public NewWindow() { InitializeComponent(); TitleBarTheme.Follow(this); } }";
        const string NotAWindow = "public class Helper { }";

        Assert.Equal(["a.cs", "b.cs", "c.cs"], WindowsThatDoNotFollow([("a.cs", Forgets), ("b.cs", OnlyInAComment), ("c.cs", OnlyInAString), ("d.cs", Follows), ("e.cs", NotAWindow)]));
    }
}
