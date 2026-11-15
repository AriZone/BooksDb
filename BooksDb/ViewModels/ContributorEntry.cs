using BooksDb.Data.Models;

namespace BooksDb.ViewModels;

public sealed class ContributorEntry
{
    public long?   PersonId   { get; init; }
    public string? FirstName  { get; init; }
    public string? LastName   { get; init; }
    public long    RoleId     { get; init; }
    public string  PersonName { get; init; } = "";
    public string  RoleName   { get; init; } = "";

    public string Display => $"{PersonName} — {RoleName}";
    public override string ToString() => Display;

    public ContributorInput ToInput() => new()
    {
        PersonId  = PersonId,
        FirstName = FirstName,
        LastName  = LastName,
        RoleId    = RoleId,
    };
}