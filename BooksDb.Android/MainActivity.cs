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
    public static Func<bool>? BackRequested { get; set; }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        if (ContextCompat.CheckSelfPermission(this, global::Android.Manifest.Permission.Camera)
            != global::Android.Content.PM.Permission.Granted)
        {
            ActivityCompat.RequestPermissions(this, new[] { global::Android.Manifest.Permission.Camera }, 100);
        }

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
            if (BackRequested?.Invoke() == true) return;   // navigated within the app

            Enabled = false;                               // nothing to pop → let the OS handle it
            Current?.OnBackPressedDispatcher.OnBackPressed();
        }
    }
}