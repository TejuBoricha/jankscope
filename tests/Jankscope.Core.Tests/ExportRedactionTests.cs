using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jankscope.Core.Reports;

namespace Jankscope.Core.Tests;

/// <summary>
/// Redaction v2 and the export pipeline (spec section 3.6, 7.3, 16): every category is planted as a canary in every kind of place a string can
/// sit, and none may survive; the guard must fail closed, and what the preview shows must be exactly what is written.
/// </summary>
public class ExportRedactionTests
{
    // Planted canaries. Distinct, unlikely strings so that a surviving copy cannot be a coincidence.
    private const string User = "Zanzibar_Quinn";
    private const string Host = "QUINN-RIG-77";
    private const string Ssid = "Wifi of Quinn's Flat";
    private const string Bssid = "AA:BB:CC:11:22:33";
    private const string Mac = "00:1A:2B:3C:4D:5E";
    private const string Ip4 = "192.168.77.12";
    private const string Ip6 = "fe80::1c2d:3e4f:5a6b:7c8d";
    private const string Serial = "SN-9X77-QWERTY-4421";

    private static RedactionContext Context() => RedactionContext.Empty
        .With(RedactionCategory.UserName, User)
        .With(RedactionCategory.HostName, Host)
        .With(RedactionCategory.WifiSsid, Ssid)
        .With(RedactionCategory.Bssid, Bssid)
        .With(RedactionCategory.DeviceSerial, Serial);

    private sealed record Row(string Name, string Value);

    private sealed record Bundle(string Title, string? Note, string CommandLine, IReadOnlyList<Row> Rows, Dictionary<string, string> Extra);

    private static Bundle Planted() => new(
        Title: $"Incident on {Host} for {User}",
        Note: $"it happened on {Ssid} ({Bssid}) while {User} was away",
        CommandLine: $"\"C:\\Users\\{User}\\Games\\game.exe\" --token=hunter2 --server {Ip4}",
        Rows:
        [
            new Row("path", $"C:\\Users\\{User}\\AppData\\Local\\Temp\\x.log"),
            new Row("unc", $"\\\\{Host}\\share\\{User}"),
            new Row("net", $"adapter {Mac} at {Ip4} and {Ip6}"),
            new Row("serial", $"disk serial {Serial}"),
            new Row("mail", $"report to {User}@example.org"),
            new Row("onedrive", $"C:\\Users\\{User}\\OneDrive - Contoso Ltd\\save.dat"),
        ],
        Extra: new Dictionary<string, string> { [$"key for {User}"] = $"value {Host}" });

    private static string AllText(ExportPreview preview) => preview.Text;

    [Fact]
    public void Every_planted_canary_is_gone_from_every_kind_of_place()
    {
        var preview = ExportPipeline.Prepare(Planted(), Context());
        var text = AllText(preview);

        foreach (var canary in new[] { User, Host, Ssid, Bssid, Mac, Ip4, Ip6, Serial, "hunter2", "Contoso" })
        {
            Assert.DoesNotContain(canary, text, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain("example.org", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<USER>", text);
        Assert.Contains("<HOST>", text);
        Assert.Contains("<SSID>", text);
        Assert.Contains("<SERIAL>", text);
        Assert.Contains("<MAC>", text);
        Assert.Contains("<IP>", text);
        Assert.Contains("<EMAIL>", text);
    }

    [Fact]
    public void Property_names_are_redacted_too()
    {
        var text = ExportPipeline.Prepare(Planted(), Context()).Text;
        Assert.DoesNotContain(User, text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("key for <USER>", text);
    }

    [Fact]
    public void A_command_line_keeps_only_the_program_name()
    {
        var node = JsonNode.Parse(ExportPipeline.Prepare(Planted(), Context()).Text)!;
        var commandLine = node["commandLine"]!.GetValue<string>();
        Assert.StartsWith("game.exe", commandLine);
        Assert.DoesNotContain("--token", commandLine);
        Assert.DoesNotContain("--server", commandLine);
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\Game\\game.exe\" -user bob", "game.exe")]
    [InlineData("C:\\Games\\game.exe -x", "game.exe")]
    [InlineData("game.exe", "game.exe")]
    [InlineData("\"D:\\a b\\c d\\Launcher.exe\"", "Launcher.exe")]
    public void Command_line_redaction_cuts_directory_and_arguments(string commandLine, string expectedProgram)
    {
        var redactor = new Redactor(RedactionContext.Empty);
        var result = redactor.RedactCommandLine(commandLine);
        Assert.StartsWith(expectedProgram, result);
        Assert.DoesNotContain("bob", result);
        Assert.DoesNotContain("Program Files", result);
    }

    [Fact]
    public void Command_line_with_arguments_says_they_were_removed()
    {
        var redactor = new Redactor(RedactionContext.Empty);
        Assert.Contains("<ARGS>", redactor.RedactCommandLine("game.exe --secret"));
        Assert.DoesNotContain("<ARGS>", redactor.RedactCommandLine("game.exe"));
        Assert.Equal(1, redactor.Counts[RedactionCategory.CommandLine]);
    }

    [Theory]
    [InlineData("C:\\Users\\Someone Else\\Desktop", "C:\\Users\\<USER>\\Desktop")]
    [InlineData("c:/users/someone/desktop", "c:/users/<USER>/desktop")]
    [InlineData("\\\\?\\C:\\Users\\bob\\x", "\\\\?\\C:\\Users\\<USER>\\x")]
    public void Any_profile_path_is_cut_even_for_an_account_we_did_not_know(string input, string expected)
    {
        Assert.Equal(expected, new Redactor(RedactionContext.Empty).Redact(input));
    }

    [Fact]
    public void An_unknown_unc_host_is_cut_but_the_long_path_prefix_is_not_mistaken_for_one()
    {
        var redactor = new Redactor(RedactionContext.Empty);
        Assert.Equal("\\\\<HOST>\\share\\file", redactor.Redact("\\\\some-box\\share\\file"));
        Assert.Equal("\\\\?\\C:\\Windows", redactor.Redact("\\\\?\\C:\\Windows"));
    }

    [Theory]
    [InlineData("2026-11-04T01:23:40.123Z")]
    [InlineData("elapsed 00:01:02")]
    [InlineData("driver 31.0.101.5186 on build 10.0.26300")]
    [InlineData("Driver-style version 546.33")]
    [InlineData("frame 12:34 ratio 1:2")]
    public void Ordinary_numbers_times_and_versions_are_left_alone(string text)
    {
        Assert.Equal(text, new Redactor(RedactionContext.Empty).Redact(text));
    }

    [Theory]
    [InlineData("fe80::1", "<IP>")]
    [InlineData("2001:db8::ff00:42:8329", "<IP>")]
    [InlineData("2001:0db8:0000:0000:0000:ff00:0042:8329", "<IP>")]
    [InlineData("::1", "<IP>")]
    [InlineData("AA-BB-CC-DD-EE-FF", "<MAC>")]
    [InlineData("aa:bb:cc:dd:ee:ff", "<MAC>")]
    [InlineData("10.20.30.40", "<IP>")]
    public void Network_identifiers_are_replaced_in_every_common_notation(string text, string token)
    {
        Assert.Equal(token, new Redactor(RedactionContext.Empty).Redact(text));
    }

    [Fact]
    public void A_short_user_name_is_matched_as_a_whole_word_only()
    {
        var context = RedactionContext.Empty.With(RedactionCategory.UserName, "al");
        var redactor = new Redactor(context);
        Assert.Equal("hello <USER> and algorithm", redactor.Redact("hello al and algorithm"));
        // The guard uses the same rule, so a short name does not make every export fail on the word "metal".
        ExportGuard.Verify("metal <USER> algorithm", context);
        Assert.Throws<ExportBlockedException>(() => ExportGuard.Verify("hello al there", context));
    }

    [Fact]
    public void A_user_name_that_is_a_common_word_does_not_trip_the_guard_on_its_own_token()
    {
        var context = RedactionContext.Empty.With(RedactionCategory.UserName, "user").With(RedactionCategory.HostName, "host");
        var preview = ExportPipeline.Prepare(new Row("n", "a user on a host"), context);
        Assert.Contains("<USER>", preview.Text);
        Assert.Contains("<HOST>", preview.Text);
    }

    [Fact]
    public void Redaction_counts_are_reported_per_category_without_the_values()
    {
        var preview = ExportPipeline.Prepare(Planted(), Context());
        Assert.True(preview.Counts[RedactionCategory.UserName] >= 5);
        Assert.True(preview.Counts[RedactionCategory.HostName] >= 3);
        Assert.True(preview.Counts[RedactionCategory.WifiSsid] >= 1);
        Assert.True(preview.Counts[RedactionCategory.DeviceSerial] >= 1);
        Assert.True(preview.Counts[RedactionCategory.IpAddress] >= 2);
        Assert.True(preview.RedactionApplied);

        var node = JsonNode.Parse(preview.Text)!;
        Assert.True(node["redaction"]!["applied"]!.GetValue<bool>());
        Assert.True(node["redaction"]!["replacements"]!["UserName"]!.GetValue<int>() >= 5);
    }

    [Fact]
    public void The_preview_is_byte_for_byte_what_is_written_and_nothing_is_overwritten_by_default()
    {
        var preview = ExportPipeline.Prepare(Planted(), Context());
        var dir = Directory.CreateTempSubdirectory("jankscope-export-");
        try
        {
            var path = Path.Combine(dir.FullName, "incident.json");
            ExportPipeline.WriteAtomic(preview, path);
            Assert.Equal(preview.Bytes.ToArray(), File.ReadAllBytes(path));
            Assert.False(File.ReadAllBytes(path).AsSpan().StartsWith(Encoding.UTF8.Preamble), "no byte order mark");
            Assert.Empty(dir.GetFiles("*.tmp*"));

            Assert.Throws<IOException>(() => ExportPipeline.WriteAtomic(preview, path));
            ExportPipeline.WriteAtomic(preview, path, overwrite: true);
            Assert.Single(dir.GetFiles());
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void The_guard_fails_closed_when_a_known_value_survives_and_never_prints_it()
    {
        var context = Context();
        var ex = Assert.Throws<ExportBlockedException>(() => ExportGuard.Verify($"{{\"a\":\"leak {User} here\"}}", context));
        Assert.Contains(RedactionCategory.UserName, ex.Categories);
        Assert.DoesNotContain(User, ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Nothing was written", ex.Message);
    }

    [Theory]
    [InlineData("C:\\\\Users\\\\Zanzibar_Quinn\\\\x")]                  // JSON-escaped backslashes
    [InlineData("zanzibar_quinn")]                                      // case
    [InlineData("Zanzibar%5FQuinn")]                                    // url-encoded underscore
    [InlineData("name=Zanzibar_Quinn&")]
    public void The_guard_sees_json_escaped_url_encoded_and_re_cased_copies(string leaked)
    {
        Assert.Throws<ExportBlockedException>(() => ExportGuard.Verify(leaked, Context()));
    }

    [Theory]
    [InlineData("addr 8.8.4.4")]
    [InlineData("mac 01:23:45:67:89:ab")]
    [InlineData("six fe80::9")]
    [InlineData("mail me@example.org")]
    [InlineData("C:\\\\Users\\\\somebody\\\\x")]
    public void The_guard_also_stops_pattern_shaped_identifiers_it_was_never_told_about(string leaked)
    {
        Assert.Throws<ExportBlockedException>(() => ExportGuard.Verify(leaked, RedactionContext.Empty));
    }

    [Fact]
    public void The_guard_passes_text_that_only_carries_our_own_tokens()
    {
        ExportGuard.Verify("{\"p\":\"C:\\\\Users\\\\<USER>\\\\x on <HOST> <IP> <MAC> <EMAIL> <SSID> <SERIAL> game.exe <ARGS>\"}", Context());
    }

    [Fact]
    public void Mutation_a_redactor_that_skips_a_category_makes_the_pipeline_refuse_to_produce_a_preview()
    {
        // Simulates a bug: the SSID is known to the guard but the redactor was built without it. The pipeline must not hand out a preview.
        var guardContext = Context();
        var redactorContext = RedactionContext.Empty.With(RedactionCategory.UserName, User).With(RedactionCategory.HostName, Host);
        Assert.Throws<ExportBlockedException>(() => ExportPipeline.Prepare(Planted(), redactorContext, guardContext: guardContext));
    }

    [Fact]
    public void Without_redaction_the_preview_says_so_and_the_guard_is_not_applied()
    {
        var preview = ExportPipeline.Prepare(Planted(), Context(), redact: false);
        Assert.False(preview.RedactionApplied);
        Assert.Contains(User, preview.Text);
        var node = JsonNode.Parse(preview.Text)!;
        Assert.False(node["redaction"]!["applied"]!.GetValue<bool>());
    }

    [Fact]
    public void The_machine_context_knows_this_user_and_this_host()
    {
        var context = RedactionContext.ForThisMachine();
        Assert.Contains(context.Values, v => v.Category == RedactionCategory.HostName && string.Equals(v.Value, Environment.MachineName, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(Environment.UserName))
        {
            Assert.Contains(context.Values, v => v.Category == RedactionCategory.UserName && string.Equals(v.Value, Environment.UserName, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Blank_values_are_ignored_and_duplicates_collapse()
    {
        var context = RedactionContext.Empty
            .With(RedactionCategory.WifiSsid, null)
            .With(RedactionCategory.WifiSsid, "   ")
            .With(RedactionCategory.UserName, "dup")
            .With(RedactionCategory.UserName, "DUP");
        Assert.Single(context.Values);
    }

    [Fact]
    public void The_json_output_is_valid_and_keeps_numbers_as_numbers()
    {
        var preview = ExportPipeline.Prepare(new { Frames = 12, Ratio = 1.5, Ok = true, Nothing = (string?)null, Name = "x" }, RedactionContext.Empty);
        using var doc = JsonDocument.Parse(preview.Text);
        Assert.Equal(12, doc.RootElement.GetProperty("frames").GetInt32());
        Assert.Equal(1.5, doc.RootElement.GetProperty("ratio").GetDouble());
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.False(doc.RootElement.TryGetProperty("nothing", out _));
    }

    [Fact]
    public void The_guard_sees_a_value_that_json_escaping_hides_in_the_raw_text()
    {
        // The serializer writes a quote and a backslash as \" and \\, so the raw document does not contain the value itself.
        var context = RedactionContext.Empty.With(RedactionCategory.UserName, "Quinn \"The\\Wall\" Jr");
        const string json = "{\"note\":\"hello Quinn \\\"The\\\\Wall\\\" Jr bye\"}";
        Assert.DoesNotContain("Quinn \"The\\Wall\" Jr", json, StringComparison.Ordinal);
        Assert.Throws<ExportBlockedException>(() => ExportGuard.Verify(json, context));
    }

    [Fact]
    public void A_name_with_quotes_and_backslashes_is_redacted_in_a_real_export()
    {
        var context = RedactionContext.Empty.With(RedactionCategory.UserName, "Quinn \"The\\Wall\" Jr");
        var preview = ExportPipeline.Prepare(new Row("n", "hello Quinn \"The\\Wall\" Jr bye"), context);
        Assert.Equal("hello <USER> bye", JsonNode.Parse(preview.Text)!["value"]!.GetValue<string>());
    }

    [Fact]
    public void Two_properties_that_redact_to_the_same_name_do_not_crash_or_lose_data()
    {
        var context = RedactionContext.Empty.With(RedactionCategory.UserName, "alpha").With(RedactionCategory.UserName, "bravo");
        var preview = ExportPipeline.Prepare(new Dictionary<string, string> { ["alpha"] = "1", ["bravo"] = "2" }, context);
        var node = JsonNode.Parse(preview.Text)!.AsObject();
        Assert.Equal(2, node.Count(p => p.Key.StartsWith("<USER>", StringComparison.Ordinal)));
    }
}
