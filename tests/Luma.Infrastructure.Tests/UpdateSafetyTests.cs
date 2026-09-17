using Luma.Infrastructure.Updates;

namespace Luma.Infrastructure.Tests;

/// <summary>
/// The rules that decide whether an update is allowed to be downloaded and run. Pure
/// functions, so they can be read on their own — which matters here more than most
/// places, because getting one of them wrong ends in running someone else's installer.
/// </summary>
public sealed class UpdateSafetyTests
{
    [Theory]
    [InlineData("https://updates.example.com")]
    [InlineData("https://updates.example.com/")]
    [InlineData("http://localhost:5000")]
    [InlineData("http://127.0.0.1:8080")]
    public void Https_and_the_local_machine_are_acceptable(string url) =>
        UpdateSafety.IsAcceptableUrl(url).ShouldBeTrue();

    [Theory]
    [InlineData("http://updates.example.com")]     // plain HTTP over a network
    [InlineData("ftp://updates.example.com")]
    [InlineData("file:///C:/evil.msi")]
    [InlineData("/relative/path")]
    [InlineData("")]
    [InlineData(null)]
    public void Everything_else_is_not(string? url) =>
        UpdateSafety.IsAcceptableUrl(url).ShouldBeFalse();

    [Theory]
    [InlineData("1.0.1", "1.0.0")]
    [InlineData("1.1.0", "1.0.9")]
    [InlineData("2.0.0", "1.9.9")]
    [InlineData("1.0.0.1", "1.0.0")]
    public void A_higher_version_is_an_update(string offered, string current) =>
        UpdateSafety.IsNewerVersion(offered, current).ShouldBeTrue();

    /// <summary>
    /// The case the rule exists for. Every other gate passes an older build: the server
    /// computes the hash for the file it serves, and serves it from its own origin — so
    /// nothing but the version says this is a step backwards onto known flaws.
    /// </summary>
    [Theory]
    [InlineData("0.9.0", "1.0.0")]
    [InlineData("1.0.0", "1.0.1")]
    [InlineData("1.0.0", "2.0.0")]
    public void An_older_version_is_refused(string offered, string current) =>
        UpdateSafety.IsNewerVersion(offered, current).ShouldBeFalse();

    [Fact]
    public void The_running_version_is_not_an_update_to_itself() =>
        UpdateSafety.IsNewerVersion("1.2.3", "1.2.3").ShouldBeFalse();

    /// <summary>
    /// Ordering is over the numeric part alone, so a pre-release of the version already
    /// running is not offered. Refusing is the safe way round: installing a beta over a
    /// release because the strings differ is exactly the surprise this class avoids.
    /// </summary>
    [Theory]
    [InlineData("1.2.3-beta1", "1.2.3")]
    [InlineData("1.2.3+build9", "1.2.3")]
    public void A_pre_release_of_the_running_version_is_not_an_update(string offered, string current) =>
        UpdateSafety.IsNewerVersion(offered, current).ShouldBeFalse();

    [Fact]
    public void A_pre_release_of_a_later_version_still_counts() =>
        UpdateSafety.IsNewerVersion("1.3.0-rc1", "1.2.9").ShouldBeTrue();

    [Theory]
    [InlineData("latest", "1.0.0")]
    [InlineData("", "1.0.0")]
    [InlineData(null, "1.0.0")]
    [InlineData("1.0.1", "")]
    [InlineData("1.0.1", null)]
    [InlineData("...", "1.0.0")]
    public void A_version_that_cannot_be_read_is_not_an_update(string? offered, string? current) =>
        UpdateSafety.IsNewerVersion(offered, current).ShouldBeFalse();

    [Fact]
    public void A_download_from_the_configured_server_is_allowed()
    {
        UpdateSafety.IsFromSameServer(
            "https://updates.example.com/api/downloads/9f2", "https://updates.example.com")
            .ShouldBeTrue();
    }

    [Theory]
    [InlineData("https://evil.example.com/luma.msi")]           // another host
    [InlineData("https://updates.example.com.evil.net/x.msi")]  // host that merely starts the same
    [InlineData("http://updates.example.com/luma.msi")]         // downgraded to plain HTTP
    [InlineData("https://updates.example.com:8443/luma.msi")]   // another port
    [InlineData("not a url")]
    public void A_download_pointing_anywhere_else_is_refused(string downloadUrl)
    {
        UpdateSafety.IsFromSameServer(downloadUrl, "https://updates.example.com")
            .ShouldBeFalse();
    }

    [Fact]
    public void Host_comparison_ignores_case()
    {
        UpdateSafety.IsFromSameServer(
            "https://Updates.Example.COM/api/downloads/1", "https://updates.example.com")
            .ShouldBeTrue();
    }

    [Theory]
    [InlineData("1.2.0", "1.2.0")]
    [InlineData("2.0.0-beta.3", "2.0.0-beta.3")]
    public void An_ordinary_version_is_left_alone(string version, string expected) =>
        UpdateSafety.FileNamePart(version).ShouldBe(expected);

    [Theory]
    [InlineData(@"..\..\Windows\System32\evil")]
    [InlineData("../../etc/passwd")]
    [InlineData(@"C:\Windows\evil")]
    [InlineData("1.0/../../x")]
    public void A_version_cannot_climb_out_of_the_folder(string version)
    {
        var part = UpdateSafety.FileNamePart(version);

        part.ShouldNotContain("..");
        part.ShouldNotContain("/");
        part.ShouldNotContain("\\");
        part.ShouldNotContain(":");
        Path.GetFileName(part).ShouldBe(part);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    [InlineData(null)]
    public void A_version_with_nothing_usable_becomes_a_placeholder(string? version) =>
        UpdateSafety.FileNamePart(version).ShouldBe("unknown");

    [Fact]
    public void A_very_long_version_is_cut_short()
    {
        UpdateSafety.FileNamePart(new string('9', 500)).Length.ShouldBeLessThanOrEqualTo(40);
    }

    /// <summary>The sanitised name has to survive being put in a path, which is the point.</summary>
    [Fact]
    public void The_result_is_usable_as_a_file_name()
    {
        var path = Path.Combine(Path.GetTempPath(), "Luma-updates",
            $"Luma-{UpdateSafety.FileNamePart("../../evil")}.msi");

        Path.GetDirectoryName(path).ShouldBe(
            Path.Combine(Path.GetTempPath(), "Luma-updates"));
    }
}
