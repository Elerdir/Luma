using Luma.Presentation.Services;

namespace Luma.Presentation.Tests.Fakes;

/// <summary>
/// Stands in for handing the download to the operating system, without handing anything
/// anywhere. The test says what the platform would have done with it.
/// </summary>
public sealed class FakeInstallerLauncher : IInstallerLauncher
{
    /// <summary>What the platform reports back. Windows installs; macOS only opens.</summary>
    public UpdateHandoff Handoff { get; set; } = UpdateHandoff.Installing;

    /// <summary>Thrown instead of handing over, when set.</summary>
    public Exception? Fails { get; set; }

    /// <summary>What was handed over, in order.</summary>
    public List<string> Launched { get; } = [];

    public UpdateHandoff Launch(string installerPath)
    {
        if (Fails is not null)
            throw Fails;

        Launched.Add(installerPath);
        return Handoff;
    }
}
