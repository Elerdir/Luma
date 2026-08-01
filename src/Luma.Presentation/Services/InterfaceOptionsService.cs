using Luma.Application.Abstractions;
using Luma.Presentation.Localization;

namespace Luma.Presentation.Services;

/// <summary>
/// Loads, applies and remembers the shell preferences, keeping settings I/O out of the
/// view-model. One service for the whole record rather than one per setting: they share
/// a file, so saving a single field on its own would write the others back at whatever
/// value it happened to assume.
/// </summary>
public sealed class InterfaceOptionsService(ISettingsStore<InterfaceOptions> store)
{
    /// <summary>
    /// The options in force. Defaults until <see cref="RestoreAsync"/> has run, which
    /// happens before the window is built.
    /// </summary>
    public InterfaceOptions Current { get; private set; } = new();

    /// <summary>Load the stored preferences and apply the ones this service owns.</summary>
    public async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        Current = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
        Localizer.Instance.SetLanguage(Current.Language);
    }

    /// <summary>Switch language and remember the choice.</summary>
    public Task SetLanguageAsync(string language, CancellationToken cancellationToken = default)
    {
        Localizer.Instance.SetLanguage(language);
        return SaveAsync(Current with { Language = language }, cancellationToken);
    }

    /// <summary>
    /// Remember whether opening one file should load its folder. Applying it is the
    /// player's business; this only records the choice.
    /// </summary>
    public Task SetLoadWholeFolderAsync(bool enabled, CancellationToken cancellationToken = default) =>
        SaveAsync(Current with { LoadWholeFolder = enabled }, cancellationToken);

    private async Task SaveAsync(InterfaceOptions options, CancellationToken cancellationToken)
    {
        Current = options;

        try
        {
            await store.SaveAsync(options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The choice still holds for this session; failing to write it down is not
            // worth interrupting anyone over.
        }
    }
}
