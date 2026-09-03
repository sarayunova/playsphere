namespace PlaySphere.Domain.Media;

/// <summary>Outcome of a media-conversion run (PICKLE.md §29). Always produced, never thrown.</summary>
public sealed record ConversionResult(bool Success, string? ErrorMessage)
{
    public static ConversionResult Ok { get; } = new(true, null);

    public static ConversionResult Fail(string errorMessage) => new(false, errorMessage);
}