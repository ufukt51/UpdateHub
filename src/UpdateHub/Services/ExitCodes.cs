namespace UpdateHub.Services;

public static class ExitCodes
{
    /// <summary>Yükleyiciler başarılı ama yeniden başlatma gerektiren kurulumu bu kodlarla bildirir.</summary>
    public static bool IsRebootRequired(int code) => code is 3010 or 1641;

    public static bool IsSuccess(int code) => code == 0 || IsRebootRequired(code);
}
