namespace BooksDb.Data.Models;

public sealed class LoanHistoryItem
{
    public string  Borrower     { get; init; } = "";
    public string  DateLent     { get; init; } = "";
    public string? DateReturned { get; init; }
    public string? Notes        { get; init; }

    public string ReturnedDisplay => string.IsNullOrWhiteSpace(DateReturned) ? "— still out —" : DateReturned!;
}