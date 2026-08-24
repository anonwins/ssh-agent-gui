using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace SshAgentGui.Ssh;

internal static class PageantPipeName
{
    public const string Input = "Pageant";
    private const uint CrossProcess = 1;
    private const int BlockSize = 16;

    public static string ForCurrentUser() => $"pageant.{UserName()}.{Obfuscate(Input)}";

    public static string UserName()
    {
        var name = Environment.UserName;
        var slash = name.LastIndexOfAny(['\\', '/']);
        return slash >= 0 ? name[(slash + 1)..] : name;
    }

    public static string Obfuscate(string realname)
    {
        var cryptlen = realname.Length + 1;
        cryptlen = (cryptlen + BlockSize - 1) / BlockSize * BlockSize;
        var data = new byte[cryptlen];
        Encoding.ASCII.GetBytes(realname, 0, realname.Length, data, 0);
        _ = CryptProtectMemory(data, (uint)data.Length, CrossProcess);
        return Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    }

    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptProtectMemory(byte[] pData, uint cbData, uint dwFlags);
}
