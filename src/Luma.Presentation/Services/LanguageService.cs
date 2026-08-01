using Luma.Application.Abstractions;
using Luma.Presentation.Localization;

namespace Luma.Presentation.Services;

/// <summary>
/// Applies and remembers the interface language. Wrapping this keeps settings I/O out
/// of the view-model, which otherwise only needs to say "use this language".
/// </summary>
public sealed class LanguageService(ISettingsStore<InterfaceOptions> store)
{
    /// <summary>Apply the stored language at startup.</summary>
    public async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        var options = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
        Localizer.Instance.SetLanguage(options.Language);
    }

    /// <summary>Switch language and remember the choice.</summary>
    public async Task SetAsync(string language, CancellationToken cancellationToken = default)
    {
        Localizer.Instance.SetLanguage(language);

        try
        {
            await store
                .SaveAsync(new InterfaceOptions { Language = language }, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The language still changed for this session; failing to write it down is
            // not worth interrupting anyone over.
        }
    }
}
