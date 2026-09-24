namespace AmdNrAssistant;

public static class LibraryPaging
{
    public const int PageSize = 8;
    public static int PageCount(int count) => Math.Max(1, (Math.Max(0, count) + PageSize - 1) / PageSize);
    public static int ClampPage(int page, int count) => Math.Clamp(page, 0, PageCount(count) - 1);
}
