using System;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Avalonia.Android;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using AndroidX.Activity;

namespace BooksDb.Android;

[Activity(
    Label = "BooksDb",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@mipmap/ic_launcher",
    MainLauncher = true,
    ScreenOrientation = ScreenOrientation.Portrait,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    // Raised on each resume; CameraPreview binds once camera permission is granted.
    public static event Action? Resumed;

    // Activity handle for native dialogs (e.g. confirm-clear) that must sit above the camera surface.
    public static MainActivity? Current { get; private set; }

    // Hardware back routing: App wires this to ShellViewModel.TryHandleBack. Returns true when
    // the in-app navigation consumed the press; otherwise the OS default runs (exit).
    public static Func<bool>? HardwareBackHandler { get; set; }
    
    private const string LocalNetworkPermission = "android.permission.ACCESS_LOCAL_NETWORK";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Ask once for everything not yet granted. Camera: barcode scan + cover capture.
        // Local network: Android 17+ blocks LAN connections (the desktop capture server) for
        // apps targeting API 37 unless this runtime permission is granted.
        var needed = new System.Collections.Generic.List<string>();
        if (ContextCompat.CheckSelfPermission(this, global::Android.Manifest.Permission.Camera)
            != global::Android.Content.PM.Permission.Granted)
            needed.Add(global::Android.Manifest.Permission.Camera);
        if (OperatingSystem.IsAndroidVersionAtLeast(37)
            && ContextCompat.CheckSelfPermission(this, LocalNetworkPermission)
            != global::Android.Content.PM.Permission.Granted)
            needed.Add(LocalNetworkPermission);
        if (needed.Count > 0)
            ActivityCompat.RequestPermissions(this, needed.ToArray(), 100);

        // Route the hardware/predictive back gesture through in-app navigation. The legacy
        // OnBackPressed override is bypassed once predictive back is in play, so we register a
        // dispatcher callback instead — this fires on every Android version.
        OnBackPressedDispatcher.AddCallback(this, new NavBackCallback());
    }

    protected override void OnResume()
    {
        base.OnResume();
        Current = this;
        Resumed?.Invoke();
    }

    // Back dispatcher callback: consume the press for in-app navigation, otherwise disable
    // ourselves and re-dispatch so the system performs its default (exit / move to back).
    private sealed class NavBackCallback : OnBackPressedCallback
    {
        public NavBackCallback() : base(true) { }

        public override void HandleOnBackPressed()
        {
            if (HardwareBackHandler?.Invoke() == true) return;   // navigated within the app

            Enabled = false;                               // nothing to pop → let the OS handle it
            Current?.OnBackPressedDispatcher.OnBackPressed();
        }
    }
}