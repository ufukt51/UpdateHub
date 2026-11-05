using UpdateHub.Models;

namespace UpdateHub.Sources;

/// <summary>Güncelleme sonrası uyumsuzluk denetimi ve geri alma destekleyen kaynak.</summary>
public interface IConflictCheckSource
{
    /// <summary>Günlükte görünen denetim komutu.</summary>
    string CheckCommandText { get; }

    /// <summary>Kurulu paketler arasındaki uyumsuzluklar. Sorun yoksa boş.</summary>
    Task<IReadOnlyList<string>> CheckAsync(CancellationToken ct);

    /// <summary>Paketleri güncelleme öncesi sürümlerine döndüren tek komut.</summary>
    UpdateCommand RollbackCommand(IReadOnlyList<PackageUpdate> packages);

    /// <summary>Kurulum komutunun stderr'inden, denetimin göremediği uyumsuzluklar.</summary>
    IReadOnlyList<string> ParseInstallWarnings(string stderr) => [];
}
