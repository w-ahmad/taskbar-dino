using Windows.Win32.Foundation;
using Windows.Win32.System.Com;

namespace TaskbarDino.Win32;

/// <summary>Tiny helpers for the raw (unmarshalled) COM pointers CsWin32 generates.</summary>
internal static unsafe class Com
{
    /// <summary>Releases the object behind <paramref name="ptr"/> (if any) and nulls the pointer.</summary>
    public static void Release<T>(ref T* ptr)
        where T : unmanaged
    {
        if (ptr is not null)
        {
            ((IUnknown*)ptr)->Release();
            ptr = null;
        }
    }

    /// <summary>Queries <paramref name="obj"/> for interface <typeparamref name="T"/> (throws on failure).</summary>
    public static T* Query<T>(void* obj, in Guid iid)
        where T : unmanaged
    {
        void* result;
        Guid guid = iid;
        ((IUnknown*)obj)->QueryInterface(&guid, &result).ThrowOnFailure();
        return (T*)result;
    }
}
