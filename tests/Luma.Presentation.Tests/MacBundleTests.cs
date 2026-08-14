using System.Xml.Linq;
using Luma.Domain.Media;

namespace Luma.Presentation.Tests;

/// <summary>
/// Checks on installer/macos/Info.plist, the file that decides what Finder will offer
/// Luma for.
///
/// It is the macOS counterpart to the file associations in the MSI, and it has the same
/// failure mode: nothing here breaks a build or a test run, so a claim that is wrong
/// stays wrong until someone double-clicks a film and gets the wrong application — or
/// none. These are the parts that can be checked from here. Whether LaunchServices
/// actually offers Luma for every one of them can only be settled on a Mac.
/// </summary>
public class MacBundleTests
{
    private static readonly XDocument Plist = XDocument.Load(
        Path.Combine(AppContext.BaseDirectory, "macos", "Info.plist"));

    /// <summary>The value element that follows a &lt;key&gt; in a plist dictionary.</summary>
    private static IEnumerable<XElement> ValuesFor(string key) =>
        Plist.Descendants("key")
            .Where(k => k.Value == key)
            .Select(k => k.ElementsAfterSelf().First());

    private static IEnumerable<string> Strings(string key) =>
        ValuesFor(key).SelectMany(value =>
            value.Name == "string" ? [value.Value] : value.Elements("string").Select(s => s.Value));

    [Fact]
    public void The_version_is_still_a_placeholder()
    {
        // build-dmg.sh substitutes it. A hard-coded number here would ship every
        // release reporting the same version — including to the update server, which
        // would then never offer an update again.
        Strings("CFBundleVersion").ShouldContain("@VERSION@");
        Strings("CFBundleShortVersionString").ShouldContain("@VERSION@");
    }

    [Fact]
    public void The_bundle_executable_matches_what_the_build_produces()
    {
        // The project renames the assembly to Luma; a mismatch here is a bundle that
        // will not launch at all.
        Strings("CFBundleExecutable").ShouldContain("Luma");
    }

    [Fact]
    public void Audio_files_can_open_Luma()
    {
        // Luma plays eight audio formats and stepping through an album is a supported
        // way to use it, but the bundle used to declare video only.
        Strings("LSItemContentTypes").ShouldContain("public.audio");
    }

    [Fact]
    public void Video_files_can_open_Luma()
    {
        Strings("LSItemContentTypes").ShouldContain("public.movie");
    }

    /// <summary>
    /// Every extension the bundle claims is one the player will actually open. Claiming
    /// a type Luma cannot play means Finder offers it and the user gets an error.
    /// </summary>
    [Fact]
    public void Nothing_is_claimed_that_Luma_does_not_play()
    {
        var claimed = Strings("public.filename-extension").ToArray();

        claimed.ShouldNotBeEmpty();

        foreach (var extension in claimed)
            MediaFileTypes.IsPlayable($"film.{extension}")
                .ShouldBeTrue($"Info.plist claims .{extension}, which MediaFileTypes does not list.");
    }

    /// <summary>
    /// Every type declared is also claimed. A declaration nobody references teaches
    /// LaunchServices about a file format and then does not ask for it — all of the
    /// cost of the entry and none of the effect.
    /// </summary>
    [Fact]
    public void Every_declared_type_is_one_the_bundle_asks_for()
    {
        var claimed = Strings("LSItemContentTypes").ToHashSet(StringComparer.Ordinal);

        foreach (var declared in Strings("UTTypeIdentifier"))
            claimed.ShouldContain(declared);
    }

    /// <summary>
    /// Luma never takes an extension for itself — the same restraint the MSI shows.
    /// On macOS that is LSHandlerRank: Owner or Default would make Luma the handler for
    /// every film on the machine the moment it is installed.
    /// </summary>
    [Fact]
    public void No_document_type_makes_Luma_the_default_handler()
    {
        var ranks = Strings("LSHandlerRank").ToArray();

        ranks.ShouldNotBeEmpty();
        ranks.ShouldAllBe(rank => rank == "Alternate");
    }
}
