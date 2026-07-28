using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MYTC.App.Windows;

public static class TaskbarIdentity
{
    public const string AppUserModelId = "AIDELL.MYTC.FileManager";

    private const ushort VtEmpty = 0;
    private const ushort VtLpWStr = 31;
    private static readonly Guid AppUserModelPropertySet =
        new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
    private static readonly Guid PropertyStoreInterfaceId =
        new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
    private static readonly PropertyKey AppUserModelIdKey =
        new(AppUserModelPropertySet, 5);
    private static readonly PropertyKey RelaunchCommandKey =
        new(AppUserModelPropertySet, 2);
    private static readonly PropertyKey RelaunchDisplayNameKey =
        new(AppUserModelPropertySet, 4);
    private static readonly PropertyKey RelaunchIconKey =
        new(AppUserModelPropertySet, 3);

    public static bool TryInitializeProcessIdentity()
    {
        try
        {
            return SetCurrentProcessExplicitAppUserModelID(
                AppUserModelId) >= 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    public static bool TryApplyWindowProperties(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var handle = new WindowInteropHelper(window).Handle;
        var executablePath = Environment.ProcessPath;
        if (handle == IntPtr.Zero ||
            string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        if (!TryGetPropertyStore(handle, out var propertyStore))
        {
            return false;
        }

        try
        {
            return TrySetString(
                    propertyStore,
                    AppUserModelIdKey,
                    AppUserModelId) &&
                TrySetString(
                    propertyStore,
                    RelaunchCommandKey,
                    $"\"{executablePath}\"") &&
                TrySetString(
                    propertyStore,
                    RelaunchDisplayNameKey,
                    "MYTC 四窗格资源管理器") &&
                TrySetString(
                    propertyStore,
                    RelaunchIconKey,
                    $"{executablePath},0");
        }
        finally
        {
            _ = Marshal.FinalReleaseComObject(propertyStore);
        }
    }

    public static void TryClearWindowProperties(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero ||
            !TryGetPropertyStore(handle, out var propertyStore))
        {
            return;
        }

        try
        {
            var empty = new PropVariant
            {
                VariantType = VtEmpty,
            };
            ClearValue(propertyStore, AppUserModelIdKey, ref empty);
            ClearValue(propertyStore, RelaunchCommandKey, ref empty);
            ClearValue(propertyStore, RelaunchDisplayNameKey, ref empty);
            ClearValue(propertyStore, RelaunchIconKey, ref empty);
        }
        finally
        {
            _ = Marshal.FinalReleaseComObject(propertyStore);
        }
    }

    private static bool TryGetPropertyStore(
        IntPtr windowHandle,
        out IPropertyStore propertyStore)
    {
        try
        {
            var interfaceId = PropertyStoreInterfaceId;
            return SHGetPropertyStoreForWindow(
                    windowHandle,
                    ref interfaceId,
                    out propertyStore) >= 0;
        }
        catch (DllNotFoundException)
        {
            propertyStore = null!;
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            propertyStore = null!;
            return false;
        }
    }

    private static bool TrySetString(
        IPropertyStore propertyStore,
        PropertyKey key,
        string value)
    {
        var variant = new PropVariant
        {
            VariantType = VtLpWStr,
            PointerValue = Marshal.StringToCoTaskMemUni(value),
        };
        try
        {
            return propertyStore.SetValue(ref key, ref variant) >= 0;
        }
        finally
        {
            Marshal.FreeCoTaskMem(variant.PointerValue);
        }
    }

    private static void ClearValue(
        IPropertyStore propertyStore,
        PropertyKey key,
        ref PropVariant empty)
    {
        _ = propertyStore.SetValue(ref key, ref empty);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(
        string appUserModelId);

    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(
        IntPtr windowHandle,
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IPropertyStore propertyStore);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private readonly record struct PropertyKey(Guid FormatId, uint PropertyId);

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct PropVariant
    {
        [FieldOffset(0)]
        public ushort VariantType;

        [FieldOffset(8)]
        public IntPtr PointerValue;
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig]
        int GetCount(out uint propertyCount);

        [PreserveSig]
        int GetAt(uint propertyIndex, out PropertyKey key);

        [PreserveSig]
        int GetValue(ref PropertyKey key, out PropVariant value);

        [PreserveSig]
        int SetValue(ref PropertyKey key, ref PropVariant value);

        [PreserveSig]
        int Commit();
    }
}
