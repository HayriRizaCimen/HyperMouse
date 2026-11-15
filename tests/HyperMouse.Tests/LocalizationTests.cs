using HyperMouse.Core.Common;
using HyperMouse.Core.Engine;

namespace HyperMouse.Tests;

public class LocalizationTests
{
    [Fact]
    public void DefaultLanguage_IsTurkish()
    {
        Assert.Equal("tr", LocalizationService.DefaultLanguage);
        Assert.Equal(AppLanguage.Tr, LocalizationService.DefaultAppLanguage);
    }

    [Fact]
    public void AllLanguages_DefineAllL10nKeys()
    {
        foreach (var lang in Enum.GetValues<AppLanguage>())
        {
            foreach (var key in Enum.GetValues<L10nKey>())
            {
                string val = LocalizationService.Get(key, lang);
                Assert.False(string.IsNullOrWhiteSpace(val), $"Missing translation for {key} in {lang}");
                Assert.NotEqual(key.ToString(), val);
            }
        }
    }

    [Theory]
    [InlineData(AppLanguage.Tr, "Tuş Haritası")]
    [InlineData(AppLanguage.En, "Key Map")]
    public void Get_ByEnumKeyAndLanguage_ReturnsCorrectText(AppLanguage lang, string expected)
    {
        string actual = LocalizationService.Get(L10nKey.Nav_KeyMap, lang);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("tr", "Tuş Haritası")]
    [InlineData("en", "Key Map")]
    public void Get_ReturnsCorrectLanguageText(string lang, string expected)
    {
        string actual = LocalizationService.Get("Nav_KeyMap", lang);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(MouseButton.XButton1, "tr", "Yan Tuş 1 (Geri)")]
    [InlineData(MouseButton.XButton1, "en", "Side Button 1 (Back)")]
    [InlineData(MouseButton.DpiButton, "tr", "DPI Tuşu")]
    [InlineData(MouseButton.DpiButton, "en", "DPI Button")]
    public void ButtonDisplayName_TranslatesCorrectly(MouseButton btn, string lang, string expected)
    {
        Assert.Equal(expected, LocalizationService.ButtonDisplayName(btn, lang));
    }

    [Theory]
    [InlineData(CadenceType.SingleClick, "tr", "Tek Tık")]
    [InlineData(CadenceType.SingleClick, "en", "Single Click")]
    [InlineData(CadenceType.LongPress, "tr", "Uzun Basma")]
    [InlineData(CadenceType.LongPress, "en", "Long Press")]
    public void CadenceDisplayName_TranslatesCorrectly(CadenceType cad, string lang, string expected)
    {
        Assert.Equal(expected, LocalizationService.CadenceDisplayName(cad, lang));
    }

    [Theory]
    [InlineData(QuickDisableCadence.Hold, "tr", "Uzun Basma (Hold — 1 sn)")]
    [InlineData(QuickDisableCadence.Hold, "en", "Long Press (Hold — 1s)")]
    [InlineData(QuickDisableCadence.TripleClick, "en", "Triple Click (3 Clicks)")]
    public void QuickDisableCadenceDisplayName_TranslatesCorrectly(QuickDisableCadence cad, string lang, string expected)
    {
        Assert.Equal(expected, LocalizationService.QuickDisableCadenceDisplayName(cad, lang));
    }

    [Theory]
    [InlineData("VolumeUp", "tr", "Sesi Aç")]
    [InlineData("VolumeUp", "en", "Volume Up")]
    public void MediaCommandDisplayName_TranslatesCorrectly(string cmd, string lang, string expected)
    {
        Assert.Equal(expected, LocalizationService.MediaCommandDisplayName(cmd, lang));
    }

    [Fact]
    public void LocalizeActionDisplay_TranslatesPresets()
    {
        string en = LocalizationService.LocalizeActionDisplay("Hotkey", "Ctrl+C", "Kopyala (Ctrl + C)", AppLanguage.En);
        Assert.Equal("Copy (Ctrl + C)", en);

        string tr = LocalizationService.LocalizeActionDisplay("Hotkey", "Ctrl+C", "Copy (Ctrl + C)", AppLanguage.Tr);
        Assert.Equal("Kopyala (Ctrl + C)", tr);
    }
}
