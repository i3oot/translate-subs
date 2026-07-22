# Jellyfin Subtitle Translator

An administrator-only Jellyfin plugin for translating an existing text subtitle stream into another language. It extracts the selected subtitle through Jellyfin, translates caption text while preserving SRT timing, and uploads the result as a new external subtitle.

## Features

- Dashboard plugin page only; no player or regular-user menu entry.
- Every custom API endpoint requires Jellyfin's elevated administrator policy.
- Movies and episodes can be searched from the plugin page.
- External and embedded text subtitles are supported.
- LibreTranslate and OpenAI-compatible translation backends are supported.
- Image-based PGS/VobSub/DVD subtitles are identified but require OCR and are not translated.

## Build

Requires the .NET 9 SDK (or a newer SDK capable of targeting .NET 9):

```powershell
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

Copy `Jellyfin.Plugin.SubtitleTranslator.dll` from `Jellyfin.Plugin.SubtitleTranslator/bin/Release/net9.0/` to a dedicated directory under Jellyfin's plugin directory, then restart Jellyfin.

The plugin targets Jellyfin Server 10.11.5. Jellyfin plugin package versions must match the installed server line.

## Configure and use

1. Open **Dashboard → Plugins → Subtitle Translator** as a Jellyfin administrator.
2. Configure either:
   - **LibreTranslate**: base URL such as `http://libretranslate:5000` and an optional API key.
   - **OpenAI-compatible**: base URL (or a full `/chat/completions` URL), API key, and model name.
3. Save the configuration.
4. Search for a movie or episode, select a text subtitle and target language, then choose **Translate subtitle**.

The Jellyfin server process needs write access to the media folder when the library is configured to save subtitles next to media.

## License

GPL-3.0-or-later.
