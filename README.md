# BooksDb

A cross-platform personal book catalog: an Avalonia desktop application backed by
SQLite, with an Android companion app ("BooksDb Helper") that scans ISBN barcodes
and captures cover photos and sends them to the desktop over the local network.

Built on .NET 10 and Avalonia 12. The desktop app runs on Linux and Windows.

## Features

- **Catalog** — books with authors, translators, illustrators and editors, publishers,
  cities, languages, categories, cover types, and read/unread state, stored in SQLite.
- **Metadata lookup** — fill in a book from its ISBN or title via Google Books,
  Biblioteka Narodowa (Polish National Library), and Open Library.
- **Barcode capture** — the Android companion scans ISBN barcodes with ML Kit and
  captures full-sensor cover photos with CameraX, then sends them to the desktop.
- **Lending** — track borrowers, loans, and returns.
- **Shelf view** — a virtualized cover-grid alongside the data grid, with click-to-preview
  and double-click-to-edit.
- **Excel import** — bulk-import an existing catalog from a spreadsheet.

## Project layout

| Project | Target | Role |
|---|---|---|
| `BooksDb` | `net10.0` | Shared Avalonia UI, views and view-models (MVVM) |
| `BooksDb.Data` | `net10.0` | SQLite data layer, repositories, metadata-lookup clients, Excel import |
| `BooksDb.Desktop` | `net10.0` | Desktop entry point; hosts the capture listener |
| `BooksDb.Android` | `net10.0-android` | Companion app: barcode scanning + cover capture |

## Tech stack

- [.NET 10](https://dotnet.microsoft.com/) / C#
- [Avalonia 12](https://avaloniaui.net/) (Fluent theme, DataGrid)
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)
- SQLite via [Microsoft.Data.Sqlite](https://learn.microsoft.com/dotnet/standard/data/sqlite/)
  and [Dapper](https://github.com/DapperLib/Dapper)
- [ClosedXML](https://github.com/ClosedXML/ClosedXML) for Excel import
- Android: AndroidX CameraX, Google ML Kit Barcode Scanning
- Arimo embedded as the application font

## Building

Requires the .NET 10 SDK. For the Android companion you also need the Android workload:

```sh
dotnet workload install android
```

Build and run the desktop app:

```sh
dotnet build BooksDb.Desktop/BooksDb.Desktop.csproj -c Debug
dotnet run   --project BooksDb.Desktop
```

Build and deploy the Android companion to a connected device:

```sh
dotnet build BooksDb.Android/BooksDb.Android.csproj -c Debug -t:Install
```

The solution builds with Rider, Visual Studio, or the `dotnet` CLI — everything is
plain MSBuild with central package management (`Directory.Packages.props`).

## Capture setup (desktop ↔ phone)

The Android app sends captures to the desktop over plain HTTP on the local network.
The listener is **off by default** and turns on once a port and a non-empty token are set.

On the desktop, in `settings.json`:

- `CapturePort` — TCP port for the listener (default `8787`)
- `CaptureToken` — a shared secret; an empty token keeps the listener off

On the phone, in the app's Settings:

- the desktop's LAN address, e.g. `http://192.168.1.100:8787`
- the same token

Every request is gated by a bearer token. The channel is cleartext HTTP by design —
it is intended for a trusted LAN, not the open internet. `usesCleartextTraffic` is
enabled on Android for the same reason. Do not expose the capture port beyond your
local network.

## Configuration

Desktop settings live in a `settings.json` in the app's config directory (not tracked
by git). Notable keys beyond the capture listener:

- `GoogleBooksApiKey` — optional; without it, Google Books lookups are keyless and
  throttled aggressively
- `DatabasePath` — the active database (default `books.db` in the config dir)
- `DeletePictureFilesOnBookDelete` — delete a book's images on delete, or move them to
  a `pictures_orphaned` folder instead

## License

ISC — see [LICENSE](LICENSE).
