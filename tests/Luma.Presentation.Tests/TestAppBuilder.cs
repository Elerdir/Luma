using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Luma.Presentation.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Luma.Presentation.Tests;

/// <summary>
/// Boots a headless Avalonia application for tests marked <c>[AvaloniaFact]</c> — just
/// enough of a UI thread and dispatcher for real <c>Dispatcher.UIThread.Post</c> calls to
/// actually run, which is what lets <see cref="MainViewModelDispatchTests"/> exercise the
/// same code path the app runs under, instead of one that quietly never executes.
///
/// Deliberately the plain <see cref="Application"/> base class rather than the real
/// <c>Luma.Presentation.App</c>: nothing under test creates a <c>Window</c> or a
/// <c>VideoView</c>, and the real <c>App</c>'s composition root touches LibVLC, which
/// headless test runs should not need.
/// </summary>
public class TestAppBuilder
{
    // Luma.Application (the application layer) shadows the bare name "Application" from
    // this namespace, so Avalonia's has to stay qualified here.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Avalonia.Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
