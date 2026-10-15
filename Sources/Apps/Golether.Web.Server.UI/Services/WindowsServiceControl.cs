using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Golether.Web.Server.UI.Enums;

namespace Golether.Web.Server.UI.Services;

/// <summary>
/// Asks the service control manager of Windows for the state of a service. Reading the state needs no special
/// rights, and the answer is a number, the same in every language of the system.
/// </summary>
public static partial class WindowsServiceControl
{
    /// <summary>
    /// The right to connect to the service control manager.
    /// </summary>
    private const uint MANAGER_CONNECT = 0x0001;

    /// <summary>
    /// The right to ask a service for its state.
    /// </summary>
    private const uint SERVICE_QUERY_STATUS = 0x0004;

    /// <summary>
    /// The error of Windows for a service that does not exist.
    /// </summary>
    private const int SERVICE_DOES_NOT_EXIST = 1060;

    /// <summary>
    /// Asks for the state of a service.
    /// </summary>
    /// <param name="name">The name of the service.</param>
    /// <returns>The state; <see cref="ServiceState.Unknown"/> when the system could not be asked.</returns>
    public static ServiceState Query(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return OperatingSystem.IsWindows() ? QueryOnWindows(name) : ServiceState.Unknown;
    }

    /// <summary>
    /// Turns the number of a state of the service control manager into a state of the monitor.
    /// </summary>
    /// <param name="state">The number: 1 stopped, 2 starting, 3 stopping, 4 running, 5–7 pausing and paused.</param>
    /// <returns>The state.</returns>
    public static ServiceState FromNative(uint state) => state switch
    {
        1 => ServiceState.Stopped,
        2 or 5 => ServiceState.Starting,
        3 or 6 => ServiceState.Stopping,
        4 or 7 => ServiceState.Running,
        _ => ServiceState.Unknown,
    };

    /// <summary>
    /// Asks the service control manager.
    /// </summary>
    /// <param name="name">The name of the service.</param>
    /// <returns>The state.</returns>
    [SupportedOSPlatform("windows")]
    private static ServiceState QueryOnWindows(string name)
    {
        var manager = OpenSCManager(null, null, MANAGER_CONNECT);
        if (manager == 0)
        {
            return ServiceState.Unknown;
        }

        try
        {
            var service = OpenService(manager, name, SERVICE_QUERY_STATUS);
            if (service == 0)
            {
                return Marshal.GetLastPInvokeError() == SERVICE_DOES_NOT_EXIST ? ServiceState.NotInstalled : ServiceState.Unknown;
            }

            try
            {
                return QueryServiceStatus(service, out var status) ? FromNative(status.CurrentState) : ServiceState.Unknown;
            }
            finally
            {
                CloseServiceHandle(service);
            }
        }
        finally
        {
            CloseServiceHandle(manager);
        }
    }

    /// <summary>
    /// Connects to the service control manager.
    /// </summary>
    /// <param name="machine">The computer, or <see langword="null"/> for this one.</param>
    /// <param name="database">The database, or <see langword="null"/> for the active one.</param>
    /// <param name="access">The rights asked for.</param>
    /// <returns>The handle, or 0.</returns>
    [LibraryImport("advapi32.dll", EntryPoint = "OpenSCManagerW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint OpenSCManager(string? machine, string? database, uint access);

    /// <summary>
    /// Opens a service.
    /// </summary>
    /// <param name="manager">The service control manager.</param>
    /// <param name="name">The name of the service.</param>
    /// <param name="access">The rights asked for.</param>
    /// <returns>The handle, or 0.</returns>
    [LibraryImport("advapi32.dll", EntryPoint = "OpenServiceW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint OpenService(nint manager, string name, uint access);

    /// <summary>
    /// Reads the state of a service.
    /// </summary>
    /// <param name="service">The service.</param>
    /// <param name="status">The state.</param>
    /// <returns><see langword="true"/> on success.</returns>
    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryServiceStatus(nint service, out ServiceStatus status);

    /// <summary>
    /// Closes a handle of the service control manager.
    /// </summary>
    /// <param name="handle">The handle.</param>
    /// <returns><see langword="true"/> on success.</returns>
    [LibraryImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseServiceHandle(nint handle);

    /// <summary>
    /// The state of a service as Windows reports it (SERVICE_STATUS).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        /// <summary>
        /// The type of the service.
        /// </summary>
        public uint ServiceType;

        /// <summary>
        /// The state of the service.
        /// </summary>
        public uint CurrentState;

        /// <summary>
        /// The controls the service accepts.
        /// </summary>
        public uint ControlsAccepted;

        /// <summary>
        /// The exit code of Windows.
        /// </summary>
        public uint Win32ExitCode;

        /// <summary>
        /// The exit code of the service itself.
        /// </summary>
        public uint ServiceSpecificExitCode;

        /// <summary>
        /// The progress of a long start or stop.
        /// </summary>
        public uint CheckPoint;

        /// <summary>
        /// The time the next progress report is expected in.
        /// </summary>
        public uint WaitHint;
    }
}
