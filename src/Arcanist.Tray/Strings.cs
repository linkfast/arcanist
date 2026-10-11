using System.Globalization;

namespace Arcanist.Tray;

/// <summary>User-facing text in the Windows display language: Spanish or English, like the admin panel.</summary>
internal sealed class Strings
{
    public static Strings Current { get; } =
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es" ? Spanish() : English();

    public required string StatusRunning { get; init; }
    public required string StatusStarting { get; init; }
    public required string StatusStopping { get; init; }
    public required string StatusStopped { get; init; }
    public required string StatusFailed { get; init; }
    public required string StatusMissing { get; init; }
    public required string ServiceSuffix { get; init; }
    public required string OpenPlayer { get; init; }
    public required string OpenAdmin { get; init; }
    public required string Start { get; init; }
    public required string Stop { get; init; }
    public required string Restart { get; init; }
    public required string OpenLogs { get; init; }
    public required string StartWithWindows { get; init; }
    public required string InstallService { get; init; }
    public required string UpdateService { get; init; }
    public required string UninstallService { get; init; }
    public required string CheckForUpdates { get; init; }
    public required string Checking { get; init; }
    public required string UpToDate { get; init; }
    public required string ApplyUpdate { get; init; }
    public required string SourceCode { get; init; }
    public required string Exit { get; init; }

    public required string ServiceTitle { get; init; }
    public required string ServiceIntro { get; init; }
    public required string ServiceAccount { get; init; }
    public required string ServiceNetworkWarning { get; init; }
    public required string ServiceFolders { get; init; }
    public required string AddFolder { get; init; }
    public required string Remove { get; init; }
    public required string Install { get; init; }
    public required string Cancel { get; init; }
    public required string Uninstall { get; init; }
    public required string Close { get; init; }
    public required string Working { get; init; }
    public required string UninstallServiceConfirm { get; init; }
    public required string ServiceFailed { get; init; }
    public required string ServiceOutdated { get; init; }

    private static Strings Spanish() => new()
    {
        StatusRunning = "En ejecución",
        StatusStarting = "Iniciando…",
        StatusStopping = "Deteniendo…",
        StatusStopped = "Detenido",
        StatusFailed = "El servidor se detuvo por un error",
        StatusMissing = "No se encuentra el servidor",
        ServiceSuffix = "servicio de Windows",
        OpenPlayer = "Abrir Arcanist",
        OpenAdmin = "Panel de administración",
        Start = "Iniciar",
        Stop = "Detener",
        Restart = "Reiniciar",
        OpenLogs = "Ver registros",
        StartWithWindows = "Iniciar con Windows",
        InstallService = "Ejecutar como servicio de Windows…",
        UpdateService = "Actualizar el servicio a {0}",
        UninstallService = "Dejar de ejecutar como servicio…",
        CheckForUpdates = "Buscar actualizaciones",
        Checking = "Buscando actualizaciones…",
        UpToDate = "Arcanist está actualizado",
        ApplyUpdate = "Reiniciar para actualizar a {0}",
        SourceCode = "Código fuente",
        Exit = "Salir",

        ServiceTitle = "Ejecutar Arcanist como servicio",
        ServiceIntro = "Como servicio, Arcanist arranca con Windows y sigue funcionando aunque nadie haya iniciado sesión. " +
                       "Windows pedirá permisos de administrador para instalarlo.",
        ServiceAccount = "El servicio usa la cuenta dedicada NT SERVICE\\Arcanist, que solo puede leer las carpetas a las que le des acceso.",
        ServiceNetworkWarning = "Las unidades de red y las carpetas compartidas que dependen de tu usuario no estarán disponibles. " +
                                "Si tu música está en un NAS, sigue usando Arcanist como aplicación o cambia la cuenta del servicio en services.msc.",
        ServiceFolders = "Carpetas de música a las que tendrá acceso:",
        AddFolder = "Añadir carpeta…",
        Remove = "Quitar",
        Install = "Instalar servicio",
        Cancel = "Cancelar",
        Uninstall = "Desinstalar servicio",
        Close = "Cerrar",
        Working = "Esperando a Windows…",
        UninstallServiceConfirm = "Arcanist dejará de ejecutarse como servicio y volverá a funcionar como aplicación mientras tengas la sesión iniciada. " +
                                  "Tu biblioteca y tu configuración se conservan.",
        ServiceFailed = "No se pudo completar la operación. Los detalles están en service-helper.log, en la carpeta de registros.",
        ServiceOutdated = "El servicio ejecuta una versión distinta de la de esta aplicación.",
    };

    private static Strings English() => new()
    {
        StatusRunning = "Running",
        StatusStarting = "Starting…",
        StatusStopping = "Stopping…",
        StatusStopped = "Stopped",
        StatusFailed = "The server stopped because of an error",
        StatusMissing = "The server is missing",
        ServiceSuffix = "Windows service",
        OpenPlayer = "Open Arcanist",
        OpenAdmin = "Admin panel",
        Start = "Start",
        Stop = "Stop",
        Restart = "Restart",
        OpenLogs = "Show logs",
        StartWithWindows = "Start with Windows",
        InstallService = "Run as a Windows service…",
        UpdateService = "Update the service to {0}",
        UninstallService = "Stop running as a service…",
        CheckForUpdates = "Check for updates",
        Checking = "Checking for updates…",
        UpToDate = "Arcanist is up to date",
        ApplyUpdate = "Restart to update to {0}",
        SourceCode = "Source code",
        Exit = "Exit",

        ServiceTitle = "Run Arcanist as a service",
        ServiceIntro = "As a service, Arcanist starts with Windows and keeps running when nobody is signed in. " +
                       "Windows will ask for administrator permission to install it.",
        ServiceAccount = "The service uses the dedicated account NT SERVICE\\Arcanist, which can only read the folders you give it access to.",
        ServiceNetworkWarning = "Network drives and shared folders that rely on your user account will not be available. " +
                                "If your music is on a NAS, keep running Arcanist as an app or change the service account in services.msc.",
        ServiceFolders = "Music folders it will have access to:",
        AddFolder = "Add folder…",
        Remove = "Remove",
        Install = "Install service",
        Cancel = "Cancel",
        Uninstall = "Uninstall service",
        Close = "Close",
        Working = "Waiting for Windows…",
        UninstallServiceConfirm = "Arcanist will stop running as a service and run as an app again while you are signed in. " +
                                  "Your library and settings are kept.",
        ServiceFailed = "The operation could not be completed. Details are in service-helper.log, in the logs folder.",
        ServiceOutdated = "The service runs a different version than this app.",
    };
}
