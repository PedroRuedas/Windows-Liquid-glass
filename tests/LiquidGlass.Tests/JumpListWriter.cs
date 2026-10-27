using System.Runtime.InteropServices;

namespace LiquidGlass.Tests;

/// <summary>
/// Grava listas de atalhos de verdade, pela mesma API que os apps usam (ICustomDestinationList),
/// para um AppID de teste. O Windows escreve o arquivo; os testes leem o que ele escreveu.
/// </summary>
internal static class JumpListWriter
{
    public const string Notepad = @"C:\Windows\System32\notepad.exe";

    private const ushort VtBool = 11;
    private const ushort VtLpwstr = 31;

    private static readonly Guid DestinationListClass = new("77F10CF0-3DB5-4966-B520-B7C54FD35ED6");
    private static readonly Guid ObjectCollectionClass = new("2D3468C1-36A7-43B6-AC24-D3F02FD9607A");
    private static readonly Guid ShellLinkClass = new("00021401-0000-0000-C000-000000000046");
    private static readonly PropertyKey Title = new(new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), 2);
    private static readonly PropertyKey IsSeparator = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 6);

    /// <summary>Tarefas (<c>null</c> é uma divisória) e um grupo com nome, todos abrindo <paramref name="target"/>.</summary>
    public static void Write(string appId, IReadOnlyList<string?> tasks, string categoryName, IReadOnlyList<string> categoryItems, string target = Notepad)
    {
        var list = (ICustomDestinationList)Create(DestinationListClass);
        list.SetAppID(appId);
        var arrayId = typeof(IObjectArray).GUID;
        list.BeginList(out _, ref arrayId, out _);
        list.AppendCategory(categoryName, Collection(categoryItems.Select(title => Link(title, target))));
        list.AddUserTasks(Collection(tasks.Select(title => Link(title, target))));
        list.CommitList();
    }

    public static void Delete(string appId)
    {
        ((ICustomDestinationList)Create(DestinationListClass)).DeleteList(appId);
    }

    private static object Create(Guid classId) => Activator.CreateInstance(Type.GetTypeFromCLSID(classId, throwOnError: true)!)!;

    private static IObjectArray Collection(IEnumerable<object> items)
    {
        var collection = (IObjectCollection)Create(ObjectCollectionClass);
        foreach (var item in items)
        {
            collection.AddObject(item);
        }

        return (IObjectArray)collection;
    }

    private static object Link(string? title, string target)
    {
        var link = (IShellLinkW)Create(ShellLinkClass);
        link.SetPath(target);
        link.SetArguments(title ?? string.Empty);

        var key = title is null ? IsSeparator : Title;
        var value = title is null ? PropVariant.FromBoolean(true) : PropVariant.FromString(title);
        try
        {
            var store = (IPropertyStore)link;
            store.SetValue(ref key, ref value);
            store.Commit();
        }
        finally
        {
            value.Free();
        }

        return link;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PropertyKey(Guid formatId, uint propertyId)
    {
        public readonly Guid FormatId = formatId;
        public readonly uint PropertyId = propertyId;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)]
        private ushort _valueType;

        [FieldOffset(8)]
        private nint _pointer;

        [FieldOffset(8)]
        private short _boolean;

        public static PropVariant FromString(string text) => new() { _valueType = VtLpwstr, _pointer = Marshal.StringToCoTaskMemUni(text) };

        public static PropVariant FromBoolean(bool flag) => new() { _valueType = VtBool, _boolean = (short)(flag ? -1 : 0) };

        public void Free()
        {
            if (_valueType == VtLpwstr)
            {
                Marshal.FreeCoTaskMem(_pointer);
            }

            this = default;
        }
    }

    [ComImport]
    [Guid("6332DEBF-87B5-4670-90C0-5E57B408A49E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICustomDestinationList
    {
        void SetAppID([MarshalAs(UnmanagedType.LPWStr)] string appId);

        void BeginList(out uint minSlots, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object removed);

        void AppendCategory([MarshalAs(UnmanagedType.LPWStr)] string category, IObjectArray items);

        void AppendKnownCategory(int category);

        void AddUserTasks(IObjectArray tasks);

        void CommitList();

        void GetRemovedDestinations(ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object removed);

        void DeleteList([MarshalAs(UnmanagedType.LPWStr)] string appId);

        void AbortList();
    }

    [ComImport]
    [Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectArray
    {
        void GetCount(out uint count);

        void GetAt(uint index, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object item);
    }

    [ComImport]
    [Guid("5632B1A4-E38A-400A-928A-D4CD63230295")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectCollection
    {
        void GetCount(out uint count);

        void GetAt(uint index, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object item);

        void AddObject([MarshalAs(UnmanagedType.Interface)] object item);

        void AddFromArray(IObjectArray items);

        void RemoveObjectAt(uint index);

        void Clear();
    }

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath(nint file, int maxLength, nint findData, uint flags);

        void GetIDList(out nint idList);

        void SetIDList(nint idList);

        void GetDescription(nint name, int maxLength);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);

        void GetWorkingDirectory(nint directory, int maxLength);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);

        void GetArguments(nint arguments, int maxLength);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);

        void GetHotkey(out short hotkey);

        void SetHotkey(short hotkey);

        void GetShowCmd(out int showCommand);

        void SetShowCmd(int showCommand);

        void GetIconLocation(nint path, int maxLength, out int index);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);

        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);

        void Resolve(nint window, uint flags);

        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count);

        void GetAt(uint index, out PropertyKey key);

        void GetValue(ref PropertyKey key, out PropVariant value);

        void SetValue(ref PropertyKey key, ref PropVariant value);

        void Commit();
    }
}
