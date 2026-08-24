using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace SshAgentGui.Ssh;

internal static class PageantCaller
{
    public const int MaxLabelLength = 80;
    public const string UnknownPrompt = "A program wants to use a key from the agent.";

    public static string? Format(string? description, string? title, string? processName)
    {
        var desc = Sanitize(description);
        var window = Sanitize(title);
        var process = Sanitize(StripExe(processName));

        if (desc is not null && window is not null
            && desc.Equals(window, StringComparison.OrdinalIgnoreCase))
            window = null;

        string? label = null;
        if (desc is not null && window is not null)
            label = desc + " — " + window;
        else
            label = desc ?? window ?? process;

        return Truncate(label);
    }

    public static string PromptLine(string? caller)
    {
        if (string.IsNullOrWhiteSpace(caller))
            return UnknownPrompt;
        var name = Truncate(caller.Trim());
        return name is null ? UnknownPrompt : name + " wants to use a key from the agent.";
    }

    public static string? FromProcessId(int pid) => FromProcessId(pid, preferredTitle: null);

    public static string? FromWindow(IntPtr hwnd)
    {
        try
        {
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
                return null;
            var title = WindowTitle(hwnd);
            _ = GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0)
                return Format(null, title, null);
            return FromProcessId(unchecked((int)pid), title);
        }
        catch
        {
            return null;
        }
    }

    public static string? FromPipe(SafePipeHandle handle)
    {
        try
        {
            if (handle.IsInvalid)
                return null;
            var added = false;
            handle.DangerousAddRef(ref added);
            try
            {
                var raw = handle.DangerousGetHandle();
                if (GetNamedPipeClientProcessId(raw, out var pid) && pid != 0)
                    return FromProcessId(unchecked((int)pid));
                return FromPipeUsers(raw);
            }
            finally
            {
                if (added)
                    handle.DangerousRelease();
            }
        }
        catch
        {
            return null;
        }
    }

    public static string? FromPuttyMappingName(string? name)
    {
        if (name is null || !PageantMapping.TryGetPuttyRequestThreadId(name, out var threadId))
            return null;
        try
        {
            var thread = OpenThread(0x1040, false, threadId); // QUERY | QUERY_LIMITED
            if (thread != IntPtr.Zero)
            {
                try
                {
                    var pid = GetProcessIdOfThread(thread);
                    if (pid != 0)
                        return FromProcessId(unchecked((int)pid));
                }
                finally
                {
                    CloseHandle(thread);
                }
            }

            return FromThreadSnapshot(threadId);
        }
        catch
        {
            return null;
        }
    }

    private static string? FromProcessId(int pid, string? preferredTitle)
    {
        try
        {
            if (pid <= 0)
                return null;
            using var process = Process.GetProcessById(pid);
            var description = TryFileDescription(process) ?? TryFileDescriptionFromImage(pid);
            var title = Sanitize(preferredTitle) ?? FirstWindowTitle(process);
            return Format(description, title, process.ProcessName);
        }
        catch
        {
            return null;
        }
    }

    private static string? FromPipeUsers(IntPtr handle)
    {
        var size = 8 + (IntPtr.Size * 16);
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            var status = NtQueryInformationFile(
                handle,
                out _,
                buffer,
                (uint)size,
                47); // FileProcessIdsUsingFileInformation
            if (status != 0)
                return null;

            var count = Marshal.ReadInt32(buffer);
            var ours = (uint)Environment.ProcessId;
            var offset = IntPtr.Size;
            for (var i = 0; i < count && i < 16; i++)
            {
                var pid = IntPtr.Size == 8
                    ? (uint)Marshal.ReadInt64(buffer, offset + (i * 8))
                    : (uint)Marshal.ReadInt32(buffer, offset + (i * 4));
                if (pid != 0 && pid != ours)
                    return FromProcessId(unchecked((int)pid));
            }

            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string? FromThreadSnapshot(uint threadId)
    {
        var snap = CreateToolhelp32Snapshot(0x4, 0);
        if (snap == IntPtr.Zero || snap == new IntPtr(-1))
            return null;
        try
        {
            var entry = new ThreadEntry32 { DwSize = (uint)Marshal.SizeOf<ThreadEntry32>() };
            if (!Thread32First(snap, ref entry))
                return null;
            do
            {
                if (entry.Th32ThreadId == threadId && entry.Th32OwnerProcessId != 0)
                    return FromProcessId(unchecked((int)entry.Th32OwnerProcessId));
            }
            while (Thread32Next(snap, ref entry));
            return null;
        }
        finally
        {
            CloseHandle(snap);
        }
    }

    private static string? TryFileDescriptionFromImage(int pid)
    {
        var process = OpenProcess(0x1000, false, unchecked((uint)pid)); // PROCESS_QUERY_LIMITED_INFORMATION
        if (process == IntPtr.Zero)
            return null;
        try
        {
            var buffer = new StringBuilder(512);
            var capacity = (uint)buffer.Capacity;
            if (!QueryFullProcessImageName(process, 0, buffer, ref capacity))
                return null;
            var path = buffer.ToString();
            if (string.IsNullOrWhiteSpace(path))
                return null;
            return ShortProduct(FileVersionInfo.GetVersionInfo(path).FileDescription);
        }
        catch
        {
            return null;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static string? TryFileDescription(Process process)
    {
        try
        {
            var description = process.MainModule?.FileVersionInfo.FileDescription;
            return ShortProduct(description);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static string? FirstWindowTitle(Process process)
    {
        var main = Sanitize(process.MainWindowTitle);
        if (main is not null)
            return main;

        string? found = null;
        var pid = unchecked((uint)process.Id);
        EnumWindows((hwnd, _) =>
        {
            if (found is not null)
                return false;
            GetWindowThreadProcessId(hwnd, out var windowPid);
            if (windowPid != pid || !IsWindowVisible(hwnd))
                return true;
            found = WindowTitle(hwnd);
            return found is null;
        }, IntPtr.Zero);
        return found;
    }

    private static string? WindowTitle(IntPtr hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        if (length <= 0)
            return null;
        var buffer = new StringBuilder(length + 1);
        if (GetWindowText(hwnd, buffer, buffer.Capacity) <= 0)
            return null;
        return Sanitize(buffer.ToString());
    }

    internal static string? ShortProduct(string? description)
    {
        var text = Sanitize(description);
        if (text is null)
            return null;
        var colon = text.IndexOf(':');
        if (colon is > 0 and <= 32 && !text.AsSpan(0, colon).Contains(' '))
            return text[..colon];
        return text;
    }

    internal static string? Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var chars = value.Trim().ToCharArray();
        var dest = new char[chars.Length];
        var n = 0;
        var space = false;
        foreach (var c in chars)
        {
            if (char.IsWhiteSpace(c))
            {
                space = n > 0;
                continue;
            }

            if (space)
            {
                dest[n++] = ' ';
                space = false;
            }

            dest[n++] = c;
        }

        if (n == 0)
            return null;
        var text = new string(dest, 0, n);
        return LooksLikePath(text) ? null : text;
    }

    private static bool LooksLikePath(string text)
    {
        if (text.Contains(@":\", StringComparison.Ordinal) || text.StartsWith(@"\\", StringComparison.Ordinal))
            return true;
        if (text.Contains('/', StringComparison.Ordinal) && (text.Contains('.', StringComparison.Ordinal) || text.StartsWith('/')))
            return true;
        return false;
    }

    private static string? StripExe(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    private static string? Truncate(string? text)
    {
        if (text is null || text.Length <= MaxLabelLength)
            return text;
        return text[..(MaxLabelLength - 1)] + "…";
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct IoStatusBlock
    {
        public IntPtr Status;
        public IntPtr Information;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ThreadEntry32
    {
        public uint DwSize;
        public uint CntUsage;
        public uint Th32ThreadId;
        public uint Th32OwnerProcessId;
        public int TpBasePri;
        public int TpDeltaPri;
        public uint DwFlags;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationFile(
        IntPtr fileHandle,
        out IoStatusBlock ioStatusBlock,
        IntPtr fileInformation,
        uint length,
        int fileInformationClass);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(IntPtr pipe, out uint clientProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenThread(uint dwDesiredAccess, bool bInheritHandle, uint dwThreadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetProcessIdOfThread(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool Thread32First(IntPtr hSnapshot, ref ThreadEntry32 lpte);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool Thread32Next(IntPtr hSnapshot, ref ThreadEntry32 lpte);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(
        IntPtr hProcess,
        uint dwFlags,
        StringBuilder lpExeName,
        ref uint lpdwSize);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder lpString, int nMaxCount);
}
