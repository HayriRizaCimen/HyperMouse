using System.Text.Json;
using HyperMouse.Core.Common;

namespace HyperMouse.Core.Config;

public sealed class ProfileManager
{
    public const string DefaultProfileName = "Genel Profil";

    private readonly string _baseDir;
    private readonly string _profilesDir;
    private readonly string _settingsFilePath;
    private AppSettings _settings = new();

    public ProfileConfig CurrentProfile { get; private set; } = null!;
    public string ActiveProfileName => _settings.ActiveProfile;
    public string Language => _settings.Language;
    public string ProfilesDirectory => _profilesDir;
    public string SettingsFilePath => _settingsFilePath;

    public event Action<ProfileConfig>? ActiveProfileChanged;
    public event Action<string>? LanguageChanged;

    public ProfileManager(string? baseDir = null)
    {
        _baseDir = baseDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HyperMouse"
        );
        _profilesDir = Path.Combine(_baseDir, "Profiles");
        _settingsFilePath = Path.Combine(_baseDir, "settings.json");

        Initialize();
    }

    private void Initialize()
    {
        Directory.CreateDirectory(_baseDir);
        Directory.CreateDirectory(_profilesDir);

        LoadSettings();
        MigrateLegacyProfilesIfNeeded();
        EnsureAtLeastOneProfile();

        // Ensure active profile exists
        string activeName = _settings.ActiveProfile;
        string activePath = GetProfilePath(activeName);
        if (!File.Exists(activePath))
        {
            var existing = GetProfileNames();
            activeName = existing.Count > 0 ? existing[0] : DefaultProfileName;
            _settings.ActiveProfile = activeName;
            SaveSettings();
            activePath = GetProfilePath(activeName);
        }

        CurrentProfile = ProfileConfig.Load(activePath);
        CurrentProfile.Name = activeName;
        LocalizationService.CurrentLanguage = _settings.Language;
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                string json = File.ReadAllText(_settingsFilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null)
                {
                    _settings = loaded;
                    if (string.IsNullOrWhiteSpace(_settings.ActiveProfile))
                        _settings.ActiveProfile = DefaultProfileName;
                    _settings.Language = LocalizationService.NormalizeLanguage(_settings.Language);
                    return;
                }
            }
        }
        catch
        {
            // Fallback to default
        }

        _settings = new AppSettings
        {
            ActiveProfile = DefaultProfileName,
            Language = LocalizationService.DefaultLanguage
        };
        SaveSettings();
    }

    public void SaveSettings()
    {
        try
        {
            string json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsFilePath, json);
        }
        catch
        {
            // Ignore write errors
        }
    }

    private void MigrateLegacyProfilesIfNeeded()
    {
        try
        {
            var files = Directory.GetFiles(_profilesDir, "*.json");
            if (files.Length > 0) return; // Already has profiles

            // Check profiles.json or profile.json in _baseDir
            string legacy1 = Path.Combine(_baseDir, "profiles.json");
            string legacy2 = Path.Combine(_baseDir, "profile.json");
            string? source = File.Exists(legacy1) ? legacy1 : (File.Exists(legacy2) ? legacy2 : null);

            if (source != null)
            {
                var legacyProfile = ProfileConfig.Load(source);
                string name = string.IsNullOrWhiteSpace(legacyProfile.Name) ? DefaultProfileName : legacyProfile.Name;
                name = CleanFileName(name);
                legacyProfile.Name = name;
                string dest = GetProfilePath(name);
                ProfileConfig.Save(legacyProfile, dest);
                _settings.ActiveProfile = name;
                SaveSettings();
            }
        }
        catch
        {
            // Migration non-fatal
        }
    }

    private void EnsureAtLeastOneProfile()
    {
        var existing = GetProfileNames();
        if (existing.Count == 0)
        {
            var def = ProfileConfig.CreateDefault();
            def.Name = DefaultProfileName;
            string path = GetProfilePath(DefaultProfileName);
            ProfileConfig.Save(def, path);
            _settings.ActiveProfile = DefaultProfileName;
            SaveSettings();
        }
    }

    public IReadOnlyList<string> GetProfileNames()
    {
        try
        {
            if (!Directory.Exists(_profilesDir)) return Array.Empty<string>();
            var files = Directory.GetFiles(_profilesDir, "*.json");
            var names = new List<string>(files.Length);
            foreach (var f in files)
            {
                string name = Path.GetFileNameWithoutExtension(f);
                if (!string.IsNullOrWhiteSpace(name))
                    names.Add(name);
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public string GetProfilePath(string profileName)
    {
        string safe = CleanFileName(profileName);
        return Path.Combine(_profilesDir, $"{safe}.json");
    }

    public static string CleanFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return DefaultProfileName;
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        string clean = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(clean) ? DefaultProfileName : clean;
    }

    public static bool IsValidProfileName(string name, out string? errorKey)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            errorKey = "Profile_Error_Empty";
            return false;
        }

        string trimmed = name.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        if (trimmed.Any(c => invalid.Contains(c)))
        {
            errorKey = "Profile_Error_InvalidChars";
            return false;
        }

        errorKey = null;
        return true;
    }

    public bool SwitchProfile(string profileName)
    {
        string name = CleanFileName(profileName);
        string path = GetProfilePath(name);
        if (!File.Exists(path)) return false;

        // Save current before switching
        SaveCurrent();

        CurrentProfile = ProfileConfig.Load(path);
        CurrentProfile.Name = name;
        _settings.ActiveProfile = name;
        SaveSettings();

        ActiveProfileChanged?.Invoke(CurrentProfile);
        return true;
    }

    public ProfileConfig CreateProfile(string profileName, bool copyCurrentRules = false)
    {
        if (!IsValidProfileName(profileName, out var errorKey))
            throw new ArgumentException(errorKey ?? "Invalid name");

        string name = profileName.Trim();
        string path = GetProfilePath(name);
        if (File.Exists(path))
            throw new InvalidOperationException("Profile_Error_Exists");

        // Save current
        SaveCurrent();

        ProfileConfig newProfile;
        if (copyCurrentRules && CurrentProfile != null)
        {
            // Clone current
            string json = JsonSerializer.Serialize(CurrentProfile);
            newProfile = JsonSerializer.Deserialize<ProfileConfig>(json) ?? ProfileConfig.CreateDefault();
            newProfile.Name = name;
        }
        else
        {
            newProfile = ProfileConfig.CreateDefault();
            newProfile.Name = name;
            // Inherit global preferences like theme and quick-disable
            if (CurrentProfile != null)
            {
                newProfile.Theme = CurrentProfile.Theme;
                newProfile.MinimizeToTray = CurrentProfile.MinimizeToTray;
                newProfile.QuickDisableEnabled = CurrentProfile.QuickDisableEnabled;
                newProfile.QuickDisableButton = CurrentProfile.QuickDisableButton;
                newProfile.QuickDisableCadence = CurrentProfile.QuickDisableCadence;
                newProfile.QuickDisableHoldMs = CurrentProfile.QuickDisableHoldMs;
                newProfile.LearnedSignals = new(CurrentProfile.LearnedSignals);
                newProfile.DeviceAliases = new(CurrentProfile.DeviceAliases);
            }
        }

        ProfileConfig.Save(newProfile, path);
        CurrentProfile = newProfile;
        _settings.ActiveProfile = name;
        SaveSettings();

        ActiveProfileChanged?.Invoke(CurrentProfile);
        return newProfile;
    }

    public bool RenameProfile(string oldName, string newName)
    {
        if (!IsValidProfileName(newName, out _)) return false;

        string oldClean = CleanFileName(oldName);
        string newClean = CleanFileName(newName);
        if (string.Equals(oldClean, newClean, StringComparison.OrdinalIgnoreCase)) return true;

        string oldPath = GetProfilePath(oldClean);
        string newPath = GetProfilePath(newClean);

        if (!File.Exists(oldPath)) return false;
        if (File.Exists(newPath)) return false; // Destination already exists

        // Save current first
        SaveCurrent();

        File.Move(oldPath, newPath);

        if (string.Equals(_settings.ActiveProfile, oldClean, StringComparison.OrdinalIgnoreCase))
        {
            _settings.ActiveProfile = newClean;
            SaveSettings();
        }

        if (CurrentProfile != null && string.Equals(CurrentProfile.Name, oldClean, StringComparison.OrdinalIgnoreCase))
        {
            CurrentProfile.Name = newClean;
            ProfileConfig.Save(CurrentProfile, newPath);
        }

        ActiveProfileChanged?.Invoke(CurrentProfile!);
        return true;
    }

    public bool DeleteProfile(string profileName)
    {
        var names = GetProfileNames();
        if (names.Count <= 1) return false; // Cannot delete the only profile

        string clean = CleanFileName(profileName);
        string path = GetProfilePath(clean);
        if (!File.Exists(path)) return false;

        bool isActive = string.Equals(_settings.ActiveProfile, clean, StringComparison.OrdinalIgnoreCase);
        if (isActive)
        {
            var remaining = names.Where(n => !string.Equals(n, clean, StringComparison.OrdinalIgnoreCase)).ToList();
            string next = remaining.Count > 0 ? remaining[0] : DefaultProfileName;

            string nextPath = GetProfilePath(next);
            CurrentProfile = ProfileConfig.Load(nextPath);
            CurrentProfile.Name = next;
            _settings.ActiveProfile = next;
            SaveSettings();
            ActiveProfileChanged?.Invoke(CurrentProfile);
        }

        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return true;
    }

    public void SaveCurrent()
    {
        if (CurrentProfile != null)
        {
            string path = GetProfilePath(CurrentProfile.Name);
            ProfileConfig.Save(CurrentProfile, path);
        }
    }

    public void SaveProfile(ProfileConfig config)
    {
        string path = GetProfilePath(config.Name);
        ProfileConfig.Save(config, path);
    }

    public void SetLanguage(string lang)
    {
        string norm = LocalizationService.NormalizeLanguage(lang);
        if (_settings.Language == norm && LocalizationService.CurrentLanguage == norm) return;

        _settings.Language = norm;
        LocalizationService.CurrentLanguage = norm;
        SaveSettings();
        LanguageChanged?.Invoke(norm);
    }
}
