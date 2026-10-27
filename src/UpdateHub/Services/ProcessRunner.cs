using System.Text;
using CliWrap;

namespace UpdateHub.Services;

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr, TimeSpan Duration)
{
    public string Combined => string.IsNullOrEmpty(StdErr) ? StdOut : StdOut + Environment.NewLine + StdErr;
}

public sealed class ProcessRunner
{
    /// <summary>Tüm alt süreçlere verilen ortam değişkenleri: İngilizce ve renksiz çıktı.</summary>
    public static readonly IReadOnlyDictionary<string, string?> Environment = new Dictionary<string, string?>
    {
        ["DOTNET_CLI_UI_LANGUAGE"] = "en",
        ["DOTNET_NOLOGO"] = "1",
        ["NO_COLOR"] = "1",
        ["PYTHONIOENCODING"] = "utf-8",
        ["PYTHONUTF8"] = "1",
        ["UV_NO_PROGRESS"] = "1",
        ["npm_config_fund"] = "false",
        ["npm_config_update_notifier"] = "false",
    };

    /// <param name="onLine">Her satır geldikçe çağrılır. İkinci parametre stderr ise true.</param>
    public async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        Action<string, bool>? onLine = null,
        CancellationToken ct = default)
    {
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        var outTarget = PipeTarget.ToStringBuilder(stdout, Encoding.UTF8);
        var errTarget = PipeTarget.ToStringBuilder(stderr, Encoding.UTF8);
        if (onLine is not null)
        {
            outTarget = PipeTarget.Merge(outTarget, PipeTarget.ToDelegate(l => onLine(l, false), Encoding.UTF8));
            errTarget = PipeTarget.Merge(errTarget, PipeTarget.ToDelegate(l => onLine(l, true), Encoding.UTF8));
        }

        var result = await Cli.Wrap(fileName)
            .WithArguments(arguments)
            .WithEnvironmentVariables(Environment)
            .WithValidation(CommandResultValidation.None)
            .WithStandardOutputPipe(outTarget)
            .WithStandardErrorPipe(errTarget)
            .ExecuteAsync(ct);

        return new ProcessResult(result.ExitCode, stdout.ToString(), stderr.ToString(), result.RunTime);
    }
}
