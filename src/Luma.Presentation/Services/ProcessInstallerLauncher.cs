using System.Diagnostics;
using Avalonia.Controls.ApplicationLifetimes;

namespace Luma.Presentation.Services;

/// <inheritdoc cref="IInstallerLauncher"/>
public sealed class ProcessInstallerLauncher(IClassicDesktopStyleApplicationLifetime lifetime)
    : IInstallerLauncher
{
    public UpdateHandoff Launch(string installerPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installerPath);

        if (!File.Exists(installerPath))
            throw new InvalidOperationException($"Installer not found at {installerPath}.");

        try
        {
            // UseShellExecute so the OS picks the right handler: msiexec for .msi on
            // Windows, the disk-image mounter for .dmg on macOS.
            Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Could not start the installer: {ex.Message}", ex);
        }

        // On macOS what just happened is that a disk image was mounted. Nothing has been
        // installed and nothing will be: replacing the application in /Applications is
        // the user's to do, by dragging. Quitting here would close the only window that
        // could tell them so, leaving a mounted volume and no explanation — so Luma
        // stays up and says what is left to do.
        if (!OperatingSystem.IsWindows())
            return UpdateHandoff.Opened;

        // Shut down through the lifetime rather than Environment.Exit, so the normal
        // shutdown path runs and preferences are flushed before the process goes away.
        lifetime.Shutdown();
        return UpdateHandoff.Installing;
    }
}
