using UpdateHub.Models;
using UpdateHub.Services;

namespace UpdateHub.Tests;

public sealed class UpdateVerifierTests
{
    private static PackageUpdate Pkg(string id, string current, string available = "2.0") => new()
    {
        Source = SourceKind.Winget, Id = id, Name = id, Current = current, Available = available,
    };

    [Fact]
    public void StillListedWithSameVersion_IsUnchanged()
    {
        var ghub = Pkg("Logitech.GHUB", "2026.7.992920");
        var code = Pkg("Microsoft.VisualStudioCode", "1.94.2");

        // G HUB yeniden taramada aynı sürümle duruyor; VS Code listeden çıkmış.
        var unchanged = UpdateVerifier.Unchanged([ghub, code], [Pkg("logitech.ghub", "2026.7.992920")]);

        unchanged.ShouldBe([ghub]);
    }

    [Fact]
    public void StillListedWithNewerCurrent_IsUpdated()
    {
        // Arada daha yeni sürüm çıkmış olabilir; kurulu sürüm değiştiyse güncelleme olmuştur.
        var git = Pkg("Git.Git", "2.46.2", "2.47.0");

        UpdateVerifier.Unchanged([git], [Pkg("Git.Git", "2.47.0", "2.48.0")]).ShouldBeEmpty();
    }

    [Fact]
    public void UnknownVersion_IsNotVerified()
    {
        var wk = Pkg("wkhtmltopdf.wkhtmltox", "Unknown");

        UpdateVerifier.CanVerify(wk).ShouldBeFalse();
        UpdateVerifier.Unchanged([wk], [Pkg("wkhtmltopdf.wkhtmltox", "Unknown")]).ShouldBeEmpty();
    }
}
