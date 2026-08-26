using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace SshAgentGui.Ssh;

internal sealed class PageantCallerInfo
{
    public const string UnknownName = "A program";

    public string? Label { get; init; }
    public int? Pid { get; init; }
    public string? ProcessName { get; init; }
    public string? ImagePath { get; init; }
    public string? Description { get; init; }
    public string? WindowTitle { get; init; }

    public string DisplayName =>
        FirstNonEmpty(Description, ProcessName, Label) ?? UnknownName;

    public string? WindowSubtitle =>
        string.IsNullOrWhiteSpace(WindowTitle)
        || WindowTitle.Equals(DisplayName, StringComparison.OrdinalIgnoreCase)
            ? null
            : WindowTitle;

    public string? ProcessLine
    {
        get
        {
            var pidText = Pid is { } pid ? "(PID " + pid.ToString(CultureInfo.InvariantCulture) + ")" : null;
            if (ProcessName is not null && pidText is not null)
                return ProcessName + " " + pidText;
            return ProcessName ?? pidText;
        }
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }
}

internal static class PageantCaller
{
    public const int MaxLabelLength = 80;

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

    public static PageantCallerInfo? FromProcessId(int pid) => FromProcessId(pid, preferredTitle: null);

    public static PageantCallerInfo? FromWindow(IntPtr hwnd)
    {
        try
        {
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
                return null;
            var title = WindowTitle(hwnd);
            _ = GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0)
            {
                var label = Format(null, title, null);
                return label is null ? null : new PageantCallerInfo { Label = label, WindowTitle = title };
            }

            return FromProcessId(unchecked((int)pid), title);
        }
        catch
        {
            return null;
        }
    }

    public static PageantCallerInfo? FromPipe(SafePipeHandle handle)
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

    public static PageantCallerInfo? FromPuttyMappingName(string? name)
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

    private static PageantCallerInfo? FromProcessId(int pid, string? preferredTitle)
    {
        if (pid <= 0)
            return null;

        var imagePath = TryGetImagePath(pid);
        Process? process = null;
        try
        {
            process = Process.GetProcessById(pid);
        }
        catch
        {
        }

        try
        {
            imagePath ??= TryMainModulePath(process);
            if (process is null && imagePath is null)
                return null;

            var description = TryFileDescription(process) ?? TryFileDescriptionFromPath(imagePath);
            var title = Sanitize(preferredTitle) ?? (process is null ? null : FirstWindowTitle(process));
            var processName = process is null ? null : StripExe(process.ProcessName);
            var label = Format(description, title, processName);
            return new PageantCallerInfo
            {
                Label = label,
                Pid = pid,
                ProcessName = processName,
                ImagePath = imagePath,
                Description = description,
                WindowTitle = title,
            };
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static PageantCallerInfo? FromPipeUsers(IntPtr handle)
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

    private static PageantCallerInfo? FromThreadSnapshot(uint threadId)
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

    private static string? TryGetImagePath(int pid)
    {
        var process = OpenProcess(0x1000, false, unchecked((uint)pid)); // PROCESS_QUERY_LIMITED_INFORMATION
        if (process == IntPtr.Zero)
            return null;
        try
        {
            var buffer = new StringBuilder(32768);
            var capacity = (uint)buffer.Capacity;
            if (!QueryFullProcessImageName(process, 0, buffer, ref capacity))
                return null;
            var path = buffer.ToString();
            return string.IsNullOrWhiteSpace(path) ? null : path;
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

    private static string? TryMainModulePath(Process? process)
    {
        if (process is null)
            return null;
        try
        {
            var path = process.MainModule?.FileName;
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return null;
        }
    }

    private static string? TryFileDescriptionFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        try
        {
            return ShortProduct(FileVersionInfo.GetVersionInfo(path).FileDescription);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryFileDescription(Process? process)
    {
        if (process is null)
            return null;
        try
        {
            var description = process.MainModule?.FileVersionInfo.FileDescription;
            return ShortProduct(description);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
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
