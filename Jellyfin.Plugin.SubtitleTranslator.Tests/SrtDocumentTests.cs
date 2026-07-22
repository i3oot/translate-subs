using Jellyfin.Plugin.SubtitleTranslator.Services;
using Xunit;

namespace Jellyfin.Plugin.SubtitleTranslator.Tests;

public sealed class SrtDocumentTests
{
    [Fact]
    public void ParseAndRenderPreservesIdentifiersTimingAndMultilineText()
    {
        const string input = "1\r\n00:00:01,000 --> 00:00:03,500\r\nHello\r\nworld\r\n\r\n2\r\n00:00:04,000 --> 00:00:05,000\r\n<i>Again</i>\r\n";

        var document = SrtDocument.Parse(input);
        var rendered = document.Render(["Hallo\nWelt", "<i>Noch einmal</i>"]);
        var normalized = rendered.Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Equal(2, document.Count);
        Assert.Equal(["Hello\nworld", "<i>Again</i>"], document.Texts);
        Assert.Contains("1\n00:00:01,000 --> 00:00:03,500\nHallo\nWelt", normalized);
        Assert.Contains("2\n00:00:04,000 --> 00:00:05,000\n<i>Noch einmal</i>", normalized);
    }

    [Fact]
    public void ParseRejectsInputWithoutValidCues()
    {
        Assert.Throws<FormatException>(() => SrtDocument.Parse("not an srt document"));
    }

    [Fact]
    public void RenderRejectsMismatchedTranslationCount()
    {
        var document = SrtDocument.Parse("1\n00:00:01,000 --> 00:00:02,000\nHello\n");

        Assert.Throws<ArgumentException>(() => document.Render([]));
    }
}
