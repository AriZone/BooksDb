namespace BooksDb.Data.Models;

public sealed class PersonSuggestion
{
    public long    Id        { get; init; }
    public string? FirstName { get; init; }
    public string  LastName  { get; init; } = "";
    public string  Display   { get; init; } = "";   // "Lem, Stanisław"

    public override string ToString() => Display;
}