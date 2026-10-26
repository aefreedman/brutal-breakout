using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BrutalBreakout.Performance;

internal readonly record struct MemoryPressure(long PrivateBytes, ulong AvailablePhysicalBytes, uint LoadPercent);
internal static class MemoryGuard
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    internal delegate bool MemoryQuery(ref MemoryStatus status);
    internal static MemoryStatus Query(MemoryQuery query)
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!query(ref status)) throw new Win32Exception(Marshal.GetLastPInvokeError(), "Physical RAM telemetry unavailable; preflight fails closed.");
        if (status.TotalPhysical == 0 || status.AvailablePhysical > status.TotalPhysical || status.Load > 100)
            throw new InvalidDataException("Invalid physical RAM telemetry.");
        return status;
    }
    public static MemoryPressure Sample()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Study RAM telemetry currently supports Windows only.");
        var status = Query(GlobalMemoryStatusEx);
        using var process = Process.GetCurrentProcess();
        return new(process.PrivateMemorySize64, status.AvailablePhysical, status.Load);
    }
    public static bool Allows(MemoryPressure pressure, Policy policy, long estimate, bool beforeAllocation)
        => pressure.PrivateBytes < policy.PrivateBytes &&
           (!beforeAllocation || estimate < policy.PrivateBytes - pressure.PrivateBytes) &&
           pressure.AvailablePhysicalBytes > (ulong)(policy.HeadroomBytes + (beforeAllocation ? estimate : 0));
}
