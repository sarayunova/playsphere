using Microsoft.UI.Xaml.Controls;
using PlaySphere.UI.Navigation;
using PlaySphere.UI.Pages;
using Xunit;

namespace PlaySphere.UnitTests;

public sealed class NavigationRouteTests
{
    [Fact]
    public void Every_shell_route_has_a_registered_page_type()
    {
        var routes = RouteRegistry.CreateDefault();

        foreach (var key in AppRoutes.All)
        {
            Assert.True(routes.ContainsKey(key), $"Route '{key}' is missing from the registry.");
            Assert.NotNull(routes[key]);
        }
    }

    [Fact]
    public void Shell_menu_matches_the_route_registry_exactly_once_each()
    {
        var registry = RouteRegistry.CreateDefault();
        var menuKeys = ShellMenu.Items.Select(i => i.Key).OrderBy(k => k, StringComparer.Ordinal);
        var routeKeys = AppRoutes.All.OrderBy(k => k, StringComparer.Ordinal);

        Assert.Equal(routeKeys, menuKeys);

        foreach (var key in AppRoutes.All)
        {
            Assert.Single(ShellMenu.Items, i => i.Key == key);
            Assert.Single(registry.Keys, k => k == key);
        }
    }

    [Fact]
    public void Shell_menu_labels_and_icons_are_unique()
    {
        Assert.Equal(
            ShellMenu.Items.Count,
            ShellMenu.Items.Select(i => i.Label).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            ShellMenu.Items.Count,
            ShellMenu.Items.Select(i => i.Icon).Distinct().Count());
    }

    [Fact]
    public void Every_route_resolves_to_a_public_Page_derived_type()
    {
        var routes = RouteRegistry.CreateDefault();

        foreach (var (key, pageType) in routes)
        {
            Assert.True(typeof(Page).IsAssignableFrom(pageType),
                $"Route '{key}' resolves to {pageType}, which is not a Page.");
            Assert.True(pageType.IsPublic,
                $"Route '{key}' page must be public (XAML code-behind requirement).");
        }
    }

    [Fact]
    public void Player_route_is_registered_in_RouteRegistry()
    {
        var routes = RouteRegistry.CreateDefault();

        Assert.True(routes.ContainsKey(AppRoutes.Player));
        Assert.Same(typeof(PlayerPage), routes[AppRoutes.Player]);
    }

    [Fact]
    public void Route_resolution_is_case_sensitive()
    {
        var svc = new NavigationService();

        Assert.False(svc.Resolve(AppRoutes.Home.ToUpperInvariant()) is { });
        Assert.Same(typeof(HomePage), svc.Resolve(AppRoutes.Home));
    }

    [Fact]
    public void Unknown_route_is_not_navigable_and_resolves_to_null()
    {
        var svc = new NavigationService();

        Assert.False(svc.CanNavigate("not-a-route"));
        Assert.Null(svc.Resolve("not-a-route"));
        Assert.Null(svc.CurrentRoute);
    }

    [Fact]
    public void Navigate_requires_a_frame()
    {
        var svc = new NavigationService();

        Assert.Throws<ArgumentNullException>(() => svc.Navigate(null!, AppRoutes.Home));
    }

    [Fact]
    public void Navigate_throws_for_unknown_route()
    {
        var svc = new NavigationService();

        var ex = Assert.Throws<ArgumentException>(() => svc.Navigate(null!, "not-a-route"));
        Assert.Contains("Unknown route", ex.Message, StringComparison.Ordinal);
    }
}