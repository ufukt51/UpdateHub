using Shouldly;
using UpdateHub.Models;
using UpdateHub.Sources.Parsers;
using Xunit;

namespace UpdateHub.Tests;

public class PackageSizeParserTests
{
    [Fact]
    public void Winget_FirstInstallerUrl()
    {
        const string output = """
            Found 7-Zip [7zip.7zip]
            Version: 26.05
            Installer:
              Installer Type: wix
              Installer Url: https://github.com/ip7z/7zip/releases/download/26.05/7z2605-x64.msi
              Installer SHA256: 3fe4ab99
            """;
        PackageSizeParser.WingetInstallerUrl(output).ShouldBe("https://github.com/ip7z/7zip/releases/download/26.05/7z2605-x64.msi");
        PackageSizeParser.WingetInstallerUrl("No package found matching input criteria.").ShouldBeNull();
    }

    [Fact]
    public void Npm_Tarball()
    {
        PackageSizeParser.NpmTarball("""{"name":"npm","dist":{"tarball":"https://registry.npmjs.org/npm/-/npm-11.0.0.tgz","unpackedSize":1}}""")
            .ShouldBe("https://registry.npmjs.org/npm/-/npm-11.0.0.tgz");
        PackageSizeParser.NpmTarball("""{"name":"npm"}""").ShouldBeNull();
        PackageSizeParser.NpmTarball("Not Found").ShouldBeNull();
    }

    [Fact]
    public void PyPi_PrefersWindowsWheel_ThenUniversal_ThenSource()
    {
        const string all = """
            {"urls":[
              {"filename":"pkg-1.0.tar.gz","size":300},
              {"filename":"pkg-1.0-py3-none-any.whl","size":200},
              {"filename":"pkg-1.0-cp313-cp313-win_amd64.whl","size":100},
              {"filename":"pkg-1.0-cp313-cp313-manylinux_x86_64.whl","size":50}
            ]}
            """;
        PackageSizeParser.PyPiSize(all).ShouldBe(100);
        PackageSizeParser.PyPiSize("""{"urls":[{"filename":"pkg-1.0.tar.gz","size":300},{"filename":"pkg-1.0-py3-none-any.whl","size":200}]}""").ShouldBe(200);
        PackageSizeParser.PyPiSize("""{"urls":[{"filename":"pkg-1.0.tar.gz","size":300}]}""").ShouldBe(300);
        PackageSizeParser.PyPiSize("""{"urls":[]}""").ShouldBeNull();
    }

    [Fact]
    public void SizeText_ShowsLoadingThenValue()
    {
        var p = new PackageUpdate { Source = SourceKind.Npm, Id = "npm", Name = "npm", Current = "10.0.0", Available = "11.0.0" };
        p.SizeText.ShouldBe("—");
        p.IsSizeLoading = true;
        p.SizeText.ShouldBe("…");
        p.Size = 3 * 1024 * 1024 + 512 * 1024;
        p.SizeText.ShouldBe("3,5 MB");
    }
}
