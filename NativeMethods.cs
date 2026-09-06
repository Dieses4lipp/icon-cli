using System.Runtime.InteropServices;

namespace IconCli;

internal static class NativeMethods
{
    public const int SHCNE_UPDATEITEM = 0x00002000;
    public const int SHCNE_ASSOCCHANGED = 0x08000000;
    public const int SHCNF_PATHW = 0x0005;
    public const int SHCNF_FLUSH = 0x1000;

    [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = false)]
    public static extern int SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int PrivateExtractIcons(
        string lpszFile,
        int nIconIndex,
        int cxIcon,
        int cyIcon,
        IntPtr[] phicon,
        int[] piconid,
        int nIcons,
        int flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr hIcon);
}
