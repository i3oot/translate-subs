using System.Text;
using Jellyfin.Plugin.SubtitleTranslator.Models;
using Jellyfin.Plugin.SubtitleTranslator.Services;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.SubtitleTranslator.Api;

[ApiController]
[Route("SubtitleTranslator")]
[Authorize(Policy = Policies.RequiresElevation)]
public sealed class SubtitleTranslatorController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;
    private readonly IMediaSourceManager _mediaSourceManager;
    private readonly ISubtitleEncoder _subtitleEncoder;
    private readonly ISubtitleManager _subtitleManager;
    private readonly TranslationClient _translationClient;

    public SubtitleTranslatorController(
        ILibraryManager libraryManager,
        IMediaSourceManager mediaSourceManager,
        ISubtitleEncoder subtitleEncoder,
        ISubtitleManager subtitleManager,
        IHttpClientFactory httpClientFactory)
    {
        _libraryManager = libraryManager;
        _mediaSourceManager = mediaSourceManager;
        _subtitleEncoder = subtitleEncoder;
        _subtitleManager = subtitleManager;
        _translationClient = new TranslationClient(httpClientFactory);
    }

    [HttpGet("Items/{itemId:guid}")]
    [ProducesResponseType<ItemSubtitleInfo>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<ItemSubtitleInfo> GetSubtitles(Guid itemId)
    {
        var item = _libraryManager.GetItemById<Video>(itemId);
        if (item is null)
        {
            return NotFound();
        }

        var streams = _mediaSourceManager.GetStaticMediaSources(item, false)
            .SelectMany(source => source.MediaStreams
                .Where(stream => stream.Type == MediaStreamType.Subtitle)
                .Select(stream => new SubtitleStreamInfo(
                    source.Id,
                    stream.Index,
                    stream.Language,
                    stream.Title,
                    stream.Codec,
                    stream.IsExternal,
                    stream.IsTextSubtitleStream)))
            .ToArray();

        return new ItemSubtitleInfo(item.Id.ToString("N"), item.Name, streams);
    }

    [HttpPost("Items/{itemId:guid}/Translate")]
    [ProducesResponseType<TranslationResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TranslationResult>> Translate(
        Guid itemId,
        [FromBody] TranslateRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TargetLanguage) || request.TargetLanguage.Length > 16)
        {
            return BadRequest("TargetLanguage must be a valid language code or name with at most 16 characters.");
        }

        var item = _libraryManager.GetItemById<Video>(itemId);
        if (item is null)
        {
            return NotFound();
        }

        var source = _mediaSourceManager.GetStaticMediaSources(item, false)
            .FirstOrDefault(candidate => string.Equals(candidate.Id, request.MediaSourceId, StringComparison.Ordinal));
        var subtitle = source?.MediaStreams.FirstOrDefault(stream =>
            stream.Type == MediaStreamType.Subtitle && stream.Index == request.SubtitleStreamIndex);

        if (source is null || subtitle is null)
        {
            return BadRequest("The selected subtitle stream no longer exists.");
        }

        if (!subtitle.IsTextSubtitleStream)
        {
            return BadRequest("Image-based subtitles cannot be translated without OCR.");
        }

        await using var input = await _subtitleEncoder.GetSubtitles(
            item,
            source.Id,
            subtitle.Index,
            "srt",
            0,
            0,
            true,
            cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(input, Encoding.UTF8, true);
        var document = SrtDocument.Parse(await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false));
        var configuration = Plugin.Instance?.Configuration
            ?? throw new InvalidOperationException("Subtitle Translator configuration is unavailable.");
        var translated = await _translationClient.TranslateAsync(
            document.Texts,
            subtitle.Language ?? "auto",
            request.TargetLanguage.Trim(),
            configuration,
            cancellationToken).ConfigureAwait(false);

        await using var output = new MemoryStream(Encoding.UTF8.GetBytes(document.Render(translated)));
        await _subtitleManager.UploadSubtitle(item, new SubtitleResponse
        {
            Language = request.TargetLanguage.Trim(),
            Format = "srt",
            IsForced = subtitle.IsForced,
            IsHearingImpaired = subtitle.IsHearingImpaired,
            Stream = output
        }).ConfigureAwait(false);

        return new TranslationResult(item.Id.ToString("N"), item.Name, request.TargetLanguage.Trim(), document.Count);
    }
}
