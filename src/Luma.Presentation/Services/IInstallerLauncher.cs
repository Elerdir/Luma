namespace Luma.Presentation.Services;

/// <summary>
/// Hands a downloaded update to the operating system.
///
/// Abstracted for the same reason as <see cref="IFilePicker"/>: view-models that call
/// <c>Process.Start</c> and shut the application down cannot be tested.
/// </summary>
public interface IInstallerLauncher
{
    /// <summary>
    /// Hand the download over, and report what the platform did with it. Does not
    /// return when the result is <see cref="UpdateHandoff.Installing"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The download could not be handed over.</exception>
    UpdateHandoff Launch(string installerPath);
}

/// <summary>
/// What handing an update to the operating system actually achieves — which is not the
/// same thing on every platform, and the banner used to claim it was.
/// </summary>
public enum UpdateHandoff
{
    /// <summary>
    /// The installer is running and Luma is closing so it can be replaced. Windows,
    /// where an MSI does the whole job.
    /// </summary>
    Installing,

    /// <summary>
    /// The disk image is open and the rest is the user's to do: drag Luma into
    /// Applications, replacing the copy that is there.
    ///
    /// macOS has no equivalent of running an installer that replaces the running
    /// application, and pretending otherwise left someone staring at a mounted volume
    /// wondering why nothing had been updated.
    /// </summary>
    Opened
}
