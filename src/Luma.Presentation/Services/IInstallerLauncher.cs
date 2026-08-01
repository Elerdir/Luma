namespace Luma.Presentation.Services;

/// <summary>
/// Hands a downloaded installer to the OS and closes Luma so it can be replaced.
/// Abstracted for the same reason as <see cref="IFilePicker"/>: view-models that call
/// <c>Process.Start</c> and <c>Environment.Exit</c> directly cannot be tested.
/// </summary>
public interface IInstallerLauncher
{
    /// <summary>
    /// Start the installer and quit. Does not return when it succeeds.
    /// </summary>
    /// <exception cref="InvalidOperationException">The installer could not be started.</exception>
    void LaunchAndExit(string installerPath);
}
