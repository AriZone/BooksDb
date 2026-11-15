namespace BooksDb.Data.Models;

public sealed class BookDetail
{
    public long    Id                 { get; set; }   // 0 = new
    public string  Title              { get; set; } = "";
    public string? OriginalTitle      { get; set; }
    public long    LanguageId         { get; set; }
    public long?   OriginalLanguageId { get; set; }
    public int?    YearPublished      { get; set; }
    public int?    YearCopyright      { get; set; }
    public string? Edition            { get; set; }
    public string? OriginalEdition    { get; set; }
    public string? Isbn13             { get; set; }
    public string? Isbn10             { get; set; }
    public long?   CoverId            { get; set; }
    public int?    Pages              { get; set; }
    public string? Description        { get; set; }
    public string? Shelf              { get; set; }
    public bool    IsRead             { get; set; }
    public string? Notes              { get; set; }

    public long?   PublisherId        { get; set; }
    public string? PublisherName      { get; set; }   // display only (from join)
    public long?   CityId             { get; set; }
    public string? CityName           { get; set; }   // display only (from join)
    public int?    InitialPrice       { get; set; }   // grosze
    public string? DateOfBuy          { get; set; }   // ISO yyyy-MM-dd
    public string? DateOfLastRead     { get; set; }   // ISO yyyy-MM-dd
}