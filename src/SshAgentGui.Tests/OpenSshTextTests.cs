using SshAgentGui.Ssh;

namespace SshAgentGui.Tests;

public sealed class OpenSshTextTests
{
    [Fact]
    public void Unknown_path_bearing_text_is_generic_fail()
    {
        var raw = @"ssh-add: unexpected diagnostic for C:\Users\me\.ssh\id_ed25519";
        Assert.Equal(OpenSshText.AddFailed, OpenSshText.ForAdd(raw, exitCode: 1, successIfEmpty: false));
        Assert.DoesNotContain(@"C:\Users", OpenSshText.ForAdd(raw, 1, false), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Empty_failed_add_is_incorrect_passphrase() =>
        Assert.Equal(OpenSshText.IncorrectPassphrase, OpenSshText.ForAdd("", 1, successIfEmpty: false));

    [Theory]
    [InlineData("Permissions 0664 for 'key' are too open.", OpenSshText.AccessDenied)]
    [InlineData(@"C:\Users\me\.ssh\missing: No such file or directory", OpenSshText.FileNotFound)]
    [InlineData("Error loading key: invalid format", OpenSshText.UnusableKey)]
    [InlineData("Invalid lifetime", OpenSshText.InvalidLifetime)]
    [InlineData("Bad passphrase", OpenSshText.IncorrectPassphrase)]
    public void Known_maps(string text, string expected) =>
        Assert.Equal(expected, OpenSshText.Classify(text, OpenSshText.AddFailed));

    [Fact]
    public void Does_not_map_already_loaded() =>
        Assert.Equal(OpenSshText.AddFailed, OpenSshText.Classify("Identity added: already loaded", OpenSshText.AddFailed));

    [Fact]
    public void Redact_then_classify_does_not_return_secret()
    {
        var classified = OpenSshText.ForKeygen("ssh-keygen: failed for secret-value", "secret-value");
        Assert.Equal(OpenSshText.KeygenFailed, classified);
        Assert.DoesNotContain("secret-value", classified, StringComparison.Ordinal);
    }
}
