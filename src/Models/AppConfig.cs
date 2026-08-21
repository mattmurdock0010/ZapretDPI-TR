using System.Collections.Generic;

namespace ZapretDPI.Models;

public enum FilterMode
{
    Disabled = 0,
    Auto = 1,
    Manual = 2
}

public class AppConfig
{
    public string ActiveStrategy { get; set; } = "Türkiye Çift Profil (Discord + Web/Roblox)";
    public string CustomStrategy { get; set; } = string.Empty;
    public FilterMode FilterMode { get; set; } = FilterMode.Disabled;
    public bool DnscryptInstalled { get; set; } = false;
    public string DnscryptServer { get; set; } = "Google";
    public string Language { get; set; } = "tr";
    public List<string> DeletedPresets { get; set; } = new List<string>();
}
