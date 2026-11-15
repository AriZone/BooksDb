using System.Collections.Generic;
using BooksDb.Data.Lookup;

namespace BooksDb.ViewModels;

public enum BnLookupKind { NothingToSearch, Error, Results }

public sealed record BnLookup(BnLookupKind Kind, IReadOnlyList<BnResult> Results, string? Message);