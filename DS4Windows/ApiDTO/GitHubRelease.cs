using System.Text.Json.Serialization;

namespace DS4WinWPF.ApiDTO
{
    // https://docs.github.com/en/rest/releases/releases?apiVersion=2022-11-28
    public class GithubRelease
    {
        [JsonPropertyName("tag_name")] public string TagName { get; set; }
        [JsonPropertyName("prerelease")] public bool PreRelease { get; set; }
        [JsonPropertyName("body")] public string Body { get; set; }
        [JsonPropertyName("assets")] public GithubReleaseAsset[] Assets { get; set; }
    }

    public class GithubReleaseAsset
    {
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("size")] public long Size { get; set; }
        [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; set; }
    }
}
