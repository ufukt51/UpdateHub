using Shouldly;
using UpdateHub.Services;
using Xunit;

namespace UpdateHub.Tests;

public class InstallProgressTests
{
    [Fact]
    public void Winget_StepsFillInOrder()
    {
        var p = new InstallProgress();
        p.OnLine("Found Node.js [OpenJS.NodeJS] Version 27.0.0").ShouldBeFalse();

        p.OnLine("Downloading https://nodejs.org/dist/v27.0.0/node-v27.0.0-x64.msi").ShouldBeTrue();
        p.Phase.ShouldBe(InstallPhase.Downloading);
        p.DownloadUrl.ShouldBe("https://nodejs.org/dist/v27.0.0/node-v27.0.0-x64.msi");
        p.Text.ShouldBe("İndiriliyor…");

        p.OnBytes(15 * 1024 * 1024, 30 * 1024 * 1024).ShouldBeTrue();
        p.Text.ShouldBe("İndiriliyor: ≈15,0 MB / 30,0 MB (≈%50) · ≈15,0 MB kaldı");
        p.Fraction.ShouldBe(0.31, 0.001);

        p.OnLine("Successfully verified installer hash");
        p.Phase.ShouldBe(InstallPhase.Verifying);
        p.Fraction.ShouldBe(0.65);

        p.OnLine("Starting package install...");
        p.Phase.ShouldBe(InstallPhase.Installing);
        p.Text.ShouldBe("Kuruluyor…");

        p.OnLine("Successfully installed");
        p.Phase.ShouldBe(InstallPhase.Finishing);
        p.Fraction.ShouldBe(0.95);
    }

    [Fact]
    public void Estimate_NeverReachesFullBeforeVerification()
    {
        var p = new InstallProgress();
        p.OnLine("Downloading https://example.com/a.exe");
        p.OnBytes(500, 100);
        p.Received.ShouldBe(99);
        p.DownloadPercent.ShouldBe(99);
    }

    [Fact]
    public void Choco_SavingLine_GivesBytesAndPercent()
    {
        var p = new InstallProgress();
        p.OnLine("Progress: Downloading nodejs.install 27.0.0... 100%");
        p.Fraction.ShouldBe(0.3, 0.001);

        p.OnLine("Downloading nodejs.install 64 bit from 'https://nodejs.org/dist/v27.0.0/node-v27.0.0-x64.msi'");
        p.OnLine("Progress: 45% - Saving 13,51 MB of 30,02 MB");
        p.Received.ShouldBe((long)(13.51 * 1024 * 1024));
        p.Total.ShouldBe((long)(30.02 * 1024 * 1024));
        p.IsEstimate.ShouldBeFalse();
        p.Text.ShouldStartWith("İndiriliyor: 13,5 MB / 30,0 MB (%45)");
        p.Fraction.ShouldBe(0.3 + 0.3 * 0.45, 0.001);

        p.OnLine("Download of node-v27.0.0-x64.msi (30.02 MB) completed.");
        p.Phase.ShouldBe(InstallPhase.Verifying);
        p.OnLine("Installing nodejs.install...");
        p.Phase.ShouldBe(InstallPhase.Installing);
        p.OnLine(" The upgrade of nodejs.install was successful.");
        p.Phase.ShouldBe(InstallPhase.Finishing);
    }

    [Fact]
    public void Choco_UrlOnNextLine_AndRepeatedDownloads()
    {
        var p = new InstallProgress();
        p.OnLine("Downloading package from source 'https://community.chocolatey.org/api/v2/'");
        p.Phase.ShouldBe(InstallPhase.Downloading);
        p.OnLine("visualstudio2026buildtools package files upgrade completed. Performing other installation steps.");

        p.OnLine("Downloading channel manifest");
        p.DownloadUrl.ShouldBeNull();
        p.OnLine("  from 'https://aka.ms/vs/18/stable/channel'");
        p.DownloadUrl.ShouldBe("https://aka.ms/vs/18/stable/channel");

        p.OnLine("Download of ChannelManifest_444114094.man (115.7 KB) completed.");
        p.Phase.ShouldBe(InstallPhase.Verifying);

        // İkinci indirme çubuğu geri götürmez, metin yine indirmeyi gösterir.
        p.OnLine("Downloading catalog manifest");
        p.Phase.ShouldBe(InstallPhase.Downloading);
        p.Text.ShouldBe("İndiriliyor…");
        p.Fraction.ShouldBe(0.65);
        p.OnLine("Progress: 50% - Saving 8.7 MB of 17.5 MB");
        p.Fraction.ShouldBeGreaterThan(0.65);

        p.OnLine("visualstudio2026buildtools has been installed.");
        p.Phase.ShouldBe(InstallPhase.Finishing);
    }

    [Theory]
    [InlineData("Progress: 45% - Saving 13.51 MB of 30.02 MB", true)]
    [InlineData("  Progress: Downloading nodejs 27.0.0... 10%", true)]
    [InlineData("Download of x.msi (30.02 MB) completed.", false)]
    public void IsProgressLine(string line, bool expected) => InstallProgress.IsProgressLine(line).ShouldBe(expected);

    [Fact]
    public void Fraction_NeverGoesBack()
    {
        var p = new InstallProgress();
        p.OnLine("Starting package install...");
        p.OnLine("Successfully verified installer hash").ShouldBeFalse();
        p.Phase.ShouldBe(InstallPhase.Installing);
        p.Fraction.ShouldBe(0.75);
    }

    [Theory]
    [InlineData("13.51", "MB", 14166261)]
    [InlineData("13,51", "MB", 14166261)]
    [InlineData("1,234.5", "KB", 1264128)]
    [InlineData("512", "B", 512)]
    public void ParseSize_AcceptsBothDecimalSeparators(string number, string unit, long expected) =>
        InstallProgress.ParseSize(number, unit).ShouldBe(expected);
}
