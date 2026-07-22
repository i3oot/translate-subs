namespace Jellyfin.Plugin.SubtitleTranslator.Models;

public sealed record TranslateRequest(string MediaSourceId, int SubtitleStreamIndex, string TargetLanguage);

public sealed record TranslationResult(string ItemId, string ItemName, string TargetLanguage, int CueCount);

public sealed record SubtitleStreamInfo(
    string MediaSourceId,
    int Index,
    string? Language,
    string? Title,
    string? Codec,
    bool IsExternal,
    bool IsText);

public sealed record ItemSubtitleInfo(string ItemId, string Name, IReadOnlyList<SubtitleStreamInfo> Subtitles);
