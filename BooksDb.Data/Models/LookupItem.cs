namespace BooksDb.Data.Models;

public sealed class LookupItem
{
    public long   Id   { get; init; }
    public string Name { get; init; } = "";

    public override string ToString() => Name;
}