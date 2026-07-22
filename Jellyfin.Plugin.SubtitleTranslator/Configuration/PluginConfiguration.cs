using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.SubtitleTranslator.Configuration;

public sealed class PluginConfiguration : BasePluginConfiguration
{
    public string Provider { get; set; } = "LibreTranslate";

    public string Endpoint { get; set; } = "http://localhost:5000";

    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "gpt-4.1-mini";

    public int BatchSize { get; set; } = 20;
}
