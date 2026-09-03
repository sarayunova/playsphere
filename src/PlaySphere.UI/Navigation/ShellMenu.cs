using Microsoft.UI.Xaml.Controls;

namespace PlaySphere.UI.Navigation;

/// <summary>Descriptor for one item in the left navigation menu.</summary>
public sealed record ShellNavItem(string Key, string Label, Symbol Icon);

/// <summary>
/// Single source of truth for the shell menu. Built from <see cref="AppRoutes"/> so the
/// menu never drifts from the route registry.
/// </summary>
public static class ShellMenu
{
    public static IReadOnlyList<ShellNavItem> Items { get; } =
    [
        new(AppRoutes.Home, "Home", Symbol.Home),
        new(AppRoutes.Library, "Library", Symbol.Library),
        new(AppRoutes.Music, "Music", Symbol.Audio),
        new(AppRoutes.Playlists, "Playlists", Symbol.Bullets),
        new(AppRoutes.Streams, "Streams", Symbol.Link),
        new(AppRoutes.AiMedia, "AI Media", Symbol.AllApps),
    ];
}