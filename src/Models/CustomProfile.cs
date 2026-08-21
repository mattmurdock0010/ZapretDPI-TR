using System.Text.Json.Serialization;

namespace ZapretDPI.Models;

public class CustomProfile
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("parameters")]
    public string Parameters { get; set; } = string.Empty;
}
