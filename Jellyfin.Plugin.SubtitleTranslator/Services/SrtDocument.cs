using System.Text;

namespace Jellyfin.Plugin.SubtitleTranslator.Services;

internal sealed class SrtDocument
{
    private readonly List<Cue> _cues;

    private SrtDocument(List<Cue> cues)
    {
        _cues = cues;
    }

    public int Count => _cues.Count;

    public IReadOnlyList<string> Texts => _cues.Select(cue => cue.Text).ToArray();

    public static SrtDocument Parse(string value)
    {
        var lines = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var cues = new List<Cue>();
        var cursor = 0;

        while (cursor < lines.Length)
        {
            while (cursor < lines.Length && string.IsNullOrWhiteSpace(lines[cursor]))
            {
                cursor++;
            }

            if (cursor >= lines.Length)
            {
                break;
            }

            var identifier = lines[cursor++].Trim();
            if (cursor >= lines.Length || !lines[cursor].Contains("-->", StringComparison.Ordinal))
            {
                throw new FormatException($"Invalid SRT cue near '{identifier}'.");
            }

            var timing = lines[cursor++].Trim();
            var text = new List<string>();
            while (cursor < lines.Length && !string.IsNullOrWhiteSpace(lines[cursor]))
            {
                text.Add(lines[cursor++]);
            }

            if (text.Count == 0)
            {
                continue;
            }

            cues.Add(new Cue(identifier, timing, string.Join('\n', text)));
        }

        if (cues.Count == 0)
        {
            throw new FormatException("The subtitle contains no translatable SRT cues.");
        }

        return new SrtDocument(cues);
    }

    public string Render(IReadOnlyList<string> translatedTexts)
    {
        if (translatedTexts.Count != _cues.Count)
        {
            throw new ArgumentException("The translation result does not match the subtitle cue count.", nameof(translatedTexts));
        }

        var output = new StringBuilder();
        for (var index = 0; index < _cues.Count; index++)
        {
            var cue = _cues[index];
            output.AppendLine(cue.Identifier);
            output.AppendLine(cue.Timing);
            output.AppendLine(translatedTexts[index].Trim());
            output.AppendLine();
        }

        return output.ToString();
    }

    private sealed record Cue(string Identifier, string Timing, string Text);
}
