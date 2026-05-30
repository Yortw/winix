#nullable enable
using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Winix.Trash;

// Objective-C runtime interop for the macOS Trash backend. This is the suite's first Obj-C interop:
// we call the Foundation method -[NSFileManager trashItemAtURL:resultingItemURL:error:] through the
// objc runtime's objc_msgSend, building the NSString/NSURL arguments the same way.
//
// ABI notes (verified against Apple's Objective-C runtime + NSFileManager docs):
//  * NativeAOT needs a CONCRETE objc_msgSend overload per call shape — there is no variadic P/Invoke
//    — so each distinct (return, args) signature gets its own [LibraryImport] with EntryPoint =
//    "objc_msgSend" and a distinct managed name.
//  * For id-returning and BOOL-returning selectors on both arm64 and x86_64, the plain objc_msgSend
//    entry point is correct. The _stret / _fpret variants are only for struct / floating-point
//    returns and must NOT be used here (none of our selectors return a struct or float).
//  * Objective-C BOOL is a signed char, so [return: MarshalAs(UnmanagedType.U1)] bool is the correct
//    marshalling on both architectures. macOS CI (macos-latest) is arm64; these signatures are
//    ABI-correct there.
//  * UTF8String returns a pointer into the NSString's internal buffer — we copy it immediately via
//    Marshal.PtrToStringUTF8 and never hold the pointer. The autoreleased NSStrings/NSURL stay valid
//    for the duration of this synchronous call chain (no autorelease pool drains in between).
[SupportedOSPlatform("macos")]
internal sealed partial class MacOsTrashBackend
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";

    [LibraryImport(LibObjC, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr objc_getClass(string name);

    [LibraryImport(LibObjC, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr sel_registerName(string name);

    // 0-arg send returning an object (defaultManager, localizedDescription, UTF8String).
    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial IntPtr objc_msgSend(IntPtr self, IntPtr sel);

    // 1-arg send taking a UTF-8 C string (stringWithUTF8String:). The marshaller hands the selector a
    // const char* — exactly what stringWithUTF8String: expects.
    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr objc_msgSend_str(IntPtr self, IntPtr sel, string utf8);

    // 1-arg send taking an object pointer (fileURLWithPath:).
    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial IntPtr objc_msgSend_ptr(IntPtr self, IntPtr sel, IntPtr arg);

    // trashItemAtURL:resultingItemURL:error: — BOOL return; args (NSURL*, NSURL** out, NSError** out).
    // We pass IntPtr.Zero for the resultingItemURL out-param (we don't need the moved URL back), and
    // a ref IntPtr for the NSError out-param (starts Zero, receives an autoreleased NSError* on failure).
    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool objc_msgSend_trash(IntPtr self, IntPtr sel, IntPtr url, IntPtr resultingItemUrlOut, ref IntPtr error);

    [LibraryImport("libc", EntryPoint = "getuid")]
    private static partial uint GetUidNative();

    // Autorelease pool management. A CLI main thread has no run loop, so there is no ambient
    // autorelease pool; without one the Obj-C runtime logs "autoreleased with no pool in place -
    // just leaking" to stderr for every autoreleased object (NSString/NSURL/NSError factory results)
    // and leaks them. We push our own pool around the Foundation call chain and pop it once we have
    // copied out everything we need into managed memory. Returns/takes an opaque pool token.
    [LibraryImport(LibObjC, EntryPoint = "objc_autoreleasePoolPush")]
    private static partial IntPtr objc_autoreleasePoolPush();

    [LibraryImport(LibObjC, EntryPoint = "objc_autoreleasePoolPop")]
    private static partial void objc_autoreleasePoolPop(IntPtr pool);

    /// <summary>Trashes a single existing item via Foundation's <c>NSFileManager</c>. Returns
    /// <c>(true, null)</c> on success; on failure returns <c>(false, message)</c> where the message is
    /// the NSError's <c>localizedDescription</c> (an OS-provided string — safe to surface even under
    /// InvariantGlobalization, unlike a .NET framework <c>ex.Message</c>) or a generic fallback. Does
    /// not catch — the caller wraps. Returns <c>(false, "macOS Foundation unavailable")</c> if any
    /// class/selector lookup fails (should never happen on a real macOS host).</summary>
    private static (bool ok, string? error) TrashViaFoundation(string fullPath)
    {
        IntPtr nsFileManagerClass = objc_getClass("NSFileManager");
        IntPtr nsStringClass = objc_getClass("NSString");
        IntPtr nsUrlClass = objc_getClass("NSURL");
        IntPtr selDefaultManager = sel_registerName("defaultManager");
        IntPtr selStringWithUtf8 = sel_registerName("stringWithUTF8String:");
        IntPtr selFileUrlWithPath = sel_registerName("fileURLWithPath:");
        IntPtr selTrashItem = sel_registerName("trashItemAtURL:resultingItemURL:error:");

        if (nsFileManagerClass == IntPtr.Zero
            || nsStringClass == IntPtr.Zero
            || nsUrlClass == IntPtr.Zero
            || selDefaultManager == IntPtr.Zero
            || selStringWithUtf8 == IntPtr.Zero
            || selFileUrlWithPath == IntPtr.Zero
            || selTrashItem == IntPtr.Zero)
        {
            return (false, "macOS Foundation unavailable");
        }

        // The factory sends below return autoreleased objects; bracket them in an explicit pool so the
        // runtime has somewhere to drain them (no run loop on a CLI main thread). Everything we need
        // out of these objects is copied into managed memory before the pool is popped.
        IntPtr pool = objc_autoreleasePoolPush();
        try
        {
            IntPtr manager = objc_msgSend(nsFileManagerClass, selDefaultManager);
            IntPtr nsString = objc_msgSend_str(nsStringClass, selStringWithUtf8, fullPath);
            IntPtr nsUrl = objc_msgSend_ptr(nsUrlClass, selFileUrlWithPath, nsString);
            if (manager == IntPtr.Zero || nsString == IntPtr.Zero || nsUrl == IntPtr.Zero)
            {
                return (false, "failed to move to Trash.");
            }

            IntPtr errorPtr = IntPtr.Zero;
            bool ok = objc_msgSend_trash(manager, selTrashItem, nsUrl, IntPtr.Zero, ref errorPtr);
            if (ok)
            {
                return (true, null);
            }

            // UnwrapNsError copies the message into a managed string before we pop the pool.
            string? message = UnwrapNsError(errorPtr);
            return (false, message ?? "failed to move to Trash.");
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }

    /// <summary>Extracts a human-readable message from an <c>NSError*</c> via
    /// <c>-localizedDescription</c> → <c>-UTF8String</c>. Returns null when the error pointer is null
    /// or the chain yields no string (caller substitutes a generic message).</summary>
    private static string? UnwrapNsError(IntPtr errorPtr)
    {
        if (errorPtr == IntPtr.Zero)
        {
            return null;
        }

        IntPtr selLocalizedDescription = sel_registerName("localizedDescription");
        IntPtr selUtf8String = sel_registerName("UTF8String");
        if (selLocalizedDescription == IntPtr.Zero || selUtf8String == IntPtr.Zero)
        {
            return null;
        }

        IntPtr description = objc_msgSend(errorPtr, selLocalizedDescription);
        if (description == IntPtr.Zero)
        {
            return null;
        }

        IntPtr utf8 = objc_msgSend(description, selUtf8String);
        if (utf8 == IntPtr.Zero)
        {
            return null;
        }

        // Copy out immediately — utf8 points into the NSString's internal buffer.
        return Marshal.PtrToStringUTF8(utf8);
    }
}
