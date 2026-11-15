using ClosedXML.Excel;
using BooksDb.Data.Models;

namespace BooksDb.Data;

public static class ExcelExporter
{
    public static void Export(IReadOnlyList<BookExportRow> rows, string path)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Books");

        string[] headers =
        {
            "Title", "Original title", "Authors", "Translators", "Illustrators", "Editors",
            "Language", "Original language", "Year", "Year ©", "Edition", "Original edition",
            "Publisher", "City", "ISBN-13", "ISBN-10", "Cover", "Pages", "Shelf", "Read",
            "Price (PLN)", "Bought", "Last read", "Description", "Notes", "Categories", "Pictures"
        };
        for (var i = 0; i < headers.Length; i++)
            ws.Cell(1, i + 1).Value = headers[i];
        ws.Row(1).Style.Font.Bold = true;

        var r = 2;
        foreach (var b in rows)
        {
            ws.Cell(r,  1).Value = b.Title;
            ws.Cell(r,  2).Value = b.OriginalTitle ?? "";
            ws.Cell(r,  3).Value = b.Authors ?? "";
            ws.Cell(r,  4).Value = b.Translators ?? "";
            ws.Cell(r,  5).Value = b.Illustrators ?? "";
            ws.Cell(r,  6).Value = b.Editors ?? "";
            ws.Cell(r,  7).Value = b.Language;
            ws.Cell(r,  8).Value = b.OriginalLanguage ?? "";
            SetInt(ws.Cell(r,  9), b.YearPublished);
            SetInt(ws.Cell(r, 10), b.YearCopyright);
            ws.Cell(r, 11).Value = b.Edition ?? "";
            ws.Cell(r, 12).Value = b.OriginalEdition ?? "";
            ws.Cell(r, 13).Value = b.Publisher ?? "";
            ws.Cell(r, 14).Value = b.City ?? "";
            ws.Cell(r, 15).Value = b.Isbn13 ?? "";
            ws.Cell(r, 16).Value = b.Isbn10 ?? "";
            ws.Cell(r, 17).Value = b.Cover ?? "";
            SetInt(ws.Cell(r, 18), b.Pages);
            ws.Cell(r, 19).Value = b.Shelf ?? "";
            ws.Cell(r, 20).Value = b.IsRead ? "Yes" : "No";
            if (b.InitialPrice is int g)
            {
                ws.Cell(r, 21).Value = g / 100m;
                ws.Cell(r, 21).Style.NumberFormat.Format = "0.00";
            }
            ws.Cell(r, 22).Value = b.DateOfBuy ?? "";
            ws.Cell(r, 23).Value = b.DateOfLastRead ?? "";
            ws.Cell(r, 24).Value = b.Description ?? "";
            ws.Cell(r, 25).Value = b.Notes ?? "";
            ws.Cell(r, 26).Value = b.Categories ?? "";
            ws.Cell(r, 27).Value = b.Pictures ?? "";
            r++;
        }

        ws.Columns().AdjustToContents();
        ws.SheetView.FreezeRows(1);
        wb.SaveAs(path);
    }

    private static void SetInt(IXLCell cell, int? v)
    {
        if (v.HasValue) cell.Value = v.Value;
    }
}