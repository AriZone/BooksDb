namespace BooksDb.Data.Models;

// What we persist for one contributor link.
public sealed class ContributorInput
{
    public long?   PersonId  { get; init; }   // null = create from First/Last
    public string? FirstName { get; init; }
    public string? LastName  { get; init; }
    public long    RoleId    { get; init; }
}

// What we read back to display a book's contributors.
public sealed class ContributorRow
{
    public long   PersonId   { get; init; }
    public string PersonName { get; init; } = "";
    public long   RoleId     { get; init; }
    public string RoleName   { get; init; } = "";
}