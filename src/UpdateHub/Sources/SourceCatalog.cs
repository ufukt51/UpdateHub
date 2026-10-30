using System.Net.Http;
using UpdateHub.Services;

namespace UpdateHub.Sources;

public static class SourceCatalog
{
    /// <summary>Tüm kaynaklar, arayüzde gösterilecek sırayla.</summary>
    public static IReadOnlyList<IPackageSource> CreateAll(ProcessRunner runner) =>
    [
        new WingetSource(runner),
        new ChocoSource(runner),
        new NpmSource(runner),
        new UvSource(runner),
        new PipSource(runner),
        new PythonRuntimeSource(runner),
        new DotnetToolSource(runner, Web.Http),
    ];
}
