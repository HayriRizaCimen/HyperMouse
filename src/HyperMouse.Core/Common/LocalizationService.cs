using HyperMouse.Core.Engine;

namespace HyperMouse.Core.Common;

/// <summary>
/// Supported application languages.
/// Adding a new language requires only adding an enum member here and its dictionary in <see cref="LocalizationService"/>.
/// </summary>
public enum AppLanguage
{
    Tr,
    En
}

/// <summary>
/// Strongly-typed localization keys for all UI texts, enums, presets, and log messages.
/// </summary>
public enum L10nKey
{
    // App / Navigation
    App_Title,
    App_Subtitle,
    Nav_KeyMap,
    Nav_LiveTest,
    Nav_Settings,
    KillSwitch_Text,
    KillSwitch_Tooltip,

    // Engine Status
    Status_EngineActive,
    Status_EnginePaused,
    Status_HookActive,
    Status_HookPaused,
    Status_ShortcutHint,

    // Detected Mouse Hero
    Hero_Title,
    Hero_Searching,
    Hero_NoMouse,
    Hero_NoMouseDetails,
    Hero_RenameTooltip,
    Hero_DpiReady,
    Hero_DpiNotLearned,
    Hero_DeviceCount,
    Hero_Active,
    Hero_Connected,

    // Page: Rules
    Rules_Title,
    Rules_Subtitle,
    Rules_Count,
    Rules_BtnNewRule,
    Rules_DpiTitle,
    Rules_DpiInstruction,
    Rules_DpiReady,
    Rules_BtnLearn,
    Rules_BtnForget,
    Rules_BtnCancel,
    Rules_EmptyTitle,
    Rules_EmptySubtitle,
    Rules_DragTooltip,
    Rules_EditTooltip,
    Rules_DeleteTooltip,
    Rules_Layer,

    // Profile Controls
    Profile_Label,
    Profile_New,
    Profile_Rename,
    Profile_Delete,
    Profile_New_Title,
    Profile_Rename_Title,
    Profile_Name_Label,
    Profile_Name_Placeholder,
    Profile_Delete_Title,
    Profile_Delete_Warning,
    Profile_Delete_Confirm,
    Profile_Error_Exists,
    Profile_Error_Empty,
    Profile_Error_InvalidChars,
    Profile_Error_CannotDeleteLast,

    // Modal: Add / Edit Rule
    Modal_TitleNew,
    Modal_TitleEdit,
    Modal_Step1,
    Modal_Step2,
    Modal_Step3,
    Modal_Step4_Hotkey,
    Modal_Step4_Media,
    Modal_Step4_Text,
    Modal_Step4_Launch,
    Modal_Step4_Mouse,
    Modal_Step4_Param,
    Modal_ChordSecondary,
    Modal_BtnCancel,
    Modal_BtnSave,
    Modal_ConflictTitle,
    Modal_DpiWarningTitle,
    Modal_DpiWarningMessage,
    Modal_ValidationEmptyParam,

    // Modal: Delete Rule Confirm
    DeleteModal_Title,
    DeleteModal_Subtitle,
    DeleteModal_Message,
    DeleteModal_BtnCancel,
    DeleteModal_BtnConfirm,

    // Page: Live Test
    Live_Title,
    Live_Subtitle,
    Live_BtnLog,
    Live_LastEvent,
    Live_Prompt,
    Live_TriggeredCadence,
    Live_Verbose,
    Live_VerboseTooltip,
    Live_ClearLogTooltip,

    // Page: Settings
    Settings_Title,
    Settings_CadenceTitle,
    Settings_CadenceSubtitle,
    Settings_DoubleClick,
    Settings_LongPress,
    Settings_QuickDisableTitle,
    Settings_QuickDisableSubtitle,
    Settings_TriggerButton,
    Settings_TriggerCadence,
    Settings_ConnectedMiceTitle,
    Settings_ConnectedMiceSubtitle,
    Settings_AppTitle,
    Settings_LanguageTitle,
    Settings_LanguageSubtitle,
    Settings_ThemeTitle,
    Settings_ThemeSubtitle,
    Settings_ThemeSystem,
    Settings_ThemeDark,
    Settings_ThemeLight,
    Settings_AutostartTitle,
    Settings_AutostartSubtitle,
    Settings_TrayTitle,
    Settings_TraySubtitle,
    Settings_HotkeyTitle,
    Settings_HotkeySubtitle,
    Settings_ProfileTitle,
    Settings_ProfileSubtitle,
    Settings_BtnExport,
    Settings_BtnImport,
    Settings_BtnOpenFolder,
    Settings_BtnResetDefaults,
    Settings_ResetConfirm,

    // Tray & Notifications
    Tray_Show,
    Tray_Pause,
    Tray_Resume,
    Tray_Exit,
    Tray_ActiveText,
    Tray_PausedText,
    Tray_EngineStarted,
    Tray_EnginePaused,
    Tray_BalloonBackground,
    Tray_BalloonHint,

    // Log messages
    Log_Pressed,
    Log_EngineStarted,
    Log_EnginePaused,
    Log_AutostartEnabled,
    Log_AutostartDisabled,
    Log_ProfileSaved,
    Log_ProfileLoaded,
    Log_ProfileDeleted,
    Log_ProfileCreated,
    Log_ProfileRenamed,

    // Mouse Buttons
    Btn_XButton1,
    Btn_XButton2,
    Btn_Middle,
    Btn_Right,
    Btn_Left,
    Btn_WheelUp,
    Btn_WheelDown,
    Btn_WheelTiltLeft,
    Btn_WheelTiltRight,
    Btn_DpiButton,

    // Cadence Types
    Cadence_SingleClick,
    Cadence_DoubleClick,
    Cadence_TripleClick,
    Cadence_LongPress,
    Cadence_PressAndHoldDown,
    Cadence_PressAndHoldUp,
    Cadence_Chord,
    Cadence_LayerModifier,

    // Quick Disable Cadences
    QuickDisable_Hold,
    QuickDisable_TripleClick,
    QuickDisable_QuadClick,
    QuickDisable_DoubleClick,

    // Action Types
    ActionType_Hotkey,
    ActionType_Media,
    ActionType_Mouse,
    ActionType_Text,
    ActionType_Launch,

    // Media Commands
    Media_VolumeUp,
    Media_VolumeDown,
    Media_VolumeMute,
    Media_PlayPause,
    Media_NextTrack,
    Media_PreviousTrack,

    // Action Presets & Prefixes
    Preset_Copy,
    Preset_Paste,
    Preset_SnippingTool,
    Prefix_Hotkey,
    Prefix_Text,
    Prefix_Launch,
    Prefix_Mouse
}

public static class LocalizationService
{
    public const string DefaultLanguage = "tr";
    public static AppLanguage DefaultAppLanguage => AppLanguage.Tr;

    private static AppLanguage _currentAppLanguage = AppLanguage.Tr;

    public static AppLanguage CurrentAppLanguage
    {
        get => _currentAppLanguage;
        set => _currentAppLanguage = value;
    }

    public static string CurrentLanguage
    {
        get => ToCode(_currentAppLanguage);
        set => _currentAppLanguage = ParseLanguage(value);
    }

    private static readonly IReadOnlyDictionary<AppLanguage, string> LanguageCodes = new Dictionary<AppLanguage, string>
    {
        [AppLanguage.Tr] = "tr",
        [AppLanguage.En] = "en"
    };

    public static AppLanguage ParseLanguage(string? lang)
    {
        if (string.IsNullOrWhiteSpace(lang)) return DefaultAppLanguage;
        return lang.Trim().ToLowerInvariant() switch
        {
            "en" or "en-us" or "english" => AppLanguage.En,
            _ => AppLanguage.Tr
        };
    }

    public static string ToCode(AppLanguage lang) =>
        LanguageCodes.TryGetValue(lang, out var code) ? code : DefaultLanguage;

    public static string NormalizeLanguage(string? lang) => ToCode(ParseLanguage(lang));

    // =========================================================================
    // ENUM-TO-KEY MAPPINGS (Single source of truth, zero language conditionals)
    // =========================================================================

    private static readonly IReadOnlyDictionary<MouseButton, L10nKey> ButtonKeys = new Dictionary<MouseButton, L10nKey>
    {
        [MouseButton.XButton1] = L10nKey.Btn_XButton1,
        [MouseButton.XButton2] = L10nKey.Btn_XButton2,
        [MouseButton.Middle] = L10nKey.Btn_Middle,
        [MouseButton.Right] = L10nKey.Btn_Right,
        [MouseButton.Left] = L10nKey.Btn_Left,
        [MouseButton.WheelUp] = L10nKey.Btn_WheelUp,
        [MouseButton.WheelDown] = L10nKey.Btn_WheelDown,
        [MouseButton.WheelTiltLeft] = L10nKey.Btn_WheelTiltLeft,
        [MouseButton.WheelTiltRight] = L10nKey.Btn_WheelTiltRight,
        [MouseButton.DpiButton] = L10nKey.Btn_DpiButton
    };

    private static readonly IReadOnlyDictionary<CadenceType, L10nKey> CadenceKeys = new Dictionary<CadenceType, L10nKey>
    {
        [CadenceType.SingleClick] = L10nKey.Cadence_SingleClick,
        [CadenceType.DoubleClick] = L10nKey.Cadence_DoubleClick,
        [CadenceType.TripleClick] = L10nKey.Cadence_TripleClick,
        [CadenceType.LongPress] = L10nKey.Cadence_LongPress,
        [CadenceType.PressAndHoldDown] = L10nKey.Cadence_PressAndHoldDown,
        [CadenceType.PressAndHoldUp] = L10nKey.Cadence_PressAndHoldUp,
        [CadenceType.Chord] = L10nKey.Cadence_Chord,
        [CadenceType.LayerModifier] = L10nKey.Cadence_LayerModifier
    };

    private static readonly IReadOnlyDictionary<QuickDisableCadence, L10nKey> QuickDisableKeys = new Dictionary<QuickDisableCadence, L10nKey>
    {
        [QuickDisableCadence.Hold] = L10nKey.QuickDisable_Hold,
        [QuickDisableCadence.TripleClick] = L10nKey.QuickDisable_TripleClick,
        [QuickDisableCadence.QuadClick] = L10nKey.QuickDisable_QuadClick,
        [QuickDisableCadence.DoubleClick] = L10nKey.QuickDisable_DoubleClick
    };

    private static readonly IReadOnlyDictionary<string, L10nKey> ActionTypeKeys = new Dictionary<string, L10nKey>(StringComparer.OrdinalIgnoreCase)
    {
        ["Hotkey"] = L10nKey.ActionType_Hotkey,
        ["Media"] = L10nKey.ActionType_Media,
        ["Mouse"] = L10nKey.ActionType_Mouse,
        ["Text"] = L10nKey.ActionType_Text,
        ["Launch"] = L10nKey.ActionType_Launch
    };

    private static readonly IReadOnlyDictionary<string, L10nKey> MediaCommandKeys = new Dictionary<string, L10nKey>(StringComparer.OrdinalIgnoreCase)
    {
        ["VolumeUp"] = L10nKey.Media_VolumeUp,
        ["VolumeDown"] = L10nKey.Media_VolumeDown,
        ["VolumeMute"] = L10nKey.Media_VolumeMute,
        ["PlayPause"] = L10nKey.Media_PlayPause,
        ["NextTrack"] = L10nKey.Media_NextTrack,
        ["PreviousTrack"] = L10nKey.Media_PreviousTrack
    };

    private static readonly L10nKey[] PresetTokenKeys =
    [
        L10nKey.Preset_Copy,
        L10nKey.Preset_Paste,
        L10nKey.Preset_SnippingTool,
        L10nKey.Media_VolumeUp,
        L10nKey.Media_VolumeDown,
        L10nKey.Media_VolumeMute,
        L10nKey.Media_PlayPause,
        L10nKey.Media_NextTrack,
        L10nKey.Media_PreviousTrack
    ];

    private static readonly L10nKey[] ActionPrefixKeys =
    [
        L10nKey.Prefix_Hotkey,
        L10nKey.Prefix_Text,
        L10nKey.Prefix_Launch,
        L10nKey.Prefix_Mouse
    ];

    // =========================================================================
    // UNIFIED LANGUAGE DICTIONARIES
    // =========================================================================

    private static readonly IReadOnlyDictionary<AppLanguage, IReadOnlyDictionary<L10nKey, string>> Resources =
        new Dictionary<AppLanguage, IReadOnlyDictionary<L10nKey, string>>
        {
            [AppLanguage.Tr] = new Dictionary<L10nKey, string>
            {
                // App / Navigation
                [L10nKey.App_Title] = "HyperMouse",
                [L10nKey.App_Subtitle] = "Fare tuş haritası",
                [L10nKey.Nav_KeyMap] = "Tuş Haritası",
                [L10nKey.Nav_LiveTest] = "Canlı Test",
                [L10nKey.Nav_Settings] = "Ayarlar",
                [L10nKey.KillSwitch_Text] = "Kill Switch",
                [L10nKey.KillSwitch_Tooltip] = "HyperMouse'u tamamen kapatır (tepsiye küçültmez)",

                // Engine Status
                [L10nKey.Status_EngineActive] = "Motor aktif",
                [L10nKey.Status_EnginePaused] = "Duraklatıldı",
                [L10nKey.Status_HookActive] = "Hook aktif",
                [L10nKey.Status_HookPaused] = "Hook pasif",
                [L10nKey.Status_ShortcutHint] = "Ctrl+Alt+Shift+M veya Fare Tuşu",

                // Detected Mouse Hero
                [L10nKey.Hero_Title] = "ALGILANAN FARE",
                [L10nKey.Hero_Searching] = "Fare aranıyor…",
                [L10nKey.Hero_NoMouse] = "Tanınan fare yok",
                [L10nKey.Hero_NoMouseDetails] = "Tuşlar yine de çalışır.",
                [L10nKey.Hero_RenameTooltip] = "Fareye ad ver",
                [L10nKey.Hero_DpiReady] = "DPI tuşu hazır",
                [L10nKey.Hero_DpiNotLearned] = "DPI tuşu öğretilmedi",
                [L10nKey.Hero_DeviceCount] = "{0} aygıt",
                [L10nKey.Hero_Active] = "Kullanılıyor",
                [L10nKey.Hero_Connected] = "Bağlı",

                // Page: Rules
                [L10nKey.Rules_Title] = "Tuş Haritası",
                [L10nKey.Rules_Subtitle] = "Her tuş ve ritim için bir eylem belirleyin.",
                [L10nKey.Rules_Count] = "{0} kural",
                [L10nKey.Rules_BtnNewRule] = "Yeni Kural",
                [L10nKey.Rules_DpiTitle] = "DPI tuşu",
                [L10nKey.Rules_DpiInstruction] = "'Öğren'e basıp DPI tuşunu tıklayın.",
                [L10nKey.Rules_DpiReady] = "Hazır: {0}.",
                [L10nKey.Rules_BtnLearn] = "Öğren",
                [L10nKey.Rules_BtnForget] = "Unut",
                [L10nKey.Rules_BtnCancel] = "İptal",
                [L10nKey.Rules_EmptyTitle] = "Henüz kural yok",
                [L10nKey.Rules_EmptySubtitle] = "'Yeni Kural' ile başlayın.",
                [L10nKey.Rules_DragTooltip] = "Sıralamak için sürükleyin",
                [L10nKey.Rules_EditTooltip] = "Düzenle",
                [L10nKey.Rules_DeleteTooltip] = "Sil",
                [L10nKey.Rules_Layer] = "Katman {0}",

                // Profile Controls
                [L10nKey.Profile_Label] = "Profil",
                [L10nKey.Profile_New] = "Yeni Profil",
                [L10nKey.Profile_Rename] = "Yeniden Adlandır",
                [L10nKey.Profile_Delete] = "Profili Sil",
                [L10nKey.Profile_New_Title] = "Yeni Profil",
                [L10nKey.Profile_Rename_Title] = "Profili Yeniden Adlandır",
                [L10nKey.Profile_Name_Label] = "Profil Adı",
                [L10nKey.Profile_Name_Placeholder] = "Örn: Oyun, İş, Çizim",
                [L10nKey.Profile_Delete_Title] = "Profili Sil",
                [L10nKey.Profile_Delete_Warning] = "Bu işlem geri alınamaz",
                [L10nKey.Profile_Delete_Confirm] = "'{0}' profilini silmek istediğinize emin misiniz?",
                [L10nKey.Profile_Error_Exists] = "Bu isimde bir profil zaten mevcut.",
                [L10nKey.Profile_Error_Empty] = "Profil adı boş olamaz.",
                [L10nKey.Profile_Error_InvalidChars] = "Profil adı geçersiz karakterler içeriyor.",
                [L10nKey.Profile_Error_CannotDeleteLast] = "Son kalan profil silinemez.",

                // Modal: Add / Edit Rule
                [L10nKey.Modal_TitleNew] = "Yeni Tuş Eşlemesi",
                [L10nKey.Modal_TitleEdit] = "Kuralı Düzenle — {0}",
                [L10nKey.Modal_Step1] = "1 · Fare tuşu",
                [L10nKey.Modal_Step2] = "2 · Ritim",
                [L10nKey.Modal_Step3] = "3 · Eylem türü",
                [L10nKey.Modal_Step4_Hotkey] = "4 · Kısayol (Ctrl+C)",
                [L10nKey.Modal_Step4_Media] = "4 · Medya komutu",
                [L10nKey.Modal_Step4_Text] = "4 · Yazılacak metin",
                [L10nKey.Modal_Step4_Launch] = "4 · Program / URL",
                [L10nKey.Modal_Step4_Mouse] = "4 · Fare tuşu (Left/Right/Middle)",
                [L10nKey.Modal_Step4_Param] = "4 · Parametre",
                [L10nKey.Modal_ChordSecondary] = "Basılıyken ikinci tuş",
                [L10nKey.Modal_BtnCancel] = "İptal",
                [L10nKey.Modal_BtnSave] = "Kaydet",
                [L10nKey.Modal_ConflictTitle] = "Çakışma",
                [L10nKey.Modal_DpiWarningTitle] = "DPI tuşu henüz öğretilmedi",
                [L10nKey.Modal_DpiWarningMessage] = "Önce Canlı Test sayfasından DPI tuşunu öğretin.",
                [L10nKey.Modal_ValidationEmptyParam] = "Eylem değerini girin (örn. Ctrl+C).",

                // Modal: Delete Rule Confirm
                [L10nKey.DeleteModal_Title] = "Kuralı Sil",
                [L10nKey.DeleteModal_Subtitle] = "Bu işlem geri alınamaz",
                [L10nKey.DeleteModal_Message] = "Bu tuş eşlemesini silmek istediğinize emin misiniz?",
                [L10nKey.DeleteModal_BtnCancel] = "Vazgeç",
                [L10nKey.DeleteModal_BtnConfirm] = "Kuralı Sil",

                // Page: Live Test
                [L10nKey.Live_Title] = "Canlı Test",
                [L10nKey.Live_Subtitle] = "Basılan tuşlar anlık olarak gösterilir.",
                [L10nKey.Live_BtnLog] = "Olay kaydı",
                [L10nKey.Live_LastEvent] = "Son olay",
                [L10nKey.Live_Prompt] = "Farenizi test edin…",
                [L10nKey.Live_TriggeredCadence] = "Tetiklenen ritim",
                [L10nKey.Live_Verbose] = "Ayrıntılı",
                [L10nKey.Live_VerboseTooltip] = "Hook, RawInput ve HID ayrıntılarını da göster",
                [L10nKey.Live_ClearLogTooltip] = "Kaydı temizle",

                // Page: Settings
                [L10nKey.Settings_Title] = "Ayarlar",
                [L10nKey.Settings_CadenceTitle] = "Ritim hassasiyeti",
                [L10nKey.Settings_CadenceSubtitle] = "Çift tık ve uzun basma süreleri.",
                [L10nKey.Settings_DoubleClick] = "Çift tıklama penceresi",
                [L10nKey.Settings_LongPress] = "Uzun basma eşiği",
                [L10nKey.Settings_QuickDisableTitle] = "Fare Tuşu ile Duraklat / Aktif Et",
                [L10nKey.Settings_QuickDisableSubtitle] = "Seçilen tuşa basılı tutarak veya tıklayarak motoru duraklatın/açın.",
                [L10nKey.Settings_TriggerButton] = "Tetikleyici Tuş",
                [L10nKey.Settings_TriggerCadence] = "Tetikleme Yöntemi",
                [L10nKey.Settings_ConnectedMiceTitle] = "Bağlı fareler",
                [L10nKey.Settings_ConnectedMiceSubtitle] = "Algılanan fare aygıtları.",
                [L10nKey.Settings_AppTitle] = "Uygulama",
                [L10nKey.Settings_LanguageTitle] = "Dil",
                [L10nKey.Settings_LanguageSubtitle] = "Uygulama arayüz dili.",
                [L10nKey.Settings_ThemeTitle] = "Tema",
                [L10nKey.Settings_ThemeSubtitle] = "Sistem, koyu veya açık.",
                [L10nKey.Settings_ThemeSystem] = "Sistem",
                [L10nKey.Settings_ThemeDark] = "Koyu",
                [L10nKey.Settings_ThemeLight] = "Açık",
                [L10nKey.Settings_AutostartTitle] = "Windows ile başlat",
                [L10nKey.Settings_AutostartSubtitle] = "Windows açıldığında arka planda başlar.",
                [L10nKey.Settings_TrayTitle] = "Kapatınca tepsiye küçült",
                [L10nKey.Settings_TraySubtitle] = "Pencere kapatıldığında sistem tepsisine küçülür.",
                [L10nKey.Settings_HotkeyTitle] = "Acil durdurma kısayolu",
                [L10nKey.Settings_HotkeySubtitle] = "Motoru genel kısayolla duraklatır/açar.",
                [L10nKey.Settings_ProfileTitle] = "Profil Yönetimi",
                [L10nKey.Settings_ProfileSubtitle] = "Profil ve tuş haritalarını yönetin.",
                [L10nKey.Settings_BtnExport] = "Dışa aktar",
                [L10nKey.Settings_BtnImport] = "İçe aktar",
                [L10nKey.Settings_BtnOpenFolder] = "Klasörü aç",
                [L10nKey.Settings_BtnResetDefaults] = "Varsayılana sıfırla",
                [L10nKey.Settings_ResetConfirm] = "Tüm kurallar varsayılana sıfırlanacak. Onaylıyor musunuz?",

                // Tray & Notifications
                [L10nKey.Tray_Show] = "Göster",
                [L10nKey.Tray_Pause] = "Duraklat",
                [L10nKey.Tray_Resume] = "Başlat",
                [L10nKey.Tray_Exit] = "Çıkış",
                [L10nKey.Tray_ActiveText] = "HyperMouse — aktif",
                [L10nKey.Tray_PausedText] = "HyperMouse — duraklatıldı",
                [L10nKey.Tray_EngineStarted] = "Motor açıldı.",
                [L10nKey.Tray_EnginePaused] = "Motor duraklatıldı.",
                [L10nKey.Tray_BalloonBackground] = "HyperMouse arka planda",
                [L10nKey.Tray_BalloonHint] = "Tepsideki simgeden açabilir veya kapatabilirsiniz.",

                // Log messages
                [L10nKey.Log_Pressed] = "{0} basıldı",
                [L10nKey.Log_EngineStarted] = "Motor açık.",
                [L10nKey.Log_EnginePaused] = "Motor duraklatıldı.",
                [L10nKey.Log_AutostartEnabled] = "Windows ile başlatma açıldı.",
                [L10nKey.Log_AutostartDisabled] = "Windows ile başlatma kapatıldı.",
                [L10nKey.Log_ProfileSaved] = "Profil kaydedildi: {0}",
                [L10nKey.Log_ProfileLoaded] = "Profil yüklendi: {0}",
                [L10nKey.Log_ProfileDeleted] = "Profil silindi: {0}",
                [L10nKey.Log_ProfileCreated] = "Profil oluşturuldu: {0}",
                [L10nKey.Log_ProfileRenamed] = "Profil yeniden adlandırıldı: {0} → {1}",

                // Mouse Buttons
                [L10nKey.Btn_XButton1] = "Yan Tuş 1 (Geri)",
                [L10nKey.Btn_XButton2] = "Yan Tuş 2 (İleri)",
                [L10nKey.Btn_Middle] = "Orta Tuş (Tekerlek tıkı)",
                [L10nKey.Btn_Right] = "Sağ Tuş",
                [L10nKey.Btn_Left] = "Sol Tuş",
                [L10nKey.Btn_WheelUp] = "Tekerlek Yukarı",
                [L10nKey.Btn_WheelDown] = "Tekerlek Aşağı",
                [L10nKey.Btn_WheelTiltLeft] = "Tekerlek Sola Eğ",
                [L10nKey.Btn_WheelTiltRight] = "Tekerlek Sağa Eğ",
                [L10nKey.Btn_DpiButton] = "DPI Tuşu",

                // Cadence Types
                [L10nKey.Cadence_SingleClick] = "Tek Tık",
                [L10nKey.Cadence_DoubleClick] = "Çift Tık",
                [L10nKey.Cadence_TripleClick] = "Üçlü Tık",
                [L10nKey.Cadence_LongPress] = "Uzun Basma",
                [L10nKey.Cadence_PressAndHoldDown] = "Bas-Çek",
                [L10nKey.Cadence_PressAndHoldUp] = "Bırak",
                [L10nKey.Cadence_Chord] = "Kombinasyon",
                [L10nKey.Cadence_LayerModifier] = "Katman Değiştirici",

                // Quick Disable Cadences
                [L10nKey.QuickDisable_Hold] = "Uzun Basma (Hold — 1 sn)",
                [L10nKey.QuickDisable_TripleClick] = "Üçlü Tık (3 Kez Tıkla)",
                [L10nKey.QuickDisable_QuadClick] = "Dörtlü Tık (4 Kez Tıkla)",
                [L10nKey.QuickDisable_DoubleClick] = "Çift Tık (2 Kez Tıkla)",

                // Action Types
                [L10nKey.ActionType_Hotkey] = "Klavye kısayolu",
                [L10nKey.ActionType_Media] = "Medya & ses",
                [L10nKey.ActionType_Mouse] = "Fare tuşu simülasyonu",
                [L10nKey.ActionType_Text] = "Hazır metin yaz",
                [L10nKey.ActionType_Launch] = "Program / URL aç",

                // Media Commands
                [L10nKey.Media_VolumeUp] = "Sesi Aç",
                [L10nKey.Media_VolumeDown] = "Sesi Kıs",
                [L10nKey.Media_VolumeMute] = "Sesi Kapat / Aç",
                [L10nKey.Media_PlayPause] = "Oynat / Durdur",
                [L10nKey.Media_NextTrack] = "Sonraki Parça",
                [L10nKey.Media_PreviousTrack] = "Önceki Parça",

                // Action Presets & Prefixes
                [L10nKey.Preset_Copy] = "Kopyala",
                [L10nKey.Preset_Paste] = "Yapıştır",
                [L10nKey.Preset_SnippingTool] = "Ekran Alıntısı",
                [L10nKey.Prefix_Hotkey] = "Kısayol: ",
                [L10nKey.Prefix_Text] = "Metin: ",
                [L10nKey.Prefix_Launch] = "Aç: ",
                [L10nKey.Prefix_Mouse] = "Fare: "
            },

            [AppLanguage.En] = new Dictionary<L10nKey, string>
            {
                // App / Navigation
                [L10nKey.App_Title] = "HyperMouse",
                [L10nKey.App_Subtitle] = "Mouse remapper",
                [L10nKey.Nav_KeyMap] = "Key Map",
                [L10nKey.Nav_LiveTest] = "Live Test",
                [L10nKey.Nav_Settings] = "Settings",
                [L10nKey.KillSwitch_Text] = "Kill Switch",
                [L10nKey.KillSwitch_Tooltip] = "Completely exit HyperMouse (do not minimize to tray)",

                // Engine Status
                [L10nKey.Status_EngineActive] = "Engine active",
                [L10nKey.Status_EnginePaused] = "Engine paused",
                [L10nKey.Status_HookActive] = "Hook active",
                [L10nKey.Status_HookPaused] = "Hook inactive",
                [L10nKey.Status_ShortcutHint] = "Ctrl+Alt+Shift+M or Mouse Button",

                // Detected Mouse Hero
                [L10nKey.Hero_Title] = "DETECTED MOUSE",
                [L10nKey.Hero_Searching] = "Searching for mouse…",
                [L10nKey.Hero_NoMouse] = "No mouse recognized",
                [L10nKey.Hero_NoMouseDetails] = "Buttons still work.",
                [L10nKey.Hero_RenameTooltip] = "Rename mouse",
                [L10nKey.Hero_DpiReady] = "DPI button ready",
                [L10nKey.Hero_DpiNotLearned] = "DPI button not learned",
                [L10nKey.Hero_DeviceCount] = "{0} devices",
                [L10nKey.Hero_Active] = "Active",
                [L10nKey.Hero_Connected] = "Connected",

                // Page: Rules
                [L10nKey.Rules_Title] = "Key Map",
                [L10nKey.Rules_Subtitle] = "Assign an action to each button and cadence.",
                [L10nKey.Rules_Count] = "{0} rules",
                [L10nKey.Rules_BtnNewRule] = "New Rule",
                [L10nKey.Rules_DpiTitle] = "DPI button",
                [L10nKey.Rules_DpiInstruction] = "Click 'Learn', then press DPI button.",
                [L10nKey.Rules_DpiReady] = "Ready: {0}.",
                [L10nKey.Rules_BtnLearn] = "Learn",
                [L10nKey.Rules_BtnForget] = "Forget",
                [L10nKey.Rules_BtnCancel] = "Cancel",
                [L10nKey.Rules_EmptyTitle] = "No rules yet",
                [L10nKey.Rules_EmptySubtitle] = "Click 'New Rule' to get started.",
                [L10nKey.Rules_DragTooltip] = "Drag to reorder",
                [L10nKey.Rules_EditTooltip] = "Edit",
                [L10nKey.Rules_DeleteTooltip] = "Delete",
                [L10nKey.Rules_Layer] = "Layer {0}",

                // Profile Controls
                [L10nKey.Profile_Label] = "Profile",
                [L10nKey.Profile_New] = "New Profile",
                [L10nKey.Profile_Rename] = "Rename Profile",
                [L10nKey.Profile_Delete] = "Delete Profile",
                [L10nKey.Profile_New_Title] = "New Profile",
                [L10nKey.Profile_Rename_Title] = "Rename Profile",
                [L10nKey.Profile_Name_Label] = "Profile name",
                [L10nKey.Profile_Name_Placeholder] = "e.g. Gaming, Productivity, Coding",
                [L10nKey.Profile_Delete_Title] = "Delete Profile",
                [L10nKey.Profile_Delete_Warning] = "This cannot be undone",
                [L10nKey.Profile_Delete_Confirm] = "Are you sure you want to delete profile '{0}'?",
                [L10nKey.Profile_Error_Exists] = "A profile with this name already exists.",
                [L10nKey.Profile_Error_Empty] = "Profile name cannot be empty.",
                [L10nKey.Profile_Error_InvalidChars] = "Profile name contains invalid characters.",
                [L10nKey.Profile_Error_CannotDeleteLast] = "Cannot delete the only profile.",

                // Modal: Add / Edit Rule
                [L10nKey.Modal_TitleNew] = "New Key Mapping",
                [L10nKey.Modal_TitleEdit] = "Edit Rule — {0}",
                [L10nKey.Modal_Step1] = "1 · Mouse button",
                [L10nKey.Modal_Step2] = "2 · Cadence",
                [L10nKey.Modal_Step3] = "3 · Action type",
                [L10nKey.Modal_Step4_Hotkey] = "4 · Hotkey (Ctrl+C)",
                [L10nKey.Modal_Step4_Media] = "4 · Media command",
                [L10nKey.Modal_Step4_Text] = "4 · Text snippet",
                [L10nKey.Modal_Step4_Launch] = "4 · Launch app / URL",
                [L10nKey.Modal_Step4_Mouse] = "4 · Mouse button (Left/Right/Middle)",
                [L10nKey.Modal_Step4_Param] = "4 · Parameter",
                [L10nKey.Modal_ChordSecondary] = "Second button while holding",
                [L10nKey.Modal_BtnCancel] = "Cancel",
                [L10nKey.Modal_BtnSave] = "Save",
                [L10nKey.Modal_ConflictTitle] = "Conflict",
                [L10nKey.Modal_DpiWarningTitle] = "DPI button not learned yet",
                [L10nKey.Modal_DpiWarningMessage] = "Learn the DPI button in Live Test first.",
                [L10nKey.Modal_ValidationEmptyParam] = "Please enter an action value (e.g. Ctrl+C).",

                // Modal: Delete Rule Confirm
                [L10nKey.DeleteModal_Title] = "Delete Rule",
                [L10nKey.DeleteModal_Subtitle] = "This cannot be undone",
                [L10nKey.DeleteModal_Message] = "Are you sure you want to delete this mapping?",
                [L10nKey.DeleteModal_BtnCancel] = "Cancel",
                [L10nKey.DeleteModal_BtnConfirm] = "Delete Rule",

                // Page: Live Test
                [L10nKey.Live_Title] = "Live Test",
                [L10nKey.Live_Subtitle] = "Real-time mouse event inspection.",
                [L10nKey.Live_BtnLog] = "Event log",
                [L10nKey.Live_LastEvent] = "Last event",
                [L10nKey.Live_Prompt] = "Test your mouse…",
                [L10nKey.Live_TriggeredCadence] = "Triggered cadence",
                [L10nKey.Live_Verbose] = "Verbose",
                [L10nKey.Live_VerboseTooltip] = "Show Hook, RawInput and HID debug details",
                [L10nKey.Live_ClearLogTooltip] = "Clear log",

                // Page: Settings
                [L10nKey.Settings_Title] = "Settings",
                [L10nKey.Settings_CadenceTitle] = "Cadence Sensitivity",
                [L10nKey.Settings_CadenceSubtitle] = "Double-click and long-press duration.",
                [L10nKey.Settings_DoubleClick] = "Double-click window",
                [L10nKey.Settings_LongPress] = "Long-press threshold",
                [L10nKey.Settings_QuickDisableTitle] = "Quick Pause / Resume with Mouse",
                [L10nKey.Settings_QuickDisableSubtitle] = "Hold or multi-click the selected mouse button to pause or resume.",
                [L10nKey.Settings_TriggerButton] = "Trigger Button",
                [L10nKey.Settings_TriggerCadence] = "Trigger Method",
                [L10nKey.Settings_ConnectedMiceTitle] = "Connected Mice",
                [L10nKey.Settings_ConnectedMiceSubtitle] = "Mouse devices detected by Windows.",
                [L10nKey.Settings_AppTitle] = "Application",
                [L10nKey.Settings_LanguageTitle] = "Language",
                [L10nKey.Settings_LanguageSubtitle] = "Application display language.",
                [L10nKey.Settings_ThemeTitle] = "Theme",
                [L10nKey.Settings_ThemeSubtitle] = "System, dark, or light.",
                [L10nKey.Settings_ThemeSystem] = "System",
                [L10nKey.Settings_ThemeDark] = "Dark",
                [L10nKey.Settings_ThemeLight] = "Light",
                [L10nKey.Settings_AutostartTitle] = "Start with Windows",
                [L10nKey.Settings_AutostartSubtitle] = "Starts silently in tray upon Windows login.",
                [L10nKey.Settings_TrayTitle] = "Minimize to tray on close",
                [L10nKey.Settings_TraySubtitle] = "Keeps running in background when closed.",
                [L10nKey.Settings_HotkeyTitle] = "Emergency pause shortcut",
                [L10nKey.Settings_HotkeySubtitle] = "Pauses or resumes engine globally.",
                [L10nKey.Settings_ProfileTitle] = "Profile Management",
                [L10nKey.Settings_ProfileSubtitle] = "Manage profiles, export or import.",
                [L10nKey.Settings_BtnExport] = "Export",
                [L10nKey.Settings_BtnImport] = "Import",
                [L10nKey.Settings_BtnOpenFolder] = "Open folder",
                [L10nKey.Settings_BtnResetDefaults] = "Reset to default",
                [L10nKey.Settings_ResetConfirm] = "All rules will be reset to default. Are you sure?",

                // Tray & Notifications
                [L10nKey.Tray_Show] = "Show",
                [L10nKey.Tray_Pause] = "Pause",
                [L10nKey.Tray_Resume] = "Resume",
                [L10nKey.Tray_Exit] = "Exit",
                [L10nKey.Tray_ActiveText] = "HyperMouse — active",
                [L10nKey.Tray_PausedText] = "HyperMouse — paused",
                [L10nKey.Tray_EngineStarted] = "Engine resumed.",
                [L10nKey.Tray_EnginePaused] = "Engine paused.",
                [L10nKey.Tray_BalloonBackground] = "HyperMouse running in background",
                [L10nKey.Tray_BalloonHint] = "You can open or exit from the system tray icon.",

                // Log messages
                [L10nKey.Log_Pressed] = "{0} pressed",
                [L10nKey.Log_EngineStarted] = "Engine resumed.",
                [L10nKey.Log_EnginePaused] = "Engine paused.",
                [L10nKey.Log_AutostartEnabled] = "Autostart enabled.",
                [L10nKey.Log_AutostartDisabled] = "Autostart disabled.",
                [L10nKey.Log_ProfileSaved] = "Profile saved: {0}",
                [L10nKey.Log_ProfileLoaded] = "Profile loaded: {0}",
                [L10nKey.Log_ProfileDeleted] = "Profile deleted: {0}",
                [L10nKey.Log_ProfileCreated] = "Profile created: {0}",
                [L10nKey.Log_ProfileRenamed] = "Profile renamed: {0} → {1}",

                // Mouse Buttons
                [L10nKey.Btn_XButton1] = "Side Button 1 (Back)",
                [L10nKey.Btn_XButton2] = "Side Button 2 (Forward)",
                [L10nKey.Btn_Middle] = "Middle Button (Wheel Click)",
                [L10nKey.Btn_Right] = "Right Button",
                [L10nKey.Btn_Left] = "Left Button",
                [L10nKey.Btn_WheelUp] = "Wheel Up",
                [L10nKey.Btn_WheelDown] = "Wheel Down",
                [L10nKey.Btn_WheelTiltLeft] = "Wheel Tilt Left",
                [L10nKey.Btn_WheelTiltRight] = "Wheel Tilt Right",
                [L10nKey.Btn_DpiButton] = "DPI Button",

                // Cadence Types
                [L10nKey.Cadence_SingleClick] = "Single Click",
                [L10nKey.Cadence_DoubleClick] = "Double Click",
                [L10nKey.Cadence_TripleClick] = "Triple Click",
                [L10nKey.Cadence_LongPress] = "Long Press",
                [L10nKey.Cadence_PressAndHoldDown] = "Press & Hold",
                [L10nKey.Cadence_PressAndHoldUp] = "Release",
                [L10nKey.Cadence_Chord] = "Chord",
                [L10nKey.Cadence_LayerModifier] = "Layer Modifier",

                // Quick Disable Cadences
                [L10nKey.QuickDisable_Hold] = "Long Press (Hold — 1s)",
                [L10nKey.QuickDisable_TripleClick] = "Triple Click (3 Clicks)",
                [L10nKey.QuickDisable_QuadClick] = "Quad Click (4 Clicks)",
                [L10nKey.QuickDisable_DoubleClick] = "Double Click (2 Clicks)",

                // Action Types
                [L10nKey.ActionType_Hotkey] = "Keyboard shortcut",
                [L10nKey.ActionType_Media] = "Media & volume",
                [L10nKey.ActionType_Mouse] = "Mouse button",
                [L10nKey.ActionType_Text] = "Text snippet",
                [L10nKey.ActionType_Launch] = "Launch app / URL",

                // Media Commands
                [L10nKey.Media_VolumeUp] = "Volume Up",
                [L10nKey.Media_VolumeDown] = "Volume Down",
                [L10nKey.Media_VolumeMute] = "Mute / Unmute",
                [L10nKey.Media_PlayPause] = "Play / Pause",
                [L10nKey.Media_NextTrack] = "Next Track",
                [L10nKey.Media_PreviousTrack] = "Previous Track",

                // Action Presets & Prefixes
                [L10nKey.Preset_Copy] = "Copy",
                [L10nKey.Preset_Paste] = "Paste",
                [L10nKey.Preset_SnippingTool] = "Snipping Tool",
                [L10nKey.Prefix_Hotkey] = "Hotkey: ",
                [L10nKey.Prefix_Text] = "Text: ",
                [L10nKey.Prefix_Launch] = "Launch: ",
                [L10nKey.Prefix_Mouse] = "Mouse: "
            }
        };

    // =========================================================================
    // LOOKUP API (Language-agnostic: resolves strictly via Resources[lang][key])
    // =========================================================================

    public static string Get(L10nKey key, AppLanguage? lang = null)
    {
        AppLanguage target = lang ?? _currentAppLanguage;
        if (Resources.TryGetValue(target, out var dict) && dict.TryGetValue(key, out var val))
            return val;
        if (Resources.TryGetValue(DefaultAppLanguage, out var fallback) && fallback.TryGetValue(key, out var defVal))
            return defVal;
        return key.ToString();
    }

    public static string Get(L10nKey key, string? lang) =>
        Get(key, lang == null ? _currentAppLanguage : ParseLanguage(lang));

    public static string Get(string key, string? lang = null) =>
        Enum.TryParse<L10nKey>(key, ignoreCase: true, out var parsed)
            ? Get(parsed, lang)
            : key;

    public static string Format(L10nKey key, params object[] args)
    {
        string pattern = Get(key);
        try { return string.Format(pattern, args); }
        catch { return pattern; }
    }

    public static string Format(string key, params object[] args) =>
        Enum.TryParse<L10nKey>(key, ignoreCase: true, out var parsed)
            ? Format(parsed, args)
            : key;

    public static string ButtonDisplayName(MouseButton b, AppLanguage? lang = null) =>
        ButtonKeys.TryGetValue(b, out var key) ? Get(key, lang) : b.ToString();

    public static string ButtonDisplayName(MouseButton b, string? lang) =>
        ButtonDisplayName(b, lang == null ? _currentAppLanguage : ParseLanguage(lang));

    public static string CadenceDisplayName(CadenceType cadence, AppLanguage? lang = null) =>
        CadenceKeys.TryGetValue(cadence, out var key) ? Get(key, lang) : cadence.ToString();

    public static string CadenceDisplayName(CadenceType cadence, string? lang) =>
        CadenceDisplayName(cadence, lang == null ? _currentAppLanguage : ParseLanguage(lang));

    public static string QuickDisableCadenceDisplayName(QuickDisableCadence cadence, AppLanguage? lang = null) =>
        QuickDisableKeys.TryGetValue(cadence, out var key) ? Get(key, lang) : cadence.ToString();

    public static string QuickDisableCadenceDisplayName(QuickDisableCadence cadence, string? lang) =>
        QuickDisableCadenceDisplayName(cadence, lang == null ? _currentAppLanguage : ParseLanguage(lang));

    public static string ActionTypeDisplayName(string actionType, AppLanguage? lang = null) =>
        ActionTypeKeys.TryGetValue(actionType, out var key) ? Get(key, lang) : actionType;

    public static string ActionTypeDisplayName(string actionType, string? lang) =>
        ActionTypeDisplayName(actionType, lang == null ? _currentAppLanguage : ParseLanguage(lang));

    public static string MediaCommandDisplayName(string command, AppLanguage? lang = null) =>
        MediaCommandKeys.TryGetValue(command, out var key) ? Get(key, lang) : command;

    public static string MediaCommandDisplayName(string command, string? lang) =>
        MediaCommandDisplayName(command, lang == null ? _currentAppLanguage : ParseLanguage(lang));

    /// <summary>
    /// Translates preset display names across any registered <see cref="AppLanguage"/> dynamically
    /// by matching tokens from all languages against the target language dictionary.
    /// </summary>
    public static string LocalizeActionDisplay(string actionType, string param, string displayName, AppLanguage? lang = null)
    {
        AppLanguage target = lang ?? _currentAppLanguage;

        if (string.Equals(actionType, "Media", StringComparison.OrdinalIgnoreCase))
            return MediaCommandDisplayName(param, target);

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            // 1. Check action prefixes across all languages (e.g., "Kısayol: " <-> "Hotkey: ")
            foreach (var prefixKey in ActionPrefixKeys)
            {
                string targetPrefix = Get(prefixKey, target);
                foreach (var (_, langDict) in Resources)
                {
                    if (langDict.TryGetValue(prefixKey, out var srcPrefix) &&
                        displayName.StartsWith(srcPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        return targetPrefix + displayName[srcPrefix.Length..];
                    }
                }
            }

            // 2. Check preset tokens across all languages (e.g., "Kopyala" <-> "Copy")
            foreach (var tokenKey in PresetTokenKeys)
            {
                string targetToken = Get(tokenKey, target);
                foreach (var (_, langDict) in Resources)
                {
                    if (langDict.TryGetValue(tokenKey, out var srcToken) &&
                        displayName.Contains(srcToken, StringComparison.OrdinalIgnoreCase))
                    {
                        return displayName.Replace(srcToken, targetToken, StringComparison.OrdinalIgnoreCase);
                    }
                }
            }

            return displayName;
        }

        var prefixMap = actionType switch
        {
            "Hotkey" => (L10nKey?)L10nKey.Prefix_Hotkey,
            "Text" => L10nKey.Prefix_Text,
            "Launch" => L10nKey.Prefix_Launch,
            "Mouse" => L10nKey.Prefix_Mouse,
            _ => null
        };

        return prefixMap.HasValue
            ? $"{Get(prefixMap.Value, target)}{param}"
            : $"{ActionTypeDisplayName(actionType, target)}: {param}";
    }

    public static string LocalizeActionDisplay(string actionType, string param, string displayName, string? lang) =>
        LocalizeActionDisplay(actionType, param, displayName, lang == null ? _currentAppLanguage : ParseLanguage(lang));
}
