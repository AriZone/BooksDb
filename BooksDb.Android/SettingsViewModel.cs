using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BooksDb.Android;

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty] private string _baseUrl = "";
    [ObservableProperty] private string _token = "";
    [ObservableProperty] private bool _beepOnScan = true;
    [ObservableProperty] private AspectOption _selectedAspect = CaptureAspect.Options[0];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScanRateText))]
    private double _scanIntervalMs = 200;

    public string ScanRateText =>
        $"Barcode scan interval — {ScanIntervalMs:0} ms (~{(ScanIntervalMs >= 1 ? 1000.0 / ScanIntervalMs : 0):0.#} fps)";

    public IReadOnlyList<AspectOption> AspectOptions => CaptureAspect.Options;

    public event Action? Done;

    public SettingsViewModel()
    {
        var s = SettingsStore.Load();
        BaseUrl = s.BaseUrl;
        Token   = s.Token;
        BeepOnScan = s.BeepOnScan;
        SelectedAspect = CaptureAspect.FromId(s.CaptureAspectRatio);
        ScanIntervalMs = s.ScanIntervalMs;
    }

    [RelayCommand]
    private void Save()
    {
        // CaptureFastStill is owned by the Helper toolbar (X/AF), not this screen — carry the saved
        // value through so saving Settings never silently resets it.
        var existing = SettingsStore.Load();
        SettingsStore.Save(new AppSettings
        {
            BaseUrl = BaseUrl.Trim(),
            Token   = Token.Trim(),
            BeepOnScan = BeepOnScan,
            CaptureAspectRatio = SelectedAspect.Id,
            CaptureFastStill = existing.CaptureFastStill,
            ScanIntervalMs = (int)System.Math.Round(ScanIntervalMs),
        });
        Done?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => Done?.Invoke();
}