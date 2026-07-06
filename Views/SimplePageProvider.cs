using Wpf.Ui.Abstractions;

namespace YGODuelSimulator.Views;

/// <summary>
/// Minimal page provider for WPF-UI's <c>NavigationView</c>. We don't use
/// dependency injection, so pages are simply constructed on demand via their
/// parameterless constructors.
/// </summary>
public sealed class SimplePageProvider : INavigationViewPageProvider
{
    public object? GetPage(Type pageType) => Activator.CreateInstance(pageType);
}
