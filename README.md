# SilverScreen

SilverScreen is a Linux-native YouTube client built with C#, GIR.Core, GTK4, libadwaita, and Blueprint views.

## Status

Early app shell. The current implementation contains a GIR.Core/libadwaita application foundation, Blueprint-backed main window, mock feed, simple playback status, and an in-memory queue. Network, YouTube backend, MPV playback, cookies, WebKit, SponsorBlock, and persistence are not implemented yet.

## Build

```sh
dotnet restore SilverScreen.sln
dotnet build SilverScreen.sln --no-restore
```

## Run

```sh
dotnet run --project src/SilverScreen.App/SilverScreen.App.csproj
```

## Test

```sh
dotnet test SilverScreen.sln --no-build
```
