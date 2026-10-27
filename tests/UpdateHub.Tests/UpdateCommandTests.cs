using UpdateHub.Models;
using UpdateHub.Services;
using UpdateHub.Sources;

namespace UpdateHub.Tests;

public class UpdateCommandTests
{
    [Fact]
    public void Winget_UpgradesPackagesWithUnknownVersion()
    {
        var package = new PackageUpdate
        {
            Source = SourceKind.Winget, Id = "wkhtmltopdf.wkhtmltox", Name = "wkhtmltox 0.12.6-1",
            Current = "Unknown", Available = "0.12.6-1",
        };

        var command = new WingetSource(new ProcessRunner()).CreateUpdateJob([package]).ShouldHaveSingleItem();

        command.Arguments.ShouldContain("--include-unknown");
    }
}
