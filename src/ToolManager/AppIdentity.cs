namespace ToolManager;

using System.Security.Cryptography;
using System.Security.Principal;
using ToolManager.Persistence;

internal static class AppIdentity
{
#if TOOLMANAGER_DEV
    public static bool IsDevelopment => true;
#else
    public static bool IsDevelopment => false;
#endif

    public static string Name => IsDevelopment ? "Tool Manager (DEV)" : "Tool Manager";

    public static string TaskPrefix => IsDevelopment ? "PortableToolManager-DEV-" : "PortableToolManager-";

    public static string InstanceName
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Store.Root)).ToUpperInvariant();
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)))[..16];
            return "Local\\" + TaskPrefix + identity.User!.Value + "-" + hash;
        }
    }
}
