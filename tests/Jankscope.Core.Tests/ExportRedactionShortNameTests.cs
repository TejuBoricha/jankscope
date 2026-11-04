using System.Text.Json;
using Jankscope.Core.Reports;

namespace Jankscope.Core.Tests;

/// <summary>
/// Independent review of Stage 1b, finding 2: a person's own short user or PC name, glued to other characters in the text they typed, used to pass the
/// redactor and the guard (the owner's own names are five characters). These tests use stand-in names of the same length. They also pin the rules that keep
/// this tool's own text and schema readable.
/// </summary>
public class ExportRedactionShortNameTests
{
    private const string User = "tomas";
    private const string Host = "ZEBRA";

    private static RedactionContext Context() => RedactionContext.Empty
        .With(RedactionCategory.UserName, User)
        .With(RedactionCategory.HostName, Host);

    private static string Note(string input, RedactionContext? context = null)
    {
        var preview = ExportPipeline.Prepare(new { Note = input }, context ?? Context());
        using var doc = JsonDocument.Parse(preview.Text);
        return doc.RootElement.GetProperty("note").GetString()!;
    }

    [Theory]
    [InlineData("tomas225")]
    [InlineData("TomasPC")]
    [InlineData("mytomas")]
    [InlineData("tomassmith")]
    [InlineData("TOMAS1")]
    [InlineData("tomas0")]
    [InlineData("0tomas")]
    [InlineData("tomasé")]
    [InlineData("ZEBRAbox")]
    [InlineData("myzebra2")]
    public void A_five_character_own_name_glued_to_other_characters_is_removed_from_the_note(string input)
    {
        var result = Note(input);
        Assert.DoesNotContain(User, result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Host, result, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.Contains("<USER>", StringComparison.Ordinal) || result.Contains("<HOST>", StringComparison.Ordinal), "a placeholder was written: " + result);
    }

    [Fact]
    public void A_two_and_three_character_name_is_removed_from_the_note_even_when_glued_but_not_from_the_tools_own_prose()
    {
        var context = RedactionContext.Empty.With(RedactionCategory.UserName, "ian");

        Assert.Equal("<USER> was here", Note("ian was here", context));
        Assert.Equal("med<USER>", Note("median", context)); // the person's own text is judged strictly: a glued name must not survive

        var preview = ExportPipeline.Prepare(new { Summary = "5 ms above the median", Note = "x" }, context);
        using var doc = JsonDocument.Parse(preview.Text);
        Assert.Equal("5 ms above the median", doc.RootElement.GetProperty("summary").GetString()); // this tool's own prose is not corrupted
    }

    [Fact]
    public void A_name_of_four_or_more_characters_is_removed_from_this_tools_own_values_too()
    {
        var preview = ExportPipeline.Prepare(new { Summary = "device mytomas1 and tomas225" }, Context());
        using var doc = JsonDocument.Parse(preview.Text);
        var summary = doc.RootElement.GetProperty("summary").GetString()!;
        Assert.DoesNotContain(User, summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Property_names_keep_the_lenient_rule_so_the_tools_own_schema_is_not_renamed_by_a_four_letter_name()
    {
        var context = RedactionContext.Empty.With(RedactionCategory.UserName, "kind");
        var preview = ExportPipeline.Prepare(new { Kind = "stutter", KindNote = "the person pressed the key" }, context);
        using var doc = JsonDocument.Parse(preview.Text);
        Assert.True(doc.RootElement.TryGetProperty("kindNote", out _)); // a substring of the key, a four-letter name: the key is untouched
        Assert.Equal("stutter", doc.RootElement.GetProperty("<USER>").GetString()); // a key that IS the whole word is still redacted (documented limitation 13)
    }

    [Theory]
    [InlineData("李雷的电脑")]
    [InlineData("x李雷y")]
    [InlineData("hi 李雷")]
    public void A_name_in_a_script_without_word_boundaries_is_matched_as_a_substring(string input)
    {
        var context = RedactionContext.Empty.With(RedactionCategory.UserName, "李雷");
        Assert.DoesNotContain("李雷", Note(input, context));

        var summary = ExportPipeline.Prepare(new { Summary = input }, context).Text;
        Assert.DoesNotContain("李雷", summary);
    }

    [Fact]
    public void A_name_is_matched_whichever_way_its_accented_letters_are_written()
    {
        var composed = "José";
        var decomposed = "José";

        Assert.Equal("hi <USER> there", Note($"hi {decomposed} there", RedactionContext.Empty.With(RedactionCategory.UserName, composed)));
        Assert.Equal("hi <USER> there", Note($"hi {composed} there", RedactionContext.Empty.With(RedactionCategory.UserName, decomposed)));
        Assert.Throws<ExportBlockedException>(() => ExportGuard.Verify("{\"note\":\"hi Jose\\u0301 there\"}", RedactionContext.Empty.With(RedactionCategory.UserName, composed)));
        Assert.Single(RedactionContext.Empty.With(RedactionCategory.UserName, composed).With(RedactionCategory.UserName, decomposed).Values);
    }

    [Theory]
    [InlineData("ip=192.0.2.5_x")]
    [InlineData("x192.0.2.5")]
    [InlineData("host192.0.2.5end")]
    [InlineData("(192.0.2.5)")]
    [InlineData("192.0.2.5:8080")]
    public void An_ipv4_address_is_found_when_letters_digits_or_underscores_touch_it(string input)
    {
        var result = new Redactor(RedactionContext.Empty).Redact(input)!;
        Assert.Contains("<IP>", result);
        Assert.DoesNotContain("192.0.2.5", result);
    }

    [Theory]
    [InlineData("driver 31.0.101.5186 on build 10.0.26300.9818")]
    [InlineData("version 6.0.1234.5678")]
    [InlineData("12.34.56")]
    public void Version_numbers_are_not_mistaken_for_ipv4_addresses(string input)
    {
        Assert.Equal(input, new Redactor(RedactionContext.Empty).Redact(input));
    }

    [Theory]
    [InlineData("A1B2C3D4E5F6")]
    [InlineData("a1b2.c3d4.e5f6")]
    [InlineData("mac=A1-B2-C3-D4-E5-F6_x")]
    [InlineData("xa1:b2:c3:d4:e5:f6")]
    public void A_mac_address_is_found_in_the_common_notations_even_when_other_characters_touch_it(string input)
    {
        var result = new Redactor(RedactionContext.Empty).Redact(input)!;
        Assert.Contains("<MAC>", result);
    }

    [Theory]
    [InlineData("order 123456789012")]                       // twelve digits and no letter: a number, not an address
    [InlineData("id 00000000-0000-0000-0000-0000000000ab")]  // the tail of a GUID
    [InlineData("hash 0123456789abcdef0123")]                // a longer hex run
    [InlineData("frame 123456789012345678")]
    public void Numbers_guids_and_longer_hex_runs_are_not_mistaken_for_mac_addresses(string input)
    {
        Assert.Equal(input, new Redactor(RedactionContext.Empty).Redact(input));
    }

    [Fact]
    public void The_guard_judges_a_note_strictly_and_other_text_by_the_value_rule()
    {
        var context = Context();

        Assert.Throws<ExportBlockedException>(() => ExportGuard.Verify("{\"note\":\"went with tomas225\"}", context));
        Assert.Throws<ExportBlockedException>(() => ExportGuard.Verify("{\"targetProcess\":\"tomasgame.exe\"}", context));
        Assert.Throws<ExportBlockedException>(() => ExportGuard.Verify("{\"deep\":{\"note\":[\"x\",\"ZEBRAbox\"]}}", context));

        // under a free-text property the name is caught, under another property a 5 character name is also caught (value rule), a 3 character one is not
        var short3 = RedactionContext.Empty.With(RedactionCategory.UserName, "ian");
        ExportGuard.Verify("{\"summary\":\"the median\"}", short3);
        Assert.Throws<ExportBlockedException>(() => ExportGuard.Verify("{\"note\":\"the median\"}", short3));
    }

    [Fact]
    public void The_guard_still_passes_text_that_only_carries_our_own_tokens_and_ordinary_words_with_short_names()
    {
        var context = RedactionContext.Empty.With(RedactionCategory.UserName, "ian").With(RedactionCategory.HostName, "al");
        ExportGuard.Verify("{\"summary\":\"median of metal <USER> <HOST>\",\"note\":\"<USER> was on <HOST>\"}", context);
    }

    [Fact]
    public void A_full_export_with_a_glued_name_in_the_note_never_reaches_the_file()
    {
        var preview = ExportPipeline.Prepare(new { Note = "felt it in tomasgame.exe on ZEBRAbox", TargetProcess = "tomasgame.exe" }, Context());
        Assert.DoesNotContain(User, preview.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Host, preview.Text, StringComparison.OrdinalIgnoreCase);
        Assert.True(preview.Counts[RedactionCategory.UserName] >= 2);
    }
}
