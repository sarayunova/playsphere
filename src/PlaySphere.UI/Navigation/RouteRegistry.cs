using Microsoft.UI.Xaml.Controls;

namespace PlaySphere.UI.Navigation;

/// <summary>
/// Maps canonical routes to the <see cref="Page"/> type that renders them.
/// </summary>
public static class RouteRegistry
{
    public static IReadOnlyDictionary<string, Type> CreateDefault() =>
        new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            [AppRoutes.Home] = typeof(Pages.HomePage),
            [AppRoutes.Library] = typeof(Pages.LibraryPage),
            [AppRoutes.Music] = typeof(Pages.MusicPage),
            [AppRoutes.Playlists] = typeof(Pages.PlaylistsPage),
            [AppRoutes.Streams] = typeof(Pages.StreamsPage),
            [AppRoutes.AiMedia] = typeof(Pages.AiMediaPage),
            [AppRoutes.Conversions] = typeof(Pages.ConversionsPage),
            [AppRoutes.Player] = typeof(Pages.PlayerPage),
        };
}