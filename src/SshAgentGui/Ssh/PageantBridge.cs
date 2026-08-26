using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace SshAgentGui.Ssh;

internal sealed class PageantBridge : IDisposable
{
    public const string ClassName = "Pageant";
    public const uint AgentCopyDataId = 0x804e50ba;

    private readonly IOpenSshAgentPipe _pipe;
    private readonly PageantConfirm _confirm;
    private readonly Dispatcher _ui;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private WndProc? _wndProc;
    private PageantPipeServer? _pageantPipe;
    private IntPtr _hwnd;
    private uint _nativeThreadId;
    private bool _started;
    private bool _disposed;

    private PageantBridge(IOpenSshAgentPipe pipe, PageantConfirm confirm, Dispatcher ui)
    {
        _pipe = pipe;
        _confirm = confirm;
        _ui = ui;
        _thread = new Thread(ThreadMain)
        {
            IsBackground = true,
            Name = "PageantBridge",
        };
        _thread.SetApartmentState(ApartmentState.STA);
    }

    public static bool IsTaken() => FindWindowW(ClassName, ClassName) != IntPtr.Zero;

    public static PageantBridge? TryStart(IOpenSshAgentPipe pipe, PageantConfirm confirm, Dispatcher ui)
    {
        if (IsTaken())
            return null;

        var bridge = new PageantBridge(pipe, confirm, ui);
        bridge._thread.Start();
        if (!bridge._ready.Wait(TimeSpan.FromSeconds(5)) || bridge._hwnd == IntPtr.Zero)
        {
            bridge.Dispose();
            return null;
        }

        bridge.StartPipe();
        return bridge;
    }

    private void StartPipe()
    {
        try
        {
            _pageantPipe = new PageantPipeServer(PageantPipeName.ForCurrentUser(), _pipe, ConfirmOnUi);
            _pageantPipe.Start();
        }
        catch
        {
            _pageantPipe?.Dispose();
            _pageantPipe = null;
        }
    }

    private void ThreadMain()
    {
        try
        {
            _nativeThreadId = GetCurrentThreadId();
            _wndProc = WindowProc;
            var cls = new WndClassEx
            {
                cbSize = (uint)Marshal.SizeOf<WndClassEx>(),
                lpfnWndProc = _wndProc,
                hInstance = GetModuleHandleW(null),
                lpszClassName = ClassName,
            };
            if (RegisterClassExW(ref cls) == 0)
                return;

            _hwnd = CreateWindowExW(
                0,
                ClassName,
                ClassName,
                0,
                0, 0, 0, 0,
                IntPtr.Zero,
                IntPtr.Zero,
                cls.hInstance,
                IntPtr.Zero);
            if (_hwnd == IntPtr.Zero)
                return;

            ShowWindow(_hwnd, 0);
            _started = true;
        }
        finally
        {
            _ready.Set();
        }

        if (_hwnd == IntPtr.Zero)
        {
            UnregisterClassW(ClassName, GetModuleHandleW(null));
            return;
        }

        while (GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }

        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        UnregisterClassW(ClassName, GetModuleHandleW(null));
    }

    private IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == 0x0010) // WM_CLOSE
        {
            DestroyWindow(hwnd);
            return IntPtr.Zero;
        }

        if (msg == 0x0002) // WM_DESTROY
        {
            PostQuitMessage(0);
            return IntPtr.Zero;
        }

        if (msg == 0x004A) // WM_COPYDATA
            return OnCopyData(wParam, lParam) ? new IntPtr(1) : IntPtr.Zero;

        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private bool OnCopyData(IntPtr sender, IntPtr lParam)
    {
        var cds = Marshal.PtrToStructure<CopyDataStruct>(lParam);
        if (cds.dwData != (UIntPtr)AgentCopyDataId)
            return false;

        var name = ReadAnsiName(cds.lpData, cds.cbData);
        if (name is null || !PageantMapping.IsSafeName(name))
            return false;

        var mapping = OpenFileMappingA(0x0002, false, name);
        if (mapping == IntPtr.Zero)
            return false;

        var view = MapViewOfFile(mapping, 0x0002, 0, 0, UIntPtr.Zero);
        if (view == IntPtr.Zero)
        {
            CloseHandle(mapping);
            return false;
        }

        try
        {
            var size = MappedSize(view);
            if (size < 5)
                return false;

            var header = new byte[4];
            Marshal.Copy(view, header, 0, 4);
            var length = SshAgentFrame.ReadUInt32Be(header);
            var total = 4 + (int)length;
            if (length < 1 || total > SshAgentFrame.MaxLength || total > size)
                return false;

            var frame = new byte[total];
            Marshal.Copy(view, frame, 0, total);
            var caller = PageantCaller.FromWindow(sender) ?? PageantCaller.FromPuttyMappingName(name);
            var response = PageantDispatch.Handle(frame, _pipe, ConfirmOnUi, caller);
            if (response is null || response.Length > size)
                return false;

            Marshal.Copy(response, 0, view, response.Length);
            return true;
        }
        finally
        {
            UnmapViewOfFile(view);
            CloseHandle(mapping);
        }
    }

    private bool ConfirmOnUi(byte[] blob, PageantCallerInfo? caller)
    {
        if (_ui.CheckAccess())
            return _confirm(blob, caller);
        return _ui.Invoke(() => _confirm(blob, caller));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _pageantPipe?.Dispose();
        _pageantPipe = null;
        if (_started && _hwnd != IntPtr.Zero)
            PostMessageW(_hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero);
        if (!_thread.Join(TimeSpan.FromSeconds(5)) && _nativeThreadId != 0)
            PostThreadMessageW(_nativeThreadId, 0x0012, IntPtr.Zero, IntPtr.Zero);
        _ready.Dispose();
    }

    private static string? ReadAnsiName(IntPtr ptr, int cb)
    {
        if (ptr == IntPtr.Zero || cb <= 0 || cb > PageantMapping.MaxNameLength + 1)
            return null;
        var bytes = new byte[cb];
        Marshal.Copy(ptr, bytes, 0, cb);
        var n = Array.IndexOf(bytes, (byte)0);
        if (n <= 0)
            return null;
        return Encoding.ASCII.GetString(bytes, 0, n);
    }

    private static int MappedSize(IntPtr view)
    {
        if (VirtualQuery(view, out var info, (UIntPtr)Marshal.SizeOf<MemoryBasicInformation>()) == UIntPtr.Zero)
            return 0;
        return (int)Math.Min(info.RegionSize.ToUInt64(), SshAgentFrame.MaxLength);
    }

    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct CopyDataStruct
    {
        public UIntPtr dwData;
        public int cbData;
        public IntPtr lpData;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint cbSize;
        public uint style;
        public WndProc lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryBasicInformation
    {
        public IntPtr BaseAddress;
        public IntPtr AllocationBase;
        public uint AllocationProtect;
        public ushort PartitionId;
        public UIntPtr RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowW(string lpClassName, string lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WndClassEx lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool UnregisterClassW(string lpClassName, IntPtr hInstance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetMessageW(out Message lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Message lpMsg);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DispatchMessageW(ref Message lpMsg);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int nExitCode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessageW(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr OpenFileMappingA(uint dwDesiredAccess, bool bInheritHandle, string lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr MapViewOfFile(
        IntPtr hFileMappingObject,
        uint dwDesiredAccess,
        uint dwFileOffsetHigh,
        uint dwFileOffsetLow,
        UIntPtr dwNumberOfBytesToMap);

    [DllImport("kernel32.dll")]
    private static extern bool UnmapViewOfFile(IntPtr lpBaseAddress);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll")]
    private static extern UIntPtr VirtualQuery(IntPtr lpAddress, out MemoryBasicInformation lpBuffer, UIntPtr dwLength);
}
