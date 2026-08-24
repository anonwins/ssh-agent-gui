using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SshAgentGui.Ssh;

internal static class ProcessAncestry
{
    private const int DefaultMaxDepth = 16;

    public static bool TryGetNamedPipeClientProcessId(SafePipeHandle pipe, out int clientProcessId)
    {
        clientProcessId = 0;
        try
        {
            if (pipe.IsInvalid)
                return false;

            var added = false;
            pipe.DangerousAddRef(ref added);
            try
            {
                if (!GetNamedPipeClientProcessId(pipe.DangerousGetHandle(), out var pid) || pid == 0)
                    return false;
                clientProcessId = unchecked((int)pid);
                return clientProcessId > 0;
            }
            finally
            {
                if (added)
                    pipe.DangerousRelease();
            }
        }
        catch
        {
            return false;
        }
    }

    public static bool TryGetParentProcessId(int processId, out int parentProcessId)
    {
        parentProcessId = 0;
        if (processId <= 0)
            return false;

        var snap = CreateToolhelp32Snapshot(Th32CsSnapProcess, 0);
        if (snap == IntPtr.Zero || snap == new IntPtr(-1))
            return false;

        try
        {
            var entry = new ProcessEntry32 { DwSize = (uint)Marshal.SizeOf<ProcessEntry32>() };
            if (!Process32First(snap, ref entry))
                return false;

            var target = unchecked((uint)processId);
            do
            {
                if (entry.Th32ProcessID != target)
                    continue;
                if (entry.Th32ParentProcessID == 0)
                    return false;
                parentProcessId = unchecked((int)entry.Th32ParentProcessID);
                return parentProcessId > 0;
            }
            while (Process32Next(snap, ref entry));

            return false;
        }
        catch
        {
            return false;
        }
        finally
        {
            CloseHandle(snap);
        }
    }

    public static bool IsDescendantOrSelf(int processId, int ancestorProcessId, int maxDepth = DefaultMaxDepth)
    {
        if (processId <= 0 || ancestorProcessId <= 0 || maxDepth < 1)
            return false;
        if (processId == ancestorProcessId)
            return true;

        var seen = new HashSet<int> { processId };
        var current = processId;
        for (var i = 0; i < maxDepth; i++)
        {
            if (!TryGetParentProcessId(current, out var parent))
                return false;
            if (parent == ancestorProcessId)
                return true;
            if (!seen.Add(parent))
                return false;
            current = parent;
        }

        return false;
    }

    public static bool IsTrustedPipeClient(SafePipeHandle pipe, int openSshChildProcessId, out int clientProcessId)
    {
        if (!TryGetNamedPipeClientProcessId(pipe, out clientProcessId))
            return false;
        return IsDescendantOrSelf(clientProcessId, openSshChildProcessId);
    }

    private const uint Th32CsSnapProcess = 0x00000002;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint DwSize;
        public uint CntUsage;
        public uint Th32ProcessID;
        public IntPtr Th32DefaultHeapID;
        public uint Th32ModuleID;
        public uint CntThreads;
        public uint Th32ParentProcessID;
        public int PcPriClassBase;
        public uint DwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string SzExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(IntPtr pipe, out uint clientProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "Process32FirstW")]
    private static extern bool Process32First(IntPtr hSnapshot, ref ProcessEntry32 lppe);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "Process32NextW")]
    private static extern bool Process32Next(IntPtr hSnapshot, ref ProcessEntry32 lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
