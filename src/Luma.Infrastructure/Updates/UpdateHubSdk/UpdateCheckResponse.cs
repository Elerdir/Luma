using System.Text.Json.Serialization;

namespace UpdateHub.Client;

internal sealed class UpdateCheckResponse
{
    [JsonPropertyName("has_update")]    public bool    HasUpdate    { get; set; }
    [JsonPropertyName("version")]       public string  Version      { get; set; } = "";
    [JsonPropertyName("release_notes")] public string? ReleaseNotes { get; set; }
    [JsonPropertyName("download_url")]  public string? DownloadUrl  { get; set; }
    [JsonPropertyName("sha256")]        public string? Sha256       { get; set; }
    [JsonPropertyName("is_mandatory")]  public bool    IsMandatory  { get; set; }
    [JsonPropertyName("channel")]       public string  Channel      { get; set; } = "stable";
}
