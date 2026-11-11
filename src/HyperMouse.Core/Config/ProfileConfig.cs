using System.Text.Json;
using HyperMouse.Core.Actions;
using HyperMouse.Core.Common;
using HyperMouse.Core.Engine;
using HyperMouse.Core.Input;

namespace HyperMouse.Core.Config;

public sealed class MappingRuleDto
{
    public MouseButton Button { get; set; }
    public CadenceType Cadence { get; set; } = CadenceType.SingleClick;
    public MouseButton ChordSecondary { get; set; } = MouseButton.None;
    public int Layer { get; set; } = 0;
    
    // Action details
    public string ActionType { get; set; } = "Hotkey"; // Hotkey, Media, Mouse, Text, Launch
    public string Parameter { get; set; } = "";
    public string DisplayName { get; set; } = "";

    public IAction? CreateAction()
    {
        return ActionType switch
        {
            "Hotkey" => ParseHotkey(Parameter),
            "Media" => Enum.TryParse<MediaCommand>(Parameter, out var cmd) ? new MediaKeyAction(cmd) : null,
            "Mouse" => Enum.TryParse<MouseButton>(Parameter, out var btn) ? new MouseRemapAction(btn) : null,
            "Text" => new TextSnippetAction(Parameter),
            "Launch" => new LaunchAppAction(Parameter),
            _ => null
        };
    }

    private static HotkeyAction? ParseHotkey(string param)
    {
        if (string.IsNullOrWhiteSpace(param)) return null;

        var parts = param.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;

        var modifiers = new List<VirtualKey>();
        VirtualKey mainKey = VirtualKey.C;

        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i];
            if (i < parts.Length - 1)
            {
                if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || part.Equals("Control", StringComparison.OrdinalIgnoreCase))
                    modifiers.Add(VirtualKey.Control);
                else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                    modifiers.Add(VirtualKey.Shift);
                else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase) || part.Equals("Menu", StringComparison.OrdinalIgnoreCase))
                    modifiers.Add(VirtualKey.Menu);
                else if (part.Equals("Win", StringComparison.OrdinalIgnoreCase) || part.Equals("LWin", StringComparison.OrdinalIgnoreCase))
                    modifiers.Add(VirtualKey.LWin);
            }
            else
            {
                if (Enum.TryParse<VirtualKey>(part, true, out var key))
                    mainKey = key;
                else if (part.Length == 1 && char.IsLetterOrDigit(part[0]))
                    Enum.TryParse(part.ToUpperInvariant(), out mainKey);
            }
        }

        return new HotkeyAction(modifiers.ToArray(), mainKey);
    }
}

public sealed class ProfileConfig
{
    public string Name { get; set; } = "Genel Profil";
    public int DoubleClickThresholdMs { get; set; } = 280;
    public int LongPressThresholdMs { get; set; } = 320;
    public List<MappingRuleDto> Rules { get; set; } = new();

    /// <summary>
    /// Vendor-specific signals learned from the user's mouse (e.g. DPI button), keyed by the logical button.
    /// </summary>
    public Dictionary<MouseButton, LearnedSignal> LearnedSignals { get; set; } = new();

    /// <summary>"System", "Dark" or "Light".</summary>
    public string Theme { get; set; } = "System";

    /// <summary>Closing the window keeps HyperMouse running in the system tray.</summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>User-given names for mice, keyed by "VID:PID" (for mice that report a generic name).</summary>
    public Dictionary<string, string> DeviceAliases { get; set; } = new();

    /// <summary>Rules dropped by the last <see cref="Load"/> because they conflicted with an earlier rule.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public List<MappingRuleDto> DroppedConflicts { get; } = new();

    public static ProfileConfig CreateDefault()
    {
        return new ProfileConfig
        {
            Name = "Genel Profil",
            DoubleClickThresholdMs = 280,
            LongPressThresholdMs = 320,
            Rules = new List<MappingRuleDto>
            {
                new()
                {
                    Button = MouseButton.XButton1,
                    Cadence = CadenceType.SingleClick,
                    ActionType = "Hotkey",
                    Parameter = "Ctrl+C",
                    DisplayName = "Kopyala (Ctrl + C)"
                },
                new()
                {
                    Button = MouseButton.XButton1,
                    Cadence = CadenceType.DoubleClick,
                    ActionType = "Hotkey",
                    Parameter = "Ctrl+V",
                    DisplayName = "Yapıştır (Ctrl + V)"
                },
                new()
                {
                    Button = MouseButton.XButton2,
                    Cadence = CadenceType.LongPress,
                    ActionType = "Hotkey",
                    Parameter = "Win+Shift+S",
                    DisplayName = "Ekran Alıntısı (Win + Shift + S)"
                },
                new()
                {
                    Button = MouseButton.XButton2,
                    Cadence = CadenceType.Chord,
                    ChordSecondary = MouseButton.WheelUp,
                    ActionType = "Media",
                    Parameter = "VolumeUp",
                    DisplayName = "Sesi Aç (+2)"
                },
                new()
                {
                    Button = MouseButton.XButton2,
                    Cadence = CadenceType.Chord,
                    ChordSecondary = MouseButton.WheelDown,
                    ActionType = "Media",
                    Parameter = "VolumeDown",
                    DisplayName = "Sesi Kıs (-2)"
                }
            }
        };
    }

    /// <summary>Reads a profile for import. Throws with a readable message if the file is not a valid profile.</summary>
    public static ProfileConfig Import(string filePath)
    {
        string json = File.ReadAllText(filePath);
        var config = JsonSerializer.Deserialize<ProfileConfig>(json)
                     ?? throw new InvalidDataException("Dosya boş.");
        if (config.Rules == null) throw new InvalidDataException("Dosyada kural listesi yok.");
        config.LearnedSignals ??= new();
        config.DeviceAliases ??= new();
        config.Theme ??= "System";
        config.DroppedConflicts.AddRange(RuleConflictChecker.RemoveConflicts(config.Rules));
        return config;
    }

    public static ProfileConfig Load(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                string json = File.ReadAllText(filePath);
                var config = JsonSerializer.Deserialize<ProfileConfig>(json);
                if (config != null)
                {
                    config.LearnedSignals ??= new();
                    config.DeviceAliases ??= new();
                    config.Theme ??= "System";
                    config.DroppedConflicts.AddRange(RuleConflictChecker.RemoveConflicts(config.Rules));
                    if (config.DroppedConflicts.Count > 0) Save(config, filePath);
                    return config;
                }
            }
        }
        catch
        {
            // Fallback to default
        }

        var def = CreateDefault();
        Save(def, filePath);
        return def;
    }

    public static void Save(ProfileConfig config, string filePath)
    {
        try
        {
            string dir = Path.GetDirectoryName(filePath) ?? "";
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }
        catch
        {
            // Ignore write errors
        }
    }
}


/// <summary>
/// Signature of a non-standard mouse signal (consumer usage, keyboard key, HID report, raw button bits).
/// </summary>
public sealed class LearnedSignal
{
    public string Kind { get; set; } = "";        // Consumer | Key | Hid | RawBits
    public ushort Vid { get; set; }
    public ushort Pid { get; set; }
    public ushort UsagePage { get; set; }
    public ushort Usage { get; set; }
    public uint Code { get; set; }                // consumer usage, VK code or raw-button bits
    public string ReportHex { get; set; } = "";   // for Kind=Hid
    public bool HasRelease { get; set; }          // false -> one-shot signal, emitted as a tap
    public string Description { get; set; } = "";

    public bool Matches(LearnedSignal other) =>
        Kind == other.Kind && Vid == other.Vid && Pid == other.Pid &&
        UsagePage == other.UsagePage && Usage == other.Usage && Code == other.Code &&
        string.Equals(ReportHex, other.ReportHex, StringComparison.OrdinalIgnoreCase);
}
