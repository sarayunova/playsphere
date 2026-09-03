namespace PlaySphere.UI.Navigation;

/// <summary>
/// Canonical route keys for the PlaySphere shell. Single source of truth for
/// navigation; the menu, page register and tests are all driven from here.
/// </summary>
public static class AppRoutes
{
    public const string Home = "home";
    public const string Library = "library";
    public const string Music = "music";
    public const string Playlists = "playlists";
    public const string Streams = "streams";
    public const string AiMedia = "ai";
    public const string Player = "player";
    public const string Conversions = "conversions";

    /// <summary>All shell menu routes, in display order.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Home,
        Library,
        Music,
        Playlists,
        Streams,
        AiMedia,
    ];
}