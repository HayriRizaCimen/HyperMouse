using HyperMouse.Core.Config;

namespace HyperMouse.Tests;

public class ProfileManagerTests : IDisposable
{
    private readonly string _tempDir;

    public ProfileManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "HyperMouseTests_" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch { }
    }

    [Fact]
    public void Initializes_WithDefaultProfile_WhenEmpty()
    {
        var pm = new ProfileManager(_tempDir);
        Assert.Equal("Genel Profil", pm.ActiveProfileName);
        Assert.NotNull(pm.CurrentProfile);
        Assert.Contains("Genel Profil", pm.GetProfileNames());
    }

    [Fact]
    public void CreatesAndSwitchesProfile()
    {
        var pm = new ProfileManager(_tempDir);
        pm.CreateProfile("Gaming");

        Assert.Equal("Gaming", pm.ActiveProfileName);
        Assert.Contains("Gaming", pm.GetProfileNames());
        Assert.Contains("Genel Profil", pm.GetProfileNames());

        bool switched = pm.SwitchProfile("Genel Profil");
        Assert.True(switched);
        Assert.Equal("Genel Profil", pm.ActiveProfileName);
    }

    [Fact]
    public void RenamesProfile()
    {
        var pm = new ProfileManager(_tempDir);
        pm.CreateProfile("OldName");
        Assert.Equal("OldName", pm.ActiveProfileName);

        bool renamed = pm.RenameProfile("OldName", "NewName");
        Assert.True(renamed);
        Assert.Equal("NewName", pm.ActiveProfileName);
        Assert.Contains("NewName", pm.GetProfileNames());
        Assert.DoesNotContain("OldName", pm.GetProfileNames());
    }

    [Fact]
    public void DeletesProfile_AndSwitchesToRemaining()
    {
        var pm = new ProfileManager(_tempDir);
        pm.CreateProfile("ProfileToDelete");
        Assert.Equal("ProfileToDelete", pm.ActiveProfileName);

        bool deleted = pm.DeleteProfile("ProfileToDelete");
        Assert.True(deleted);
        Assert.DoesNotContain("ProfileToDelete", pm.GetProfileNames());
        Assert.Equal("Genel Profil", pm.ActiveProfileName);
    }

    [Fact]
    public void CannotDelete_LastRemainingProfile()
    {
        var pm = new ProfileManager(_tempDir);
        // Only "Genel Profil" exists
        bool deleted = pm.DeleteProfile("Genel Profil");
        Assert.False(deleted);
        Assert.Single(pm.GetProfileNames());
    }

    [Fact]
    public void MigratesLegacyProfile_WhenProfilesFolderIsEmpty()
    {
        Directory.CreateDirectory(_tempDir);
        string legacyPath = Path.Combine(_tempDir, "profiles.json");
        var legacy = ProfileConfig.CreateDefault();
        legacy.Name = "MigratedProfile";
        ProfileConfig.Save(legacy, legacyPath);

        var pm = new ProfileManager(_tempDir);
        Assert.Equal("MigratedProfile", pm.ActiveProfileName);
        Assert.Contains("MigratedProfile", pm.GetProfileNames());
    }

    [Fact]
    public void SetsLanguage_AndPersists()
    {
        var pm = new ProfileManager(_tempDir);
        pm.SetLanguage("en");
        Assert.Equal("en", pm.Language);

        // Re-load from disk
        var pm2 = new ProfileManager(_tempDir);
        Assert.Equal("en", pm2.Language);
    }
}
