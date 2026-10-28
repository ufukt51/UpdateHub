namespace UpdateHub.Models;

public enum UpdateStatus
{
    Pending,
    Queued,
    Running,
    Succeeded,

    /// <summary>Komut başarılı döndü ama yeniden taramada kurulu sürüm aynı.</summary>
    Unchanged,
    Failed,
    Cancelled,
    CancelledByUac,
}
