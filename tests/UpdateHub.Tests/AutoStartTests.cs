using Microsoft.Win32;
using UpdateHub.Services;

namespace UpdateHub.Tests;

/// <summary>Gerçek Run anahtarı yerine HKCU altında geçici bir anahtar kullanır.</summary>
public sealed class AutoStartTests : IDisposable
{
    private const string Exe = @"C:\Araclar\UpdateHub.exe";

    private readonly string _root = $@"Software\UpdateHub.Tests\{Guid.NewGuid():N}";
    private string RunKey => _root + @"\Run";
    private string ApprovedKey => _root + @"\Approved";
    private AutoStart Create() => new(RunKey, ApprovedKey);

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(_root, throwOnMissingSubKey: false);

    [Fact]
    public void Enable_WritesQuotedPathWithStartupArgument()
    {
        var autoStart = Create();
        autoStart.IsEnabled.ShouldBeFalse();

        autoStart.Enable(Exe);

        autoStart.Command.ShouldBe($"\"{Exe}\" --startup");
        autoStart.IsEnabled.ShouldBeTrue();
        autoStart.PointsTo(Exe).ShouldBeTrue();
        autoStart.PointsTo(@"D:\Baska\UpdateHub.exe").ShouldBeFalse();
    }

    [Fact]
    public void Disable_RemovesValue()
    {
        var autoStart = Create();
        autoStart.Enable(Exe);

        autoStart.Disable();
        autoStart.Disable();   // ikinci kez hata vermez

        autoStart.Command.ShouldBeNull();
        autoStart.IsEnabled.ShouldBeFalse();
    }

    [Fact]
    public void DisabledInSettings_IsNotEnabled_UntilEnabledAgain()
    {
        var autoStart = Create();
        autoStart.Enable(Exe);
        using (var key = Registry.CurrentUser.CreateSubKey(ApprovedKey))
            key.SetValue("UpdateHub", new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);

        autoStart.IsEnabled.ShouldBeFalse();

        autoStart.Enable(Exe);
        autoStart.IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public void EnabledInSettings_IsEnabled()
    {
        var autoStart = Create();
        autoStart.Enable(Exe);
        using (var key = Registry.CurrentUser.CreateSubKey(ApprovedKey))
            key.SetValue("UpdateHub", new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);

        autoStart.IsEnabled.ShouldBeTrue();
    }
}
