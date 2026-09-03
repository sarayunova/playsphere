namespace PlaySphere.Domain.Media;

/// <summary>
/// Outcome of a timeline hover-thumbnail filmstrip generation (PICKLE.md §20 "hover thumbnail").
/// The strip is a single contact-sheet JPEG of <see cref="Columns"/>×<see cref="Rows"/> cells of
/// fixed <see cref="CellWidth"/>×<see cref="CellHeight"/>; the caller crops the cell for a scrub
/// position. Always produced, never thrown.
/// </summary>
public sealed record ThumbnailStripResult(
    bool Success,
    string? StripPath,
    int Columns,
    int Rows,
    int CellWidth,
    int CellHeight,
    string? ErrorMessage)
{
    public static ThumbnailStripResult Ok(string stripPath, int columns, int rows, int cellWidth, int cellHeight) =>
        new(true, stripPath, columns, rows, cellWidth, cellHeight, null);

    public static ThumbnailStripResult Fail(string errorMessage) =>
        new(false, null, 0, 0, 0, 0, errorMessage);
}