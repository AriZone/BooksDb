using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BooksDb.Android;

public partial class ShellViewModel : ObservableObject
{
    public HelperViewModel Helper { get; } = new();   // single instance → captured list survives navigation

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OnHelper))]
    [NotifyPropertyChangedFor(nameof(ShowTopBar))]
    private ObservableObject _currentPage;

    public bool OnHelper => CurrentPage is HelperViewModel;

    // The gallery carries its own header (ISBN / "Unassigned photos"), so drop the shell bar there.
    public bool ShowTopBar => CurrentPage is not GalleryViewModel;

    public ShellViewModel()
    {
        _currentPage = Helper;
        Helper.GalleryRequested += OpenGallery;
    }

    private void OpenGallery(CapturedItem item)
    {
        var gallery = new GalleryViewModel(item);
        gallery.CollectionEmptied += emptied => Helper.RemoveCollection(emptied);
        gallery.Done += () =>
        {
            gallery.Cleanup();
            CurrentPage = Helper;
        };
        CurrentPage = gallery;
    }

    /// <summary>Hardware back: let the active page consume internal levels first, otherwise fall back
    /// to the Helper. Returns false only on the Helper itself, so the OS exits the app. Wired from
    /// MainActivity via App.</summary>
    public bool TryHandleBack()
    {
        if (CurrentPage is GalleryViewModel gallery)
        {
            if (gallery.TryBack()) return true;   // fullscreen / selection levels (later steps)
            gallery.Close();
            return true;
        }
        if (CurrentPage is SettingsViewModel)
        {
            CurrentPage = Helper;
            return true;
        }
        return false;
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var settings = new SettingsViewModel();
        settings.Done += () => CurrentPage = Helper;
        CurrentPage = settings;
    }
}