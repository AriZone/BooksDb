using System;

namespace BooksDb.ViewModels;

public sealed class LendRequest
{
    public long?    BorrowerId { get; init; }   // set when an existing borrower was picked
    public string?  FirstName  { get; init; }   // for a new borrower (both required)
    public string?  LastName   { get; init; }
    public DateTime DateLent   { get; init; }
    public string?  Notes      { get; init; }
}

public sealed class ReturnRequest
{
    public DateTime DateReturned { get; init; }
    public string?  Notes        { get; init; }
}