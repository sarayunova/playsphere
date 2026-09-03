using Microsoft.UI.Xaml.Controls;

namespace PlaySphere.UI.Navigation;

/// <summary>
/// Resolves canonical routes to pages and performs in-app navigation.
/// Frame navigation is deliberately UI-bound; route resolution is pure and unit-testable.
/// </summary>
public sealed class NavigationService
{
    private readonly IReadOnlyDictionary<string, Type> _routes;

    public NavigationService(IReadOnlyDictionary<string, Type>? routes = null)
    {
        _routes = routes ?? RouteRegistry.CreateDefault();
    }

    /// <summary>The most recent route navigated to, or null before the first navigation.</summary>
    public string? CurrentRoute { get; private set; }

    /// <summary>True when <paramref name="route"/> is a registered canonical route.</summary>
    public bool CanNavigate(string route) => _routes.ContainsKey(route);

    /// <summary>Resolves a route to its page type, or null when unregistered.</summary>
    public Type? Resolve(string route) => _routes.TryGetValue(route, out var pageType) ? pageType : null;

    /// <summary>Navigates a frame to the page registered for <paramref name="route"/>.</summary>
    public void Navigate(Frame frame, string route, object? parameter = null)
    {
        if (!_routes.TryGetValue(route, out var pageType))
        {
            throw new ArgumentException($"Unknown route '{route}'.", nameof(route));
        }

        ArgumentNullException.ThrowIfNull(frame);

        if (frame.CurrentSourcePageType == pageType && parameter is null)
        {
            return;
        }

        frame.Navigate(pageType, parameter);
        CurrentRoute = route;
    }
}