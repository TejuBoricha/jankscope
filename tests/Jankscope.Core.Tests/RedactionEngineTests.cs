using Jankscope.Core.Reports;

namespace Jankscope.Core.Tests;

public class RedactionEngineTests
{
    [Fact]
    public void Redaction_replaces_usernames_hostnames_macs_and_ips()
    {
        const string raw = "Error on PC DESKTOP-ABC1234 by user JohnDoe in C:\\Users\\JohnDoe\\AppData\\Local from IP 192.168.1.50 and MAC 00:1A:2B:3C:4D:5E";
        var redacted = RedactionEngine.RedactText(raw, userName: "JohnDoe", hostName: "DESKTOP-ABC1234");

        Assert.DoesNotContain("JohnDoe", redacted);
        Assert.DoesNotContain("DESKTOP-ABC1234", redacted);
        Assert.DoesNotContain("192.168.1.50", redacted);
        Assert.DoesNotContain("00:1A:2B:3C:4D:5E", redacted);

        Assert.Contains("<USER>", redacted);
        Assert.Contains("<HOST>", redacted);
        Assert.Contains("<IP>", redacted);
        Assert.Contains("<MAC>", redacted);
    }

    [Fact]
    public void Canary_assertion_passes_when_no_canary_survives()
    {
        const string text = "System report: clean output with <USER> and <HOST>";
        string[] canaries = ["SecretUser", "SecretHost", "10.0.0.1"];

        RedactionEngine.AssertCanariesRedacted(text, canaries);
    }

    [Fact]
    public void Canary_assertion_fails_closed_when_a_canary_survives()
    {
        const string text = "System report leaked SecretUser in path C:\\Users\\SecretUser\\test";
        string[] canaries = ["SecretUser", "SecretHost"];

        var ex = Assert.Throws<InvalidOperationException>(() =>
            RedactionEngine.AssertCanariesRedacted(text, canaries));
        Assert.Contains("SecretUser", ex.Message);
    }
}
