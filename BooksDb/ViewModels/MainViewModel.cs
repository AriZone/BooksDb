using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.ComponentModel;   // ListSortDirection
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.Collections;    // DataGridCollectionView, DataGridSortDescription
using BooksDb.Data;
using BooksDb.Data.Models;
using BooksDb.Data.Lookup;
using BooksDb.Views;


namespace BooksDb.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private BookRepository _books = null!;
    private LookupRepository _lookups = null!;
    private PersonRepository _people = null!;
    private ExcelImporter _importer = null!;
    private BibliotekaNarodowaClient _bn = null!;
    private PictureRepository _pictures = null!;
    private SettingsStore _settings = null!;
    private BorrowerRepository _borrowers = null!;
    private LoanRepository _loans = null!;
    private PendingCaptureRepository _captures = null!;
    private GoogleBooksClient _google = null!;
    private OpenLibraryClient _ol = null!;

    // set while the form is being filled from a phone capture; Save() marks it committed
    private long? _pendingCaptureId;

    // that capture's staged photo paths (relative to the picture root); attached to the book on Save
    private IReadOnlyList<string> _pendingCapturePhotos = Array.Empty<string>();

    public ObservableCollection<BookListItem>     Books        { get; } = new();

    private DataGridCollectionView? _booksView;
    public DataGridCollectionView BooksView
    {
        get => _booksView ??= CreateBooksView();
        private set { _booksView = value; OnPropertyChanged(); }
    }

    private DataGridCollectionView CreateBooksView()
    {
        var view = new DataGridCollectionView(Books);
        view.SortDescriptions.Add(
            DataGridSortDescription.FromPath(nameof(BookListItem.Id), ListSortDirection.Ascending));
        return view;
    }
    
    partial void OnSelectedSortChanged(LookupItem? value)
    {
        ApplySort(value?.Id ?? 0);
        SortIsActive = (value?.Id ?? 0) != 0;     // "Added" == default → not dirty
        ResetViewCommand.NotifyCanExecuteChanged();
    }

    private void ApplySort(long key)
    {
        var desc = key switch
        {
            1 => DataGridSortDescription.FromComparer(new BookFieldComparer(b => b.Title),   ListSortDirection.Ascending),
            2 => DataGridSortDescription.FromComparer(new BookFieldComparer(b => b.Authors), ListSortDirection.Ascending),
            3 => DataGridSortDescription.FromPath(nameof(BookListItem.YearPublished),        ListSortDirection.Ascending),
            4 => DataGridSortDescription.FromComparer(new BookFieldComparer(b => b.Shelf, ShelfComparer.Instance), ListSortDirection.Ascending),
            _ => DataGridSortDescription.FromPath(nameof(BookListItem.Id),                   ListSortDirection.Ascending),
        };
        BooksView.SortDescriptions.Clear();
        BooksView.SortDescriptions.Add(desc);
    }

    public ObservableCollection<Language>         Languages    { get; } = new();
    public ObservableCollection<Cover>            Covers       { get; } = new();
    public ObservableCollection<LookupItem>       Roles        { get; } = new();
    public ObservableCollection<ContributorEntry> Contributors { get; } = new();
    public ObservableCollection<string>           Categories   { get; } = new();
    public ObservableCollection<PictureItem>      Pictures     { get; } = new();
    public ObservableCollection<LookupItem>    CategoryFilters { get; } = new();

    [ObservableProperty]
    private LookupItem? _selectedCategoryFilter;
    
    public ObservableCollection<LookupItem> StatusFilters { get; } = new()
    {
        new LookupItem { Id = 0, Name = "All books" },
        new LookupItem { Id = 1, Name = "Unread" },
        new LookupItem { Id = 2, Name = "Read" },
        new LookupItem { Id = 3, Name = "Currently lent" },
    };
    
    public ObservableCollection<LookupItem> SortOptions { get; } = new()
    {
        new LookupItem { Id = 0, Name = "Added" },
        new LookupItem { Id = 1, Name = "Title" },
        new LookupItem { Id = 2, Name = "Author" },
        new LookupItem { Id = 3, Name = "Year" },
        new LookupItem { Id = 4, Name = "Shelf" },
    };

    [ObservableProperty]
    private LookupItem? _selectedSort;

    [ObservableProperty]
    private LookupItem? _selectedStatusFilter;

    private bool _suppressFilterReload;

    [ObservableProperty] private string _searchText = "";
    
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCategoryCommand))]
    private string _categoryQuery = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveCategoryCommand))]
    private string? _selectedCategory;
    
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemovePictureCommand), nameof(SetPrimaryPictureCommand))]
    [NotifyPropertyChangedFor(nameof(CanCrop))]
    private PictureItem? _selectedPicture;
    
    

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditCommand), nameof(DeleteCommand))]
    [NotifyPropertyChangedFor(nameof(HasSelectedBook))]
    private BookListItem? _selectedBook;

    public bool HasSelectedBook => SelectedBook is not null;
    
    // ---- selection preview (read-only detail pane) ----
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    private BookPreview? _preview;
    public bool HasPreview => Preview is not null;

    public ObservableCollection<PictureItem> PreviewPictures { get; } = new();
    public bool HasMultiplePreviewPictures => PreviewPictures.Count > 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreviewImage))]
    private PictureItem? _previewMainPicture;
    public bool HasPreviewImage => PreviewMainPicture?.Image is not null;

    private CancellationTokenSource? _previewCts;
    private readonly Dictionary<long, string> _langNames = new();
    
    
    [ObservableProperty] private bool _isFormOpen;
    
    [ObservableProperty] private bool _isShelfView;

    private LookupItem? _sortBeforeShelf;

    partial void OnIsShelfViewChanged(bool value)
    {
        if (value)
        {
            _sortBeforeShelf = SelectedSort;
            var shelfOpt = SortOptions.FirstOrDefault(o => o.Id == 4);   // "Shelf"
            if (shelfOpt is not null && !ReferenceEquals(SelectedSort, shelfOpt))
                SelectedSort = shelfOpt;
        }
        else if (_sortBeforeShelf is not null)
        {
            SelectedSort = _sortBeforeShelf;
        }
    }

    // ---- book fields ----
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _newTitle = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private Language? _selectedLanguage;

    [ObservableProperty] private Language? _selectedOriginalLanguage;
    [ObservableProperty] private string    _originalTitle     = "";
    [ObservableProperty] private string    _yearPublishedText = "";
    [ObservableProperty] private string    _yearCopyrightText = "";
    [ObservableProperty] private string    _edition           = "";
    [ObservableProperty] private string    _originalEdition   = "";
    [ObservableProperty] private string    _isbn13            = "";
    [ObservableProperty] private string    _isbn10            = "";
    [ObservableProperty] private Cover?    _selectedCover;
    [ObservableProperty] private string    _pagesText         = "";
    [ObservableProperty] private string    _description       = "";
    [ObservableProperty] private string    _shelf             = "";
    [ObservableProperty] private bool      _isRead;
    [ObservableProperty] private string    _notes             = "";
    [ObservableProperty] private string       _publisherText = "";
    [ObservableProperty] private LookupItem?  _selectedPublisher;
    [ObservableProperty] private string       _cityText = "";
    [ObservableProperty] private LookupItem?  _selectedCity;
    [ObservableProperty] private string       _priceText = "";
    [ObservableProperty] private DateTime?    _dateOfBuy;
    [ObservableProperty] private DateTime?    _dateOfLastRead;

    // ---- contributor add-row ----
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddContributorCommand))]
    private string _personQuery = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddContributorCommand))]
    private PersonSuggestion? _selectedPersonSuggestion;

    [ObservableProperty] private string _newPersonFirst = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddContributorCommand))]
    private LookupItem? _selectedRole;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveContributorCommand))]
    private ContributorEntry? _selectedContributor;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveButtonText), nameof(IsEditing), nameof(ShowImagePanel))]
    private long? _editingBookId;
    
    private DispatcherTimer? _pendingTimer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPending))]
    private int _pendingCount;

    public bool HasPending => PendingCount > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPocket))]
    private int _pocketCount;

    public bool HasPocket => PocketCount > 0;
    
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetViewCommand))]
    private bool _sortIsActive;

    public void RefreshCounts()
    {
        PendingCount = _captures.CountPendingWithIsbn();   // Captures tray = ISBN'd only
        PocketCount  = _captures.CountPocket();            // Pocket = loose photos
    }
    
    public bool CanCrop => SelectedPicture is { Id: > 0 };   // committed pictures only (v1)

    public void ReloadPictures()
    {
        if (EditingBookId is { } id) LoadPictures(id);
    }

    public void RefreshPendingCount() => PendingCount = _captures.CountPending();

    public string SaveButtonText => EditingBookId is null ? "Add" : "Save";
    public bool   IsEditing      => EditingBookId is not null;
    public bool   IsFromCapture  => _pendingCaptureId is not null;

    // The Images panel shows when editing a saved book or filling a capture (to preview its covers).
    public bool   ShowImagePanel => IsEditing || _pendingCaptureId is not null;

    public MainViewModel() { }

    public MainViewModel(BookRepository books, LookupRepository lookups, PersonRepository people,
        ExcelImporter importer, BibliotekaNarodowaClient bn, GoogleBooksClient google, OpenLibraryClient openLibrary, 
        PictureRepository pictures, SettingsStore settings, BorrowerRepository borrowers, LoanRepository loans,
        PendingCaptureRepository captures)
    {
        _books    = books;
        _lookups  = lookups;
        _people   = people;
        _importer = importer;
        _bn       = bn;
        _google   = google;
        _ol       = openLibrary;
        _pictures = pictures;
        _settings = settings;
        _borrowers = borrowers;
        _loans     = loans;
        _captures  = captures;

        ReloadFromDatabase();
        _pendingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _pendingTimer.Tick += (_, _) => RefreshCounts();
        _pendingTimer.Start();
    }

    partial void OnSearchTextChanged(string value)
    {
        _ = value;          // name fixed by the MVVM source generator; value itself unused
        ReloadBooks();
        ResetViewCommand.NotifyCanExecuteChanged();
    }

    public async Task<IEnumerable<object>> SearchPeopleAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<object>();
        return await Task.Run(() => _people.Search(text), ct);
    }

    public async Task<IEnumerable<object>> SearchPublishersAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<object>();
        return await Task.Run(() => _lookups.SearchPublishers(text), ct);
    }
    
    public async Task<IEnumerable<object>> SearchCategoriesAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<object>();
        return await Task.Run(() => _lookups.SearchCategories(text), ct);
    }

    public async Task<IEnumerable<object>> SearchCitiesAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<object>();
        return await Task.Run(() => _lookups.SearchCities(text), ct);
    }
    
    public Task ExportToExcelAsync(string path)
        => Task.Run(() =>
        {
            var rows = _books.GetExportRows();
            ExcelExporter.Export(rows, path);
        });
    
    public Task<ImportResult> ImportAddAsync(string sourcePath, string resultPath)
        => Task.Run(() => _importer.ImportAdd(sourcePath, resultPath));

    /// <summary>Re-read everything from the (possibly just-switched) database: lookups,
    /// category filters, the book list and counts. Resets search/filters/sort and closes
    /// the edit form, leaving the same clean state as a fresh launch.</summary>
    public void ReloadFromDatabase()
    {
        _suppressFilterReload = true;

        Languages.Clear(); _langNames.Clear();
        foreach (var lang in _lookups.GetLanguages()) Languages.Add(lang);
        foreach (var lang in Languages) _langNames[lang.Id] = lang.Name;

        Covers.Clear();
        foreach (var cov in _lookups.GetCovers()) Covers.Add(cov);

        Roles.Clear();
        foreach (var role in _lookups.GetRoles()) Roles.Add(role);

        ResetForm();

        _searchText = "";                         OnPropertyChanged(nameof(SearchText));
        _selectedCategoryFilter = null;           // LoadCategoryFilters reselects "All categories"
        _selectedStatusFilter = StatusFilters[0]; OnPropertyChanged(nameof(SelectedStatusFilter));
        _selectedSort = SortOptions[0];           OnPropertyChanged(nameof(SelectedSort));

        _suppressFilterReload = false;

        LoadCategoryFilters();                    // rebuilds CategoryFilters, selects "All categories"
        ReloadBooks();
        BooksView = CreateBooksView();            // fresh view -> Id sort, cleared header arrows
        SortIsActive = false;
        RefreshCounts();
    }

    public void Refresh()
    {
        LoadCategoryFilters();
        ReloadBooks();
    }
    
    public Task<ImportResult> ImportReplaceAsync(string sourcePath, string resultPath)
        => Task.Run(() => _importer.ImportReplace(sourcePath, resultPath));

    public int TotalBookCount() => _books.Count();
    
    private int _totalBooks;
    public string BookCountText
    {
        get
        {
            var shown = Books.Count;
            if (shown == _totalBooks)
                return $"{_totalBooks:N0} {(_totalBooks == 1 ? "book" : "books")}";
            return $"{shown:N0} of {_totalBooks:N0} books";
        }
    }
    
    public bool IsBookListEmpty => Books.Count == 0;

    public Task<BnLookup> SearchBnAsync()
        => SearchSourceAsync(_bn,
            (c, ct) => _bn.SearchByIsbnAsync(c, ct),
            (t, a, ct) => _bn.SearchByTitleAsync(t, a, ct),
            unavailable: "BN lookup is unavailable.",
            nothing:     "Type a title or an ISBN first, then run BN lookup.",
            errorPrefix: "BN lookup failed");

    public Task<BnLookup> SearchGoogleAsync()
        => SearchSourceAsync(_google,
            (c, ct) => _google.SearchByIsbnAsync(c, ct),
            (t, a, ct) => _google.SearchByTitleAsync(t, a, ct),
            unavailable: "Google Books lookup is unavailable.",
            nothing:     "Type a title or an ISBN first, then run Google lookup.",
            errorPrefix: "Google Books lookup failed");

    public Task<BnLookup> SearchOpenLibraryAsync()
        => SearchSourceAsync(_ol,
            (c, ct) => _ol.SearchByIsbnAsync(c, ct),
            byTitle: null,                                 // Open Library lookup is ISBN-only
            unavailable: "Open Library lookup is unavailable.",
            nothing:     "Open Library lookup is by ISBN — enter an ISBN first.",
            errorPrefix: "Open Library lookup failed");

    // Shared lookup body. The two ISBN-or-title funcs are the only thing that differs
    // per source; the source object is passed only so a null (design-time VM) degrades
    // gracefully to "unavailable" instead of throwing.
    private async Task<BnLookup> SearchSourceAsync(
        object? source,
        Func<string, CancellationToken, Task<IReadOnlyList<BnResult>>> byIsbn,
        Func<string, string?, CancellationToken, Task<IReadOnlyList<BnResult>>>? byTitle,
        string unavailable, string nothing, string errorPrefix)
    {
        if (source is null)
            return new BnLookup(BnLookupKind.Error, Array.Empty<BnResult>(), unavailable);

        var title = NullIfBlank(NewTitle);

        // Try every ISBN form we hold: the 13, the 10, and each one's converted alternate.
        // Some sources index a book under only one form, so a derived alternate hits where the original misses.
        var isbnCandidates =
            new[] { NullIfBlank(Isbn13), NullIfBlank(Isbn10) }
                .Where(s => s is not null)
                .SelectMany(s => new[] { s, Isbn.To13(s), Isbn.To10(s) })
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct()
                .ToList();

        if (isbnCandidates.Count == 0 && (title is null || byTitle is null))
            return new BnLookup(BnLookupKind.NothingToSearch, Array.Empty<BnResult>(), nothing);

        try
        {
            if (isbnCandidates.Count > 0)
            {
                foreach (var cand in isbnCandidates)
                {
                    var hits = await byIsbn(cand!, default);
                    if (hits.Count > 0)
                        return new BnLookup(BnLookupKind.Results, Dedupe(hits), null);
                }
                return new BnLookup(BnLookupKind.Results, Array.Empty<BnResult>(), null);   // tried every form, no match
            }

            var titleHits = await byTitle!(title!, FirstAuthorLastName(), default);
            return new BnLookup(BnLookupKind.Results, Dedupe(titleHits), null);
        }
        catch (Exception ex)
        {
            return new BnLookup(BnLookupKind.Error, Array.Empty<BnResult>(), $"{errorPrefix}: {ex.Message}");
        }
    }
    
    /// <summary>If a book with the same ISBN (compared as canonical ISBN-13) is already in the
    /// catalog, returns its title; otherwise null. Used to warn before adding a possible duplicate.</summary>
    public string? FindCatalogDuplicate(string? rawIsbn)
    {
        var key = Isbn.To13(rawIsbn);
        if (key is null) return null;
        foreach (var d in _books.GetDedupKeys())
            if ((Isbn.To13(d.Isbn13) ?? Isbn.To13(d.Isbn10)) == key)
                return d.Title;
        return null;
    }

    /// <summary>Duplicate check against whatever ISBN is currently in the form (13 preferred, else 10).</summary>
    public string? FindCurrentFormDuplicate()
        => FindCatalogDuplicate(NullIfBlank(Isbn13) ?? NullIfBlank(Isbn10));

    public void AddPicture(string sourceFile)
    {
        if (EditingBookId is not { } bookId) return;
        _pictures.Add(bookId, sourceFile, makePrimary: Pictures.Count == 0);   // first image becomes primary
        LoadPictures(bookId);
    }
    
    public void Lend(long bookId, LendRequest req)
    {
        var borrowerId = req.BorrowerId ?? _borrowers.ResolveOrCreate(req.FirstName!, req.LastName!);
        _loans.Lend(bookId, borrowerId, req.DateLent.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), req.Notes);
        ReloadBooks();
    }

    public void Return(long bookId, ReturnRequest req)
    {
        _loans.Return(bookId, req.DateReturned.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), req.Notes);
        ReloadBooks();
    }
    
    // ReSharper disable once UnusedParameter.Local
    partial void OnSelectedCategoryFilterChanged(LookupItem? value)
    {
        if (_suppressFilterReload) return;
        _ = value;
        ReloadBooks();
        ResetViewCommand.NotifyCanExecuteChanged();
    }
    
    partial void OnSelectedStatusFilterChanged(LookupItem? value)
    {
        if (_suppressFilterReload) return;
        ReloadBooks();
        ResetViewCommand.NotifyCanExecuteChanged();
    }

    private void LoadCategoryFilters()
    {
        var keepId = SelectedCategoryFilter?.Id ?? 0;          // preserve the user's pick across rebuilds
        _suppressFilterReload = true;
        CategoryFilters.Clear();
        CategoryFilters.Add(new LookupItem { Id = 0, Name = "All categories" });   // sentinel (no real category has Id 0)
        foreach (var c in _lookups.AllCategories())
            CategoryFilters.Add(c);
        SelectedCategoryFilter = CategoryFilters.FirstOrDefault(c => c.Id == keepId) ?? CategoryFilters[0];
        _suppressFilterReload = false;
    }
    
    [RelayCommand(CanExecute = nameof(CanResetView))]
    private void ResetView()
    {
        SearchText = "";                                                  // clears search (fires ReloadBooks)
        SelectedCategoryFilter = CategoryFilters.FirstOrDefault(c => c.Id == 0)
                                 ?? CategoryFilters.FirstOrDefault();     // back to "All categories"
        SelectedStatusFilter   = StatusFilters.FirstOrDefault(s => s.Id == 0)   ?? StatusFilters.FirstOrDefault();
        _selectedSort = SortOptions[0];
        OnPropertyChanged(nameof(SelectedSort));
        BooksView = CreateBooksView();                                    // fresh view → Id sort + cleared header arrows
        SortIsActive = false;
    }

    private bool CanResetView()
        => !string.IsNullOrEmpty(SearchText)
           || SelectedCategoryFilter is { Id: > 0 }
           || SelectedStatusFilter is { Id: > 0 }
           || SortIsActive;

    private bool CanModifyPicture()
        => SelectedPicture is not null
           && (SelectedPicture.Id > 0 || _pendingCaptureId is not null);   // committed row, or staged preview

    [RelayCommand(CanExecute = nameof(CanModifyPicture))]
    private void RemovePicture()
    {
        if (SelectedPicture is null) return;

        // Staged capture preview (no DB row yet): prune the in-memory list and rebuild. The rebuilt
        // preview flags index 0 as primary, so deleting the primary auto-promotes the next one.
        if (SelectedPicture.Id == 0 && _pendingCaptureId is not null)
        {
            var idx = Pictures.IndexOf(SelectedPicture);
            if (idx < 0 || idx >= _pendingCapturePhotos.Count) return;
            var list = _pendingCapturePhotos.ToList();
            list.RemoveAt(idx);
            _pendingCapturePhotos = list;
            LoadStagedPreview();
            return;
        }

        // Committed picture: delete the row, then make sure a cover remains.
        if (EditingBookId is not { } bookId) return;
        _pictures.Delete(SelectedPicture.Id);
        EnsurePrimary(bookId);
        LoadPictures(bookId);
    }

    private bool CanSetPrimary()
        => SelectedPicture is { IsPrimary: false }
           && (SelectedPicture.Id > 0 || _pendingCaptureId is not null);

    [RelayCommand(CanExecute = nameof(CanSetPrimary))]
    private void SetPrimaryPicture()
    {
        if (SelectedPicture is null) return;

        // Staged: move the chosen photo to the front (index 0 = primary at commit).
        if (SelectedPicture.Id == 0 && _pendingCaptureId is not null)
        {
            var idx = Pictures.IndexOf(SelectedPicture);
            if (idx <= 0 || idx >= _pendingCapturePhotos.Count) return;   // already primary or invalid
            var list = _pendingCapturePhotos.ToList();
            var moved = list[idx];
            list.RemoveAt(idx);
            list.Insert(0, moved);
            _pendingCapturePhotos = list;
            LoadStagedPreview();
            return;
        }

        // Committed:
        if (EditingBookId is not { } bookId) return;
        _pictures.SetPrimary(bookId, SelectedPicture.Id);
        LoadPictures(bookId);
    }

    // Keep a cover present: if a book has pictures but none is flagged primary (e.g. the primary was
    // just deleted), promote the first one in view.
    private void EnsurePrimary(long bookId)
    {
        var pics = _pictures.GetForBook(bookId);
        if (pics.Count > 0 && !pics.Any(p => p.IsPrimary))
            _pictures.SetPrimary(bookId, pics[0].Id);
    }

    private void LoadPictures(long bookId)
    {
        foreach (var p in Pictures) p.Image?.Dispose();
        Pictures.Clear();
        SelectedPicture = null;

        foreach (var pic in _pictures.GetForBook(bookId))
            Pictures.Add(new PictureItem
            {
                Id         = pic.Id,
                IsPrimary  = pic.IsPrimary,
                Image      = LoadThumb(_pictures.AbsolutePath(pic)),
                SourcePath = _pictures.AbsolutePath(pic),
            });
    }

    private static Bitmap? LoadThumb(string absPath)
    {
        try
        {
            using var fs = ImageOrient.OrientedStream(absPath);
            return Bitmap.DecodeToWidth(fs, 240);
        }
        catch { return null; }
    }
    
    // ---- preview pipeline -------------------------------------------------

    partial void OnSelectedBookChanged(BookListItem? value)
    {
        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = null;

        if (value is null) { ClearPreview(); return; }

        var cts = _previewCts = new CancellationTokenSource();
        _ = LoadPreviewAsync(value, _settings.Current.PreviewDelayMs, cts.Token);
    }

    private async Task LoadPreviewAsync(BookListItem item, int delayMs, CancellationToken ct)
    {
        try
        {
            if (delayMs > 0) await Task.Delay(delayMs, ct);

            var (preview, pics) = await Task.Run(() => AssemblePreview(item, ct), ct);

            if (ct.IsCancellationRequested)                 // superseded after the work finished
            {
                foreach (var p in pics) p.Image?.Dispose();
                return;
            }
            ApplyPreview(preview, pics);
        }
        catch (OperationCanceledException) { /* superseded by a newer selection */ }
    }

    private (BookPreview preview, List<PictureItem> pics) AssemblePreview(BookListItem item, CancellationToken ct)
    {
        var b = _books.GetById(item.Id);
        var pics = new List<PictureItem>();
        if (b is null)
            return (new BookPreview { Title = item.Title, PlaceholderColor = CoverColor.For(null, item.Title) }, pics);

        ct.ThrowIfCancellationRequested();
        var contributors = _books.GetContributors(item.Id);
        var byline  = string.Join(", ", contributors.Select(c => $"{c.PersonName} ({c.RoleName})"));
        var authors = string.Join(", ", _books.GetAuthorsNatural(item.Id));

        ct.ThrowIfCancellationRequested();
        var cats = _books.GetCategories(item.Id);
        
        var mainCat = cats.FirstOrDefault();      // alphabetical first = "main" for now

        ct.ThrowIfCancellationRequested();
        foreach (var p in _pictures.GetForBook(item.Id))
        {
            ct.ThrowIfCancellationRequested();
            var abs = _pictures.AbsolutePath(p);
            pics.Add(new PictureItem { Id = p.Id, IsPrimary = p.IsPrimary, Image = LoadThumb(abs), SourcePath = abs });
        }

        var preview = new BookPreview
        {
            Title         = b.Title,
            Byline        = NullIfBlank(byline),
            PublisherLine = JoinDot(b.PublisherName, b.CityName, b.YearPublished?.ToString()),
            Language      = _langNames.TryGetValue(b.LanguageId, out var ln) ? ln : null,
            Pages         = b.Pages is { } pg ? $"{pg} pages" : null,
            Isbn          = b.Isbn13 ?? b.Isbn10,
            Categories    = cats.Count > 0 ? string.Join(", ", cats) : null,
            Shelf         = b.Shelf,
            LentTo        = NullIfBlank(item.LentTo),
            ReadStatus    = b.IsRead ? "Read" : "Unread",
            Description   = b.Description,
            Notes         = b.Notes,
            PlaceholderColor = CoverColor.For(mainCat, b.Title),
            Authors = NullIfBlank(authors),
        };
        return (preview, pics);
    }

    private void ApplyPreview(BookPreview preview, List<PictureItem> pics)
    {
        DisposePreviewPictures();
        PreviewPictures.Clear();
        foreach (var p in pics) PreviewPictures.Add(p);
        OnPropertyChanged(nameof(HasMultiplePreviewPictures));
        PreviewMainPicture = PreviewPictures.FirstOrDefault();   // primary is first (GetForBook orders primary-first)
        Preview = preview;
    }

    private void ClearPreview()
    {
        DisposePreviewPictures();
        PreviewPictures.Clear();
        OnPropertyChanged(nameof(HasMultiplePreviewPictures));
        PreviewMainPicture = null;
        Preview = null;
    }

    private void DisposePreviewPictures()
    {
        foreach (var p in PreviewPictures) p.Image?.Dispose();
    }

    private static string? JoinDot(params string?[] parts)
    {
        var joined = string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        return joined.Length == 0 ? null : joined;
    }
    // end of --preview pipeline -----------------------------------------------------
    
    
    // collapse near-identical records (networks returns one per holding library)
    private static IReadOnlyList<BnResult> Dedupe(IReadOnlyList<BnResult> items)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var outp = new List<BnResult>();
        foreach (var r in items)
        {
            var key = r.Isbn13 ?? r.Isbn10 ?? $"{r.Title}|{r.Year}";
            if (seen.Add(key)) outp.Add(r);
        }
        return outp;
    }
    
    private string? FirstAuthorLastName()
        => Contributors.FirstOrDefault(c => c.RoleName == "Author")?.LastName ?? NullIfBlank(PersonQuery);

    /// <summary>
    /// Apply a primary result (overwrite), then top up any still-blank fields from
    /// Open Library by ISBN. Used by the Google path; a missing ISBN or a miss just
    /// no-ops, and an Open Library failure never disturbs what the primary already filled.
    /// </summary>
    public async Task ApplyBnResultAsync(BnResult r, CancellationToken ct = default)
    {
        ApplyBnResult(r);   // primary source — overwrite

        var isbn = NullIfBlank(Isbn13) ?? NullIfBlank(Isbn10);
        if (isbn is null) return;

        try
        {
            var ol = await _ol.SearchByIsbnAsync(isbn, ct);
            if (ol.Count > 0) ApplyBnResult(ol[0], onlyFillBlanks: true);   // fill only what's still blank
        }
        catch
        {
            // best-effort gap-fill — ignore network / parse errors
        }
    }

    public void ApplyBnResult(BnResult r, bool onlyFillBlanks = false)
    {
        if (!string.IsNullOrWhiteSpace(r.Title) && Fillable(NewTitle)) NewTitle = r.Title;
        if (r.OriginalTitle is not null && Fillable(OriginalTitle))         OriginalTitle = r.OriginalTitle;
        if (r.Year is not null && Fillable(YearPublishedText))                  YearPublishedText = r.Year.Value.ToString();
        if (r.Pages is not null && Fillable(PagesText))                 PagesText = r.Pages.Value.ToString();
        if (r.Edition is not null && Fillable(Edition))               Edition = r.Edition;
        if (r.Isbn13 is not null && Fillable(Isbn13))                Isbn13 = r.Isbn13;
        if (r.Isbn10 is not null && Fillable(Isbn10))                Isbn10 = r.Isbn10;

        if (r.Publisher is not null && Fillable(PublisherText)) { PublisherText = r.Publisher; SelectedPublisher = null; }  // null → resolve-or-create on save
        if (r.City is not null && Fillable(CityText))      { CityText      = r.City;      SelectedCity      = null; }

        if (r.LanguageName is not null && (!onlyFillBlanks || SelectedLanguage is null))
        {
            var lang = Languages.FirstOrDefault(l => l.Name == r.LanguageName);
            if (lang is not null) SelectedLanguage = lang;
        }
        if (r.OriginalLanguageName is not null && (!onlyFillBlanks || SelectedOriginalLanguage is null))
        {
            var ol = Languages.FirstOrDefault(l => l.Name == r.OriginalLanguageName);
            if (ol is not null) SelectedOriginalLanguage = ol;
        }

        // Append BN contributors not already staged (dedup by display + role).
        if (!onlyFillBlanks || Contributors.Count == 0)
        foreach (var c in r.Contributors)
        {
            var role = Roles.FirstOrDefault(x => x.Name == c.RoleName) ?? Roles.FirstOrDefault(x => x.Name == "Author");
            if (role is null) continue;

            var exists = Contributors.Any(e =>
                string.Equals(e.PersonName, c.Display, StringComparison.OrdinalIgnoreCase) && e.RoleId == role.Id);
            if (exists) continue;

            Contributors.Add(new ContributorEntry
            {
                FirstName  = c.FirstName,
                LastName   = c.LastName,
                RoleId     = role.Id,
                PersonName = c.Display,
                RoleName   = role.Name,
            });
        }
        
        // Pre-stage the genres into the existing Categories collection, deduping case-insensitively against whatever's already there
        if (!onlyFillBlanks || Categories.Count == 0)
        foreach (var cat in r.Categories)
            if (!Categories.Any(c => string.Equals(c, cat, StringComparison.OrdinalIgnoreCase)))
                Categories.Add(cat);

        // may we write into this text field? (gap-fill writes blanks only)
        bool Fillable(string current) => !onlyFillBlanks || string.IsNullOrWhiteSpace(current);
    }

    [RelayCommand]
    private void NewBook()
    {
        ResetForm();
        IsFormOpen = true;
    }

    /// <summary>Open the form pre-filled from a phone capture; Save() will mark that capture committed.</summary>
    public void LoadFromCapture(PendingCapture capture)
    {
        ResetForm();                                  // clears the form and any prior capture link
        _pendingCaptureId     = capture.CaptureId;
        _pendingCapturePhotos = capture.Photos;       // attached to the book on Save
        Isbn13 = capture.Isbn13 ?? "";
        Isbn10 = Isbn.To10(capture.Isbn13) ?? "";     // derive the 10 form when the 13 is 978-prefixed
        Shelf  = capture.Shelf ?? "";
        LoadStagedPreview();                          // show the captured covers while filling the form
        OnPropertyChanged(nameof(ShowImagePanel));    // reveal the panel (new book, so IsEditing is false)
        IsFormOpen = true;
    }

    /// <summary>Drop a capture from the pending queue without committing it; the repository also removes its staged photos.</summary>
    public void DiscardCapture(PendingCapture capture)
    {
        _captures.Discard(capture.CaptureId);
        RefreshCounts();
    }

    /// <summary>Add a capture's photos to the existing catalog book with the same ISBN (no new record
    /// is created), then discard the capture. Returns false if no matching book was found.</summary>
    public bool AttachCaptureToExistingBook(PendingCapture capture)
    {
        var bookId = _books.FindIdByIsbn(capture.Isbn13);
        if (bookId is null) return false;

        var existing = _pictures.GetForBook(bookId.Value).Count;   // don't steal the book's cover
        for (var i = 0; i < capture.Photos.Count; i++)
        {
            var abs = _pictures.AbsolutePath(capture.Photos[i]);
            if (!File.Exists(abs)) continue;                                  // tolerate a missing staged file
            _pictures.Add(bookId.Value, abs, makePrimary: existing == 0 && i == 0);
        }

        _captures.Discard(capture.CaptureId);                                 // staged files cleaned up by the repo
        if (EditingBookId == bookId) ReloadPictures();                        // refresh if that book is open
        RefreshCounts();
        return true;
    }

    // Copy a capture's staged photos onto the freshly-saved book (first = primary), reusing the
    // normal picture pipeline so they land in pictures/ with BookPictures rows like any other image.
    private void AttachStagedPhotos(long bookId, IReadOnlyList<string> relPaths)
    {
        for (var i = 0; i < relPaths.Count; i++)
        {
            var abs = _pictures.AbsolutePath(relPaths[i]);
            if (!File.Exists(abs)) continue;                            // tolerate a missing staged file
            _pictures.Add(bookId, abs, makePrimary: i == 0);            // first staged photo is the cover
        }
    }

    // Show the capture's staged covers in the picture panel while the form is filled in. They carry
    // Id = 0 (no BookPictures row yet), so Set-primary/Remove stay disabled; they attach on Save.
    private void LoadStagedPreview()
    {
        foreach (var p in Pictures) p.Image?.Dispose();
        Pictures.Clear();
        SelectedPicture = null;

        for (var i = 0; i < _pendingCapturePhotos.Count; i++)
            Pictures.Add(new PictureItem
            {
                Id         = 0,                 // staged preview, not a saved BookPictures row
                IsPrimary  = i == 0,            // first staged photo is the cover
                Image      = LoadThumb(_pictures.AbsolutePath(_pendingCapturePhotos[i])),
                SourcePath = _pictures.AbsolutePath(_pendingCapturePhotos[i]),
            });
    }

    private bool CanEditOrDelete() => SelectedBook is not null;

    [RelayCommand(CanExecute = nameof(CanEditOrDelete))]
    private void Edit()
    {
        if (SelectedBook is null) return;
        var b = _books.GetById(SelectedBook.Id);
        if (b is null) return;

        EditingBookId            = b.Id;
        NewTitle                 = b.Title;
        OriginalTitle            = b.OriginalTitle ?? "";
        SelectedLanguage         = Languages.FirstOrDefault(l => l.Id == b.LanguageId);
        SelectedOriginalLanguage = b.OriginalLanguageId is null ? null : Languages.FirstOrDefault(l => l.Id == b.OriginalLanguageId);
        YearPublishedText        = b.YearPublished?.ToString() ?? "";
        YearCopyrightText        = b.YearCopyright?.ToString() ?? "";
        Edition                  = b.Edition ?? "";
        OriginalEdition          = b.OriginalEdition ?? "";
        Isbn13                   = b.Isbn13 ?? "";
        Isbn10                   = b.Isbn10 ?? "";
        SelectedCover            = b.CoverId is null ? null : Covers.FirstOrDefault(c => c.Id == b.CoverId);
        PagesText                = b.Pages?.ToString() ?? "";
        Description              = b.Description ?? "";
        Shelf                    = b.Shelf ?? "";
        IsRead                   = b.IsRead;
        Notes                    = b.Notes ?? "";
        PublisherText            = b.PublisherName ?? "";
        SelectedPublisher        = b.PublisherId is null ? null : new LookupItem { Id = b.PublisherId.Value, Name = b.PublisherName ?? "" };
        CityText                 = b.CityName ?? "";
        SelectedCity             = b.CityId is null ? null : new LookupItem { Id = b.CityId.Value, Name = b.CityName ?? "" };
        PriceText                = GroszeToText(b.InitialPrice);
        DateOfBuy                = IsoToDate(b.DateOfBuy);
        DateOfLastRead           = IsoToDate(b.DateOfLastRead);
        
        LoadPictures(b.Id);

        Contributors.Clear();
        foreach (var c in _books.GetContributors(b.Id))
            Contributors.Add(new ContributorEntry
            {
                PersonId = c.PersonId, RoleId = c.RoleId, PersonName = c.PersonName, RoleName = c.RoleName
            });
        
        Categories.Clear();
        foreach (var cat in _books.GetCategories(b.Id))
            Categories.Add(cat);

        IsFormOpen = true;
    }

    [RelayCommand(CanExecute = nameof(CanEditOrDelete))]
    private void Delete()
    {
        if (SelectedBook is null) return;
        var id   = SelectedBook.Id;
        var pics = _pictures.GetForBook(id);          // capture before Delete cascades the rows away
        _books.Delete(id);
        _pictures.DiscardFiles(pics, _settings.Current.DeletePictureFilesOnBookDelete);
        if (EditingBookId == id) CloseForm();
        ReloadBooks();
    }

    private bool CanAddContributor() =>
        SelectedRole is not null &&
        (SelectedPersonSuggestion is not null || !string.IsNullOrWhiteSpace(PersonQuery));

    [RelayCommand(CanExecute = nameof(CanAddContributor))]
    private void AddContributor()
    {
        var role = SelectedRole!;
        var picked = SelectedPersonSuggestion;
        if (picked is not null && !string.Equals(PersonQuery.Trim(), picked.Display, StringComparison.Ordinal))
            picked = null;

        var entry = picked is not null
            ? new ContributorEntry { PersonId = picked.Id, RoleId = role.Id, PersonName = picked.Display, RoleName = role.Name }
            : new ContributorEntry
              {
                  FirstName  = NullIfBlank(NewPersonFirst),
                  LastName   = PersonQuery.Trim(),
                  RoleId     = role.Id,
                  PersonName = BuildName(NewPersonFirst, PersonQuery),
                  RoleName   = role.Name
              };

        Contributors.Add(entry);

        PersonQuery = "";
        NewPersonFirst = "";
        SelectedPersonSuggestion = null;
    }

    private bool CanRemoveContributor() => SelectedContributor is not null;

    [RelayCommand(CanExecute = nameof(CanRemoveContributor))]
    private void RemoveContributor()
    {
        if (SelectedContributor is not null)
            Contributors.Remove(SelectedContributor);
    }
    
    private bool CanAddCategory() => !string.IsNullOrWhiteSpace(CategoryQuery);

    [RelayCommand(CanExecute = nameof(CanAddCategory))]
    private void AddCategory()
    {
        var name = CategoryQuery.Trim();
        if (name.Length > 0 && !Categories.Any(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase)))
            Categories.Add(name);
        CategoryQuery = "";
    }

    private bool CanRemoveCategory() => SelectedCategory is not null;

    [RelayCommand(CanExecute = nameof(CanRemoveCategory))]
    private void RemoveCategory()
    {
        if (SelectedCategory is not null) Categories.Remove(SelectedCategory);
    }

    public bool CanSave =>
        !string.IsNullOrWhiteSpace(NewTitle) && SelectedLanguage is not null;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        var detail = new BookDetail
        {
            Id                 = EditingBookId ?? 0,
            Title              = NewTitle.Trim(),
            OriginalTitle      = NullIfBlank(OriginalTitle),
            LanguageId         = SelectedLanguage!.Id,
            OriginalLanguageId = SelectedOriginalLanguage?.Id,
            YearPublished      = ParseIntOrNull(YearPublishedText),
            YearCopyright      = ParseIntOrNull(YearCopyrightText),
            Edition            = NullIfBlank(Edition),
            OriginalEdition    = NullIfBlank(OriginalEdition),
            Isbn13             = NullIfBlank(Isbn13),
            Isbn10             = NullIfBlank(Isbn10),
            CoverId            = SelectedCover?.Id,
            Pages              = ParseIntOrNull(PagesText),
            Description        = NullIfBlank(Description),
            Shelf              = NullIfBlank(Shelf),
            IsRead             = IsRead,
            Notes              = NullIfBlank(Notes),
            PublisherId        = ResolveLookup(SelectedPublisher, PublisherText, _lookups.ResolveOrCreatePublisher),
            CityId             = ResolveLookup(SelectedCity,      CityText,      _lookups.ResolveOrCreateCity),
            InitialPrice       = ParsePriceToGrosze(PriceText),
            DateOfBuy          = DateToIso(DateOfBuy),
            DateOfLastRead     = DateToIso(DateOfLastRead),
        };

        var inputs = Contributors.Select(c => c.ToInput()).ToList();
        var cats   = Categories.ToList();

        if (EditingBookId is not null)
        {
            _books.Update(detail, inputs, cats);
        }
        else
        {
            var newId = _books.Insert(detail, inputs, cats);
            if (_pendingCaptureId is { } capId)       // committed straight from a phone capture
            {
                AttachStagedPhotos(newId, _pendingCapturePhotos);   // covers -> pictures/ + BookPictures
                _captures.MarkCommitted(capId);                     // mark committed + delete the staging copies
                RefreshCounts();
            }
        }

        CloseForm();
        ReloadBooks();
        LoadCategoryFilters();
    }

    [RelayCommand]
    private void CancelEdit() => CloseForm();

    private void CloseForm()
    {
        IsFormOpen = false;
        ResetForm();
    }

    private void ResetForm()
    {
        EditingBookId            = null;
        _pendingCaptureId        = null;
        _pendingCapturePhotos    = Array.Empty<string>();
        NewTitle                 = "";
        OriginalTitle            = "";
        SelectedLanguage         = Languages.FirstOrDefault(l => l.Name == "Polish");
        SelectedOriginalLanguage = null;
        YearPublishedText        = "";
        YearCopyrightText        = "";
        Edition                  = "";
        OriginalEdition          = "";
        Isbn13                   = "";
        Isbn10                   = "";
        SelectedCover            = null;
        PagesText                = "";
        Description              = "";
        Shelf                    = "";
        IsRead                   = false;
        Notes                    = "";
        PublisherText            = "";
        SelectedPublisher        = null;
        CityText                 = "";
        SelectedCity             = null;
        PriceText                = "";
        DateOfBuy                = null;
        DateOfLastRead           = null;

        Contributors.Clear();
        PersonQuery              = "";
        NewPersonFirst           = "";
        SelectedPersonSuggestion = null;
        SelectedContributor      = null;
        SelectedRole             = Roles.FirstOrDefault(r => r.Name == "Author");
        
        Categories.Clear();
        CategoryQuery   = "";
        SelectedCategory = null;
        
        foreach (var p in Pictures) p.Image?.Dispose();
        Pictures.Clear();
        SelectedPicture = null;
        OnPropertyChanged(nameof(ShowImagePanel));    // _pendingCaptureId / EditingBookId were cleared above
    }

    private void ReloadBooks()
    {
        var catId  = SelectedCategoryFilter is { Id: > 0 } c ? c.Id : (long?)null;
        var status = (BookStatusFilter)(int)(SelectedStatusFilter?.Id ?? 0);
        Books.Clear();
        foreach (var book in _books.GetAll(SearchText, catId, status))
            Books.Add(book);
        _totalBooks = _books.Count();
        OnPropertyChanged(nameof(BookCountText));
        OnPropertyChanged(nameof(IsBookListEmpty));
    }

    private static long? ResolveLookup(LookupItem? picked, string text, Func<string, long> resolveOrCreate)
    {
        if (picked is not null && string.Equals(text.Trim(), picked.Name, StringComparison.Ordinal))
            return picked.Id;
        return string.IsNullOrWhiteSpace(text) ? null : resolveOrCreate(text);
    }

    private static string BuildName(string? first, string last)
    {
        var l = last.Trim();
        var f = string.IsNullOrWhiteSpace(first) ? null : first.Trim();
        return f is null ? l : $"{l}, {f}";
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static int?    ParseIntOrNull(string? s) => int.TryParse(s, out var n) ? n : null;

    private static int? ParsePriceToGrosze(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var norm = s.Trim().Replace(',', '.');
        return decimal.TryParse(norm, NumberStyles.Number, CultureInfo.InvariantCulture, out var pln)
            ? (int)Math.Round(pln * 100m, MidpointRounding.AwayFromZero)
            : null;
    }

    private static string GroszeToText(int? grosze)
        => grosze is null ? "" : (grosze.Value / 100m).ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');

    private static string? DateToIso(DateTime? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static DateTime? IsoToDate(string? s)
        => DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}