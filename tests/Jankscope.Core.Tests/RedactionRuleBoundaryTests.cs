using Jankscope.Core.Reports;

namespace Jankscope.Core.Tests;

/// <summary>
/// The exact edges of the redaction rules, pinned with literal lengths (independent Stage 1b review, finding 17: mutants of these rules survived). A test that
/// builds its data from <c>Redactor.SubstringMinLength</c> moves together with the constant and cannot notice the constant changing, so these do not.
/// </summary>
public class RedactionRuleBoundaryTests
{
    private static string Redact(string input, KnownValueMode mode, params (RedactionCategory Category, string Value)[] known)
    {
        var context = RedactionContext.Empty;
        foreach (var (category, value) in known)
        {
            context = context.With(category, value);
        }

        return new Redactor(context).Redact(input, mode)!;
    }

    [Theory]
    [InlineData(KnownValueMode.Name, "abcde", "xabcdex", false)]    // a name of 5 characters inside other characters: whole words only
    [InlineData(KnownValueMode.Name, "abcdef", "xabcdefx", true)]   // 6 characters: found inside other characters
    [InlineData(KnownValueMode.Value, "abc", "xabcx", false)]       // a value of 3
    [InlineData(KnownValueMode.Value, "abcd", "xabcdx", true)]      // 4
    [InlineData(KnownValueMode.FreeText, "a", "xax", false)]        // free text, 1 character
    [InlineData(KnownValueMode.FreeText, "ab", "xabx", true)]       // 2
    public void A_known_value_is_found_inside_other_characters_only_from_the_length_its_mode_sets(KnownValueMode mode, string known, string input, bool found)
    {
        var result = Redact(input, mode, (RedactionCategory.UserName, known));

        Assert.Equal(found, !result.Contains(known, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(KnownValueMode.Name, "abcde")]
    [InlineData(KnownValueMode.Value, "abc")]
    [InlineData(KnownValueMode.FreeText, "a")]
    public void A_short_known_value_standing_alone_is_always_redacted(KnownValueMode mode, string known)
    {
        var result = Redact($"logged in as {known} today", mode, (RedactionCategory.UserName, known));

        // As a standalone word: the letter itself still appears inside other words ("logged", "today").
        Assert.DoesNotMatch($@"(?<![\p{{L}}\p{{N}}]){known}(?![\p{{L}}\p{{N}}])", result);
    }

    [Fact]
    public void A_host_name_that_contains_the_user_name_is_replaced_whole_because_the_longer_value_goes_first()
    {
        // The user name is added first on purpose: without longest-first ordering it would clip "alice" out of the host name and leave "workstation" behind.
        var result = Redact("alice is on alice-workstation", KnownValueMode.Value,
            (RedactionCategory.UserName, "alice"), (RedactionCategory.HostName, "alice-workstation"));

        Assert.DoesNotContain("workstation", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alice", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_profile_folder_name_counts_as_a_user_name_even_when_it_differs_from_the_account_name()
    {
        var context = RedactionContext.ForMachine("Quinn Marsh", "QUINN-PC", @"C:\Users\qm_home", "CORP");

        var users = context.Values.Where(v => v.Category == RedactionCategory.UserName).Select(v => v.Value).ToArray();
        var hosts = context.Values.Where(v => v.Category == RedactionCategory.HostName).Select(v => v.Value).ToArray();

        Assert.Equal(["Quinn Marsh", "qm_home"], users);
        Assert.Equal(["QUINN-PC", "CORP"], hosts);
    }

    [Fact]
    public void A_domain_that_only_repeats_the_host_name_is_not_added_twice_and_blank_values_are_ignored()
    {
        var context = RedactionContext.ForMachine("quinn", "QUINN-PC", "  ", "quinn-pc");

        Assert.Equal(["quinn", "QUINN-PC"], context.Values.Select(v => v.Value).ToArray());
    }

    [Fact]
    public void The_real_machine_context_is_built_from_the_environment()
    {
        var context = RedactionContext.ForThisMachine();

        Assert.Contains(context.Values, v => v.Category == RedactionCategory.HostName && v.Value.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase));
    }
}
