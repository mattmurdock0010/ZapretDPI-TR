using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Serilog;
using ZapretDPI.Models;

namespace ZapretDPI.Services;

public enum ConfigFile
{
    Json,
    Strategy2,
    Hostlist,
    AutoHostlist,
    ExcludeList
}

public class ConfigService
{
    public string AppDir { get; }

    public string UserDataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ZapretDPI-TR");
    public string ConfigDir => Path.Combine(UserDataDir, "config");
    public string RuntimeDir => Path.Combine(UserDataDir, "runtime");
    public string RuntimeBinDir => Path.Combine(RuntimeDir, "bin");

    public string BinDir => Path.Combine(AppDir, "bin");

    public string GetPath(ConfigFile file) => file switch
    {
        ConfigFile.Json => Path.Combine(ConfigDir, "config.json"),
        ConfigFile.Strategy2 => Path.Combine(ConfigDir, "zapret2-profile.txt"),
        ConfigFile.Hostlist => Path.Combine(ConfigDir, "hostlist.txt"),
        ConfigFile.AutoHostlist => Path.Combine(ConfigDir, "autohostlist.txt"),
        ConfigFile.ExcludeList => Path.Combine(ConfigDir, "excludelist.txt"),
        _ => throw new ArgumentOutOfRangeException(nameof(file))
    };

    public string ZapretWinwsDir => Path.Combine(RuntimeBinDir, "zapret", "zapret-winws");
    public string Winws2ExePath => Path.Combine(ZapretWinwsDir, "winws2.exe");
    public string LuaDir => Path.Combine(ZapretWinwsDir, "lua");

    public string BlockcheckDir => Path.Combine(RuntimeBinDir, "zapret", "blockcheck");
    public string Blockcheck2Log => Path.Combine(BlockcheckDir, "blockcheck2.log");
    public string CygwinBashPath => Path.Combine(RuntimeBinDir, "zapret", "cygwin", "bin", "bash.exe");

    public string DnscryptDir => Path.Combine(RuntimeBinDir, "dnscrypt-proxy");
    public string DnscryptExePath => Path.Combine(DnscryptDir, "dnscrypt-proxy.exe");

    public string GoPcap2SocksDir => Path.Combine(RuntimeBinDir, "go-pcap2socks");
    public string GoPcap2SocksExePath => Path.Combine(GoPcap2SocksDir, "go-pcap2socks.exe");

    public ConfigService(string? appDir = null)
    {
        AppDir = appDir ?? ResolveAppRoot();
        EnsureDirectories();
    }

    private static string ResolveAppRoot()
    {
        var candidates = new List<string?>
        {
            AppDomain.CurrentDomain.BaseDirectory,
            Environment.CurrentDirectory,
            Path.GetDirectoryName(Environment.ProcessPath),
            Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName)
        };

        foreach (var dir in candidates)
        {
            if (string.IsNullOrEmpty(dir)) continue;
            var current = new DirectoryInfo(dir);
            while (current != null && current.Exists)
            {
                if (Directory.Exists(Path.Combine(current.FullName, "bin")) ||
                    File.Exists(Path.Combine(current.FullName, "ZapretDPI-TR.dll")))
                {
                    return current.FullName;
                }
                current = current.Parent;
            }
        }

        return AppDomain.CurrentDomain.BaseDirectory;
    }

    private void EnsureDirectories()
    {
        Directory.CreateDirectory(UserDataDir);
        Directory.CreateDirectory(ConfigDir);
        Directory.CreateDirectory(RuntimeDir);
        Directory.CreateDirectory(RuntimeBinDir);

        EnsureRuntimeBinaries();

        if (!File.Exists(GetPath(ConfigFile.Hostlist)))
        {
            File.WriteAllLines(GetPath(ConfigFile.Hostlist), new[]
            {
                "discord.com", "discord.gg", "discordapp.com", "discordapp.net",
                "updates.discord.com", "dl.discordapp.net", "gateway.discord.gg",
                "cdn.discordapp.com", "media.discordapp.net", "roblox.com"
            });
        }

        if (!File.Exists(GetPath(ConfigFile.AutoHostlist)))
            File.WriteAllText(GetPath(ConfigFile.AutoHostlist), "");

        if (!File.Exists(GetPath(ConfigFile.ExcludeList)))
            File.WriteAllLines(GetPath(ConfigFile.ExcludeList), new[] { "com.tr", "gov.tr", "google.com", "googleapis.com" });

        if (!File.Exists(GetPath(ConfigFile.Strategy2)) || File.ReadAllText(GetPath(ConfigFile.Strategy2)).Contains("--dpi-desync="))
        {
            File.WriteAllText(GetPath(ConfigFile.Strategy2),
                "--payload=\"tls_client_hello\" --lua-desync=\"multisplit:blob=fake_default_tls:tcp_seq=-3000:pos=2:nodrop:repeats=1\" --new --lua-init=\"fake_default_tls=tls_mod(fake_default_tls,'rnd')\" --lua-desync=\"wssize:wsize=1:scale=6\" --payload=\"tls_client_hello\" --lua-desync=\"multisplit:pos=10:seqovl=#fake_default_tls:seqovl_pattern=fake_default_tls\"");
        }


    }

    public void EnsureRuntimeBinaries()
    {
        var sourceBin = Path.Combine(AppDir, "bin");
        if (!Directory.Exists(sourceBin)) return;

        CopyDirectoryRecursive(sourceBin, RuntimeBinDir);
    }

    private static void CopyDirectoryRecursive(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var targetFile = Path.Combine(targetDir, Path.GetFileName(file));
            try
            {
                if (!File.Exists(targetFile) ||
                    File.GetLastWriteTimeUtc(file) > File.GetLastWriteTimeUtc(targetFile) ||
                    new FileInfo(file).Length != new FileInfo(targetFile).Length)
                {
                    File.Copy(file, targetFile, true);
                }
            }
            catch
            {
            }
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var targetSubDir = Path.Combine(targetDir, Path.GetFileName(subDir));
            CopyDirectoryRecursive(subDir, targetSubDir);
        }
    }

    public AppConfig LoadConfig()
    {
        var config = new AppConfig();
        var jsonPath = GetPath(ConfigFile.Json);

        if (File.Exists(jsonPath))
        {
            try
            {
                var json = File.ReadAllText(jsonPath);
                var loaded = JsonSerializer.Deserialize<AppConfig>(json);
                if (loaded != null)
                {
                    config = loaded;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Config yüklenemedi.");
            }
        }

        if (string.IsNullOrWhiteSpace(config.Language))
        {
            var sysLang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
            config.Language = sysLang is "tr" or "ru" or "en" ? sysLang : "en";
        }

        return config;
    }

    public void SaveConfig(AppConfig config)
    {
        try
        {
            var opts = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(config, opts);
            File.WriteAllText(GetPath(ConfigFile.Json), json);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Config kaydedilemedi.");
        }
    }

    public string CustomProfilesPath => Path.Combine(ConfigDir, "custom_profiles.json");

    public List<CustomProfile> LoadCustomProfiles()
    {
        if (File.Exists(CustomProfilesPath))
        {
            try
            {
                var json = File.ReadAllText(CustomProfilesPath);
                return JsonSerializer.Deserialize<List<CustomProfile>>(json) ?? new List<CustomProfile>();
            }
            catch { }
        }
        return new List<CustomProfile>();
    }

    public void SaveCustomProfiles(List<CustomProfile> profiles)
    {
        try
        {
            var opts = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(profiles, opts);
            File.WriteAllText(CustomProfilesPath, json);
        }
        catch { }
    }

    public string GetStrategyFilePath() => GetPath(ConfigFile.Strategy2);

    public bool HasValidStrategy()
    {
        var path = GetStrategyFilePath();
        if (!File.Exists(path)) return false;
        var content = File.ReadAllText(path).Trim();
        return !string.IsNullOrEmpty(content);
    }

    public string ReadStrategyFile()
    {
        var path = GetStrategyFilePath();
        return File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty;
    }

    public void SaveStrategyFile(string content)
    {
        var path = GetPath(ConfigFile.Strategy2);
        File.WriteAllText(path, content.Trim());
    }

    public IReadOnlyList<string> GetAvailablePresets()
    {
        var list = new List<string> { LocalizationService.Get("Preset_Custom") };
        var customProfiles = LoadCustomProfiles();
        foreach (var profile in customProfiles)
        {
            list.Add($"[{LocalizationService.Get("Preset_Custom")}] {profile.Name}");
        }

        list.AddRange(new[]
        {
            LocalizationService.Get("Preset_DualProfile"),
            LocalizationService.Get("Preset_TurkTelekom"),
            LocalizationService.Get("Preset_Superonline"),
            LocalizationService.Get("Preset_Vodafone"),
            LocalizationService.Get("Preset_TelekomMobil")
        });

        var config = LoadConfig();
        if (config.DeletedPresets != null && config.DeletedPresets.Count > 0)
        {
            list.RemoveAll(p => config.DeletedPresets.Contains(p));
        }

        return list;
    }

    public string GetPresetStrategyString(string presetName)
    {
        var customPrefix = $"[{LocalizationService.Get("Preset_Custom")}] ";
        if (presetName.StartsWith(customPrefix))
        {
            var profileName = presetName.Substring(customPrefix.Length);
            var profile = LoadCustomProfiles().Find(p => p.Name == profileName);
            if (profile != null) return profile.Parameters;
        }

        if (presetName == LocalizationService.Get("Preset_Custom") || presetName.Contains("zapret2-profile.txt") || presetName.Contains("strategy2.txt") || presetName.Contains("strategy.txt"))
        {
            return ReadStrategyFile();
        }
        if (presetName == LocalizationService.Get("Preset_ManualCustom"))
        {
            return LoadConfig().CustomStrategy ?? string.Empty;
        }
        if (presetName == LocalizationService.Get("Preset_DualProfile") || presetName.Contains("Dual") || presetName.Contains("Çift") || presetName.Contains("двухпрофильный"))
        {
            return "--payload=\"tls_client_hello\" --lua-desync=\"multisplit:blob=fake_default_tls:tcp_seq=-3000:pos=2:nodrop:repeats=1\" --new --lua-init=\"fake_default_tls=tls_mod(fake_default_tls,'rnd')\" --lua-desync=\"wssize:wsize=1:scale=6\" --payload=\"tls_client_hello\" --lua-desync=\"multisplit:pos=10:seqovl=#fake_default_tls:seqovl_pattern=fake_default_tls\"";
        }
        if (presetName == LocalizationService.Get("Preset_TurkTelekom") || presetName.StartsWith("Turk Telekom") || presetName.StartsWith("Türk Telekom"))
        {
            return "--payload=tls_client_hello --lua-desync=multidisorder:pos=2:seqovl=1";
        }
        if (presetName == LocalizationService.Get("Preset_Superonline") || presetName.StartsWith("Superonline"))
        {
            return "--payload=tls_client_hello --lua-desync=multidisorder:pos=2:seqovl=1";
        }
        if (presetName == LocalizationService.Get("Preset_Vodafone") || presetName.StartsWith("Vodafone"))
        {
            return "--payload=tls_client_hello --lua-desync=multisplit:blob=fake_default_tls:ip_ttl=5:pos=2:nodrop:repeats=1";
        }
        if (presetName == LocalizationService.Get("Preset_TelekomMobil") || presetName.Contains("Telekom Mobil") || presetName.Contains("Telekom Mobile") || presetName.Contains("Telekom Мобильный"))
        {
            return "--payload=tls_client_hello --lua-desync=fake:blob=0x00000000:ip_ttl=5:repeats=1";
        }

        return ReadStrategyFile();
    }
}
