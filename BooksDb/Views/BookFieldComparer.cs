using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using BooksDb.Data.Models;

namespace BooksDb.Views;

/// <summary>
/// Compares two BookListItem rows by a chosen string field using Polish collation,
/// so header sorting orders diacritics correctly (ą after a, ł after l, …).
/// Direction-agnostic — the DataGrid applies ascending/descending itself.
/// </summary>
public sealed class BookFieldComparer : IComparer
{
    private static readonly CompareInfo Pl = CultureInfo.GetCultureInfo("pl-PL").CompareInfo;
    private readonly Func<BookListItem, string?> _select;
    private readonly IComparer<string?>? _stringComparer;

    public BookFieldComparer(Func<BookListItem, string?> select, IComparer<string?>? stringComparer = null)
    {
        _select = select;
        _stringComparer = stringComparer;
    }

    public int Compare(object? x, object? y)
    {
        var sa = (x as BookListItem) is { } a ? _select(a) ?? "" : "";
        var sb = (y as BookListItem) is { } b ? _select(b) ?? "" : "";
        return _stringComparer is not null
            ? _stringComparer.Compare(sa, sb)
            : Pl.Compare(sa, sb, CompareOptions.None);
    }
}