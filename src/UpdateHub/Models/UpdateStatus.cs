namespace UpdateHub.Models;

public enum UpdateStatus
{
    Pending,
    Queued,
    Running,
    Succeeded,
    Failed,
    Cancelled,
    CancelledByUac,
}
