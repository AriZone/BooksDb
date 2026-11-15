using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.ComponentModel;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Avalonia.Input;
using BooksDb.ViewModels;
using BooksDb.Data;
using Microsoft.Extensions.DependencyInjection;
using BooksDb.Data.Models;

namespace BooksDb.Views;

public partial class MainView : UserControl
{
    
    private MainViewModel? _vm;
    public MainView()
    {
        InitializeComponent();
        var cols = BookGrid.Columns;
        cols[0].CustomSortComparer = new BookFieldComparer(b => b.Title);    // Title
        cols[1].CustomSortComparer = new BookFieldComparer(b => b.Authors);  // Author(s)
        cols[3].CustomSortComparer = new BookFieldComparer(b => b.Language); // Language
        cols[4].CustomSortComparer = new BookFieldComparer(b => b.LentTo);   // Lent to
        // cols[2] = Year → default numeric sort, no comparer
        PersonBox.AsyncPopulator    = PopulatePeopleAsync;
        PublisherBox.AsyncPopulator = PopulatePublishersAsync;
        CityBox.AsyncPopulator      = PopulateCitiesAsync;
        CategoryBox.AsyncPopulator  = PopulateCategoriesAsync;

        DataContextChanged += (_, _) =>
        {
            if (_vm is not null) _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm = DataContext as MainViewModel;
            if (_vm is not null) _vm.PropertyChanged += OnVmPropertyChanged;
        };
        
        // keyboard ergonomics
        AddHandler(KeyDownEvent, OnViewKeyDown, RoutingStrategies.Bubble);
        BookGrid.KeyDown += OnGridKeyDown;
        BookGrid.AddHandler(TextInputEvent, OnGridTextInput, RoutingStrategies.Tunnel);
        Shelf.KeyDown += OnGridKeyDown;   // F1 = edit, Del = delete (shelf navigation lives in ShelfView)
    }
    
    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // When the edit form closes (Save/Cancel/delete-while-editing), undo any
        // splitter drag so the book list returns to the top of the window.
        if (e.PropertyName == nameof(MainViewModel.IsFormOpen) && _vm is { IsFormOpen: false })
        {
            RootGrid.RowDefinitions[1].Height = GridLength.Auto;                       // form row
            RootGrid.RowDefinitions[3].Height = new GridLength(1, GridUnitType.Star);  // list row
        }
    }


    private async Task<IEnumerable<object>> PopulatePeopleAsync(string? text, CancellationToken ct)
        => DataContext is MainViewModel vm && !string.IsNullOrWhiteSpace(text)
            ? await vm.SearchPeopleAsync(text, ct) : Array.Empty<object>();

    private async Task<IEnumerable<object>> PopulatePublishersAsync(string? text, CancellationToken ct)
        => DataContext is MainViewModel vm && !string.IsNullOrWhiteSpace(text)
            ? await vm.SearchPublishersAsync(text, ct) : Array.Empty<object>();

    private async Task<IEnumerable<object>> PopulateCitiesAsync(string? text, CancellationToken ct)
        => DataContext is MainViewModel vm && !string.IsNullOrWhiteSpace(text)
            ? await vm.SearchCitiesAsync(text, ct) : Array.Empty<object>();
    
    private async Task<IEnumerable<object>> PopulateCategoriesAsync(string? text, CancellationToken ct)
        => DataContext is MainViewModel vm && !string.IsNullOrWhiteSpace(text)
            ? await vm.SearchCategoriesAsync(text, ct) : Array.Empty<object>();

    private async void OnNewDatabaseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var start = await owner.StorageProvider.TryGetFolderFromPathAsync(Database.DefaultDirectory());
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title                  = "New database",
            SuggestedFileName      = "books.db",
            DefaultExtension       = "db",
            SuggestedStartLocation = start,
            FileTypeChoices        = new[]
            {
                new FilePickerFileType("SQLite database") { Patterns = new[] { "*.db" } }
            }
        });
        if (file is null) return;

        if (await TrySwitchDatabaseAsync(owner, vm, file.Path.LocalPath))
            await MessageDialog.InfoAsync(owner, "Database", $"Created and switched to:\n{file.Path.LocalPath}");
    }

    private async void OnOpenDatabaseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var start = await owner.StorageProvider.TryGetFolderFromPathAsync(Database.DefaultDirectory());
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title                  = "Open database",
            AllowMultiple          = false,
            SuggestedStartLocation = start,
            FileTypeFilter         = new[]
            {
                new FilePickerFileType("SQLite database") { Patterns = new[] { "*.db", "*.sqlite", "*.sqlite3" } },
                new FilePickerFileType("All files")       { Patterns = new[] { "*" } },
            }
        });
        if (files.Count == 0) return;

        var path = files[0].Path.LocalPath;
        if (await TrySwitchDatabaseAsync(owner, vm, path))
            await MessageDialog.InfoAsync(owner, "Database", $"Opened:\n{path}");
    }

    private async void OnCloseDatabaseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var db = App.Services.GetRequiredService<Database>();
        if (string.Equals(db.CurrentPath, Path.GetFullPath(Database.DefaultPath()), StringComparison.OrdinalIgnoreCase))
        {
            await MessageDialog.InfoAsync(owner, "Database", "Already using the default database.");
            return;
        }

        if (await TrySwitchDatabaseAsync(owner, vm, Database.DefaultPath()))
            await MessageDialog.InfoAsync(owner, "Database", "Switched back to the default database.");
    }

    // Repoint the Database singleton, migrate (builds the schema for a new/empty file),
    // persist the choice, drop cached covers, and reload the UI. Rolls back on failure.
    private static async Task<bool> TrySwitchDatabaseAsync(Window owner, MainViewModel vm, string path)
    {
        var db = App.Services.GetRequiredService<Database>();
        var previous = db.CurrentPath;
        try
        {
            db.SwitchTo(path);
            Migrator.EnsureUpToDate(db);
        }
        catch (Exception ex)
        {
            try { db.SwitchTo(previous); Migrator.EnsureUpToDate(db); } catch { /* previous was fine */ }
            await MessageDialog.InfoAsync(owner, "Database", $"Could not open the database:\n{ex.Message}");
            return false;
        }

        var settings = App.Services.GetRequiredService<SettingsStore>();
        var isDefault = string.Equals(db.CurrentPath, Path.GetFullPath(Database.DefaultPath()),
                                      StringComparison.OrdinalIgnoreCase);
        settings.Current.DatabasePath = isDefault ? "" : db.CurrentPath;
        settings.Save();

        App.Services.GetRequiredService<CoverCache>().Clear();
        vm.ReloadFromDatabase();
        return true;
    }

    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title             = "Export collection to Excel",
            SuggestedFileName = "books.xlsx",
            DefaultExtension  = "xlsx",
            FileTypeChoices   = new[]
            {
                new FilePickerFileType("Excel workbook") { Patterns = new[] { "*.xlsx" } }
            }
        });

        if (file is null) return;

        var path = file.Path.LocalPath;
        try
        {
            await vm.ExportToExcelAsync(path);
            await MessageDialog.InfoAsync(owner, "Export complete", $"Saved to:\n{path}");
        }
        catch (Exception ex)
        {
            await MessageDialog.InfoAsync(owner, "Export failed", ex.Message);
        }
    }

    private async void OnImportClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var source = await PickXlsxAsync(owner, "Import books from Excel");
        if (source is null) return;

        var result = ResultPathFor(source);
        try
        {
            var summary = await vm.ImportAddAsync(source, result);
            vm.Refresh();
            await MessageDialog.InfoAsync(owner, "Import complete",
                $"Imported {summary.Imported}, skipped {summary.SkippedHard + summary.SkippedSoft} " +
                $"({summary.SkippedHard} by ISBN, {summary.SkippedSoft} by title).\n\n" +
                $"Annotated copy saved to:\n{summary.ResultPath}");
        }
        catch (Exception ex)
        {
            await MessageDialog.InfoAsync(owner, "Import failed", ex.Message);
        }
    }

    private async void OnReplaceClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var source = await PickXlsxAsync(owner, "Replace collection from Excel");
        if (source is null) return;

        var confirmed = await MessageDialog.ConfirmAsync(owner, "Replace entire collection?",
            $"This permanently deletes all {vm.TotalBookCount()} book(s) and replaces them with the contents of:\n{source}\n\n" +
            "This cannot be undone. Continue?");
        if (!confirmed) return;

        var result = ResultPathFor(source);
        try
        {
            var summary = await vm.ImportReplaceAsync(source, result);
            vm.Refresh();
            await MessageDialog.InfoAsync(owner, "Replace complete",
                $"Replaced the collection with {summary.Imported} book(s).\n\nAnnotated copy saved to:\n{summary.ResultPath}");
        }
        catch (Exception ex)
        {
            await MessageDialog.InfoAsync(owner, "Replace failed", ex.Message);
        }
    }
    
    private async void OnAddPictureClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title         = "Add image",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Images")
                {
                    Patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.webp", "*.gif", "*.bmp" }
                }
            }
        });
        if (files.Count == 0) return;

        try
        {
            vm.AddPicture(files[0].Path.LocalPath);
        }
        catch (Exception ex)
        {
            await MessageDialog.InfoAsync(owner, "Add image failed", ex.Message);
        }
    }
    
    private async void OnBnLookupClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var lookup = await vm.SearchBnAsync();

        switch (lookup.Kind)
        {
            case BnLookupKind.NothingToSearch:
            case BnLookupKind.Error:
                await MessageDialog.InfoAsync(owner, "Biblioteka Narodowa", lookup.Message ?? "");
                break;

            case BnLookupKind.Results when lookup.Results.Count == 0:
                await MessageDialog.InfoAsync(owner, "Biblioteka Narodowa", "No matches found in Biblioteka Narodowa.");
                break;

            case BnLookupKind.Results when lookup.Results.Count == 1:
                vm.ApplyBnResult(lookup.Results[0]);
                await MessageDialog.InfoAsync(owner, "Biblioteka Narodowa", "Filled from Biblioteka Narodowa.");
                break;

            case BnLookupKind.Results:
                var chosen = await BnPickerDialog.PickAsync(owner, lookup.Results);
                if (chosen is not null)
                    vm.ApplyBnResult(chosen);
                break;
        }
    }
    
    private async void OnGoogleLookupClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var lookup = await vm.SearchGoogleAsync();

        switch (lookup.Kind)
        {
            case BnLookupKind.NothingToSearch:
            case BnLookupKind.Error:
                await MessageDialog.InfoAsync(owner, "Google Books", lookup.Message ?? "");
                break;

            case BnLookupKind.Results when lookup.Results.Count == 0:
                await MessageDialog.InfoAsync(owner, "Google Books", "No matches found in Google Books.");
                break;

            case BnLookupKind.Results when lookup.Results.Count == 1:
                await vm.ApplyBnResultAsync(lookup.Results[0]);
                await MessageDialog.InfoAsync(owner, "Google Books", "Filled from Google Books.");
                break;

            case BnLookupKind.Results:
                var chosen = await BnPickerDialog.PickAsync(owner, lookup.Results);
                if (chosen is not null)
                    await vm.ApplyBnResultAsync(chosen);
                break;
        }
    }

    private async void OnOpenLibraryLookupClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var lookup = await vm.SearchOpenLibraryAsync();

        switch (lookup.Kind)
        {
            case BnLookupKind.NothingToSearch:
            case BnLookupKind.Error:
                await MessageDialog.InfoAsync(owner, "Open Library", lookup.Message ?? "");
                break;

            case BnLookupKind.Results when lookup.Results.Count == 0:
                await MessageDialog.InfoAsync(owner, "Open Library", "No matches found in Open Library.");
                break;

            case BnLookupKind.Results when lookup.Results.Count == 1:
                vm.ApplyBnResult(lookup.Results[0]);
                await MessageDialog.InfoAsync(owner, "Open Library", "Filled from Open Library.");
                break;

            case BnLookupKind.Results:
                var chosen = await BnPickerDialog.PickAsync(owner, lookup.Results);
                if (chosen is not null)
                    vm.ApplyBnResult(chosen);
                break;
        }
    }
    
    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window owner)
            await SettingsDialog.ShowAsync(owner);
    }

    private async void OnCapturesClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var capture = await CapturesDialog.ShowAsync(owner);
        if (capture is null) return;

        var dupTitle = vm.FindCatalogDuplicate(capture.Isbn13);
        if (dupTitle is not null)
        {
            var choice = await MessageDialog.ChoiceAsync(owner, "Already in your catalog",
                $"A book with this ISBN is already in your catalog:\n\n\"{dupTitle}\"\n\nWhat would you like to do?",
                "Open anyway", "Discard capture", "Add photos to book");

            switch (choice)
            {
                case MessageChoice.Primary:                    // open anyway — e.g. a second copy
                    break;
                case MessageChoice.Secondary:                  // discard the capture, don't open
                    vm.DiscardCapture(capture);
                    return;
                case MessageChoice.Tertiary:                   // add the photos to the existing book, no new record
                {
                    var added = capture.Photos.Count;
                    if (vm.AttachCaptureToExistingBook(capture))
                        await MessageDialog.InfoAsync(owner, "Photos added",
                            $"Added {added} photo{(added == 1 ? "" : "s")} to \"{dupTitle}\".");
                    else
                        await MessageDialog.InfoAsync(owner, "Couldn't add photos",
                            "The matching book could not be found - nothing was changed.");
                    return;
                }
                default:                                       // Cancel — leave it pending
                    return;
            }
        }

        vm.LoadFromCapture(capture);
    }
    
    private async void OnPocketClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        await PocketDialog.ShowAsync(owner);
        if (DataContext is MainViewModel vm) vm.RefreshCounts();
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || !vm.CanSave) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        // Warn before adding a brand-new book whose ISBN is already in the catalog.
        // (Captures are already checked at Open; edits keep their own ISBN.)
        if (!vm.IsEditing && !vm.IsFromCapture)
        {
            var dupTitle = vm.FindCurrentFormDuplicate();
            if (dupTitle is not null)
            {
                var proceed = await MessageDialog.ConfirmAsync(owner, "Already in your catalog",
                    $"A book with this ISBN is already in your catalog:\n\n\"{dupTitle}\"\n\nAdd it anyway (e.g. a second copy)?");
                if (!proceed) return;
            }
        }

        vm.SaveCommand.Execute(null);
    }

    private static async Task<string?> PickXlsxAsync(Window owner, string title)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title          = title,
            AllowMultiple  = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Excel workbook") { Patterns = new[] { "*.xlsx" } }
            }
        });
        return files.Count == 0 ? null : files[0].Path.LocalPath;
    }
    
    private async void OnLoansClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.SelectedBook is null) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        await LoansDialog.ShowAsync(owner, vm, vm.SelectedBook.Id, vm.SelectedBook.Title);
    }
   
    private async void OnPictureDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (sender is not Control c || c.DataContext is not PictureItem item) return;
        if (DataContext is not MainViewModel vm) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var withPaths = vm.Pictures.Where(p => !string.IsNullOrEmpty(p.SourcePath)).ToList();
        var paths = withPaths.Select(p => p.SourcePath!).ToList();
        if (paths.Count == 0) return;

        var start = withPaths.IndexOf(item);
        if (start < 0) start = 0;

        var title = !string.IsNullOrWhiteSpace(vm.NewTitle) ? vm.NewTitle : "Image";
        var changed = await PictureViewerWindow.ShowAsync(owner, paths, start, title, allowRotate: true);
        if (changed) vm.ReloadPictures();
    }
    
    private async void OnPreviewPictureDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        // Thumb strip → the tapped PictureItem; big cover → fall back to the current main picture.
        var item = (sender as Control)?.DataContext as PictureItem ?? vm.PreviewMainPicture;
        if (item is null) return;

        var withPaths = vm.PreviewPictures.Where(p => !string.IsNullOrEmpty(p.SourcePath)).ToList();
        var paths = withPaths.Select(p => p.SourcePath!).ToList();
        if (paths.Count == 0) return;

        var start = withPaths.IndexOf(item);
        if (start < 0) start = 0;

        var title = !string.IsNullOrWhiteSpace(vm.Preview?.Title) ? vm.Preview!.Title : "Image";
        await PictureViewerWindow.ShowAsync(owner, paths, start, title);
    }
    
    private async void OnCropClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (vm.SelectedPicture is not { Id: > 0, SourcePath: { } path }) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var applied = await CropDialog.ShowAsync(owner, path);
        if (applied) vm.ReloadPictures();
    }
    
    private void OnBookDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        // Only act on a row double-click — ignore the header and empty space.
        if ((e.Source as Control)?.FindAncestorOfType<DataGridRow>() is null) return;
        if (vm.EditCommand.CanExecute(null)) vm.EditCommand.Execute(null);
    }
    
    private async void OnDeleteClick(object? sender, RoutedEventArgs e) => await DeleteSelectedAsync();

    private async Task DeleteSelectedAsync()
    {
        if (DataContext is not MainViewModel vm || vm.SelectedBook is null) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var ok = await MessageDialog.ConfirmAsync(owner, "Delete book",
            $"Delete \u201c{vm.SelectedBook.Title}\u201d? This cannot be undone.");
        if (!ok) return;

        if (vm.DeleteCommand.CanExecute(null)) vm.DeleteCommand.Execute(null);
    }
    
    private async void OnContributorsClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window owner)
            await ContributorsDialog.ShowAsync(owner);
    }

    private async void OnAboutClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window owner)
            await AboutDialog.ShowAsync(owner);
    }
    
    private void OnGridSorting(object? sender, DataGridColumnEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.SortIsActive = true;
    }

    private static string ResultPathFor(string source)
    {
        var dir  = Path.GetDirectoryName(source) ?? "";
        var name = Path.GetFileNameWithoutExtension(source);
        return Path.Combine(dir, $"{name}-import-result.xlsx");
    }
    
    private void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void OnViewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || DataContext is not MainViewModel vm) return;

        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            FocusSearch();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (SearchBox.IsFocused && !string.IsNullOrEmpty(vm.SearchText))
                vm.SearchText = "";
            else if (vm.ResetViewCommand.CanExecute(null))
                vm.ResetViewCommand.Execute(null);
            else
                return;
            e.Handled = true;
        }
    }

    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        switch (e.Key)
        {
            case Key.F1:
                if (vm.EditCommand.CanExecute(null)) vm.EditCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Delete:
                _ = DeleteSelectedAsync();
                e.Handled = true;
                break;
        }
    }

    private string   _jumpBuffer = "";
    private DateTime _jumpAt;

    private void OnGridTextInput(object? sender, TextInputEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0])) return;

        var now = DateTime.UtcNow;
        if ((now - _jumpAt).TotalMilliseconds > 800) _jumpBuffer = "";   // reset after a pause
        _jumpAt = now;
        _jumpBuffer += e.Text;

        var ci = CultureInfo.GetCultureInfo("pl-PL").CompareInfo;
        BookListItem? match = null;
        foreach (var row in vm.BooksView)                                // display order → respects current sort
            if (row is BookListItem item && item.Title is { } t &&
                ci.IsPrefix(t, _jumpBuffer, CompareOptions.IgnoreCase))
            {
                match = item;
                break;
            }

        if (match is not null)
        {
            vm.SelectedBook = match;
            BookGrid.ScrollIntoView(match, null);
        }
        e.Handled = true;
    }
}
