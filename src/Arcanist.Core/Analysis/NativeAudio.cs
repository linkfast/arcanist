using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Arcanist.Core.Analysis;

/// <summary>
/// Bindings to <c>native/arcanist-audio</c> (Rust): <c>arcanist_audio.dll</c>, <c>libarcanist_audio.so</c> or
/// <c>libarcanist_audio.dylib</c> next to the server. Every result comes back as JSON allocated by the library and
/// released with <see cref="Free"/>.
/// </summary>
internal static unsafe partial class NativeAudio
{
    private const string Library = "arcanist_audio";

    public const int StatusOk = 0;
    public const int StatusUnsupported = 1;
    public const int StatusInvalid = 2;
    public const int StatusPanic = 3;

    /// <summary>Reads the media through managed callbacks; see <c>ArcanistSource</c> in <c>lib.rs</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Source
    {
        public nint Context;
        public delegate* unmanaged[Cdecl]<nint, byte*, int, long> Read;
        public delegate* unmanaged[Cdecl]<nint, long, int, long> Seek;
        public long Length;
    }

    [LibraryImport(Library, EntryPoint = "arcanist_audio_version")]
    public static partial uint Version();

    [LibraryImport(Library, EntryPoint = "arcanist_audio_analyze", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int Analyze(Source* source, string? extension, uint flags, out nint json);

    [LibraryImport(Library, EntryPoint = "arcanist_audio_analyzer_new")]
    public static partial nint AnalyzerNew(uint rate, uint channels, uint flags);

    [LibraryImport(Library, EntryPoint = "arcanist_audio_analyzer_push")]
    public static partial int AnalyzerPush(nint analyzer, float* samples, nuint count);

    [LibraryImport(Library, EntryPoint = "arcanist_audio_analyzer_finish")]
    public static partial int AnalyzerFinish(nint analyzer, out nint json);

    [LibraryImport(Library, EntryPoint = "arcanist_audio_analyzer_free")]
    public static partial void AnalyzerFree(nint analyzer);

    [LibraryImport(Library, EntryPoint = "arcanist_audio_compare")]
    public static partial double Compare(uint* a, nuint aCount, uint* b, nuint bCount);

    [LibraryImport(Library, EntryPoint = "arcanist_audio_free")]
    public static partial void Free(nint value);

    /// <summary>Copies and releases a string returned by the library.</summary>
    public static string TakeString(nint value)
    {
        try
        {
            return Marshal.PtrToStringUTF8(value) ?? "";
        }
        finally
        {
            Free(value);
        }
    }

    /// <summary>
    /// Whether the library could be loaded. Builds without Rust, or for platforms without a native build, run
    /// without analysis instead of failing.
    /// </summary>
    public static bool IsAvailable => Availability.Value.Available;

    /// <summary>Why the library could not be loaded, for the logs and the admin panel.</summary>
    public static string? LoadError => Availability.Value.Error;

    private static readonly Lazy<(bool Available, string? Error)> Availability = new(() =>
    {
        try
        {
            Version();
            return (true, null);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return (false, e.Message);
        }
    });

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static long ReadCallback(nint context, byte* buffer, int length)
    {
        try
        {
            var stream = (Stream)GCHandle.FromIntPtr(context).Target!;
            return stream.Read(new Span<byte>(buffer, length));
        }
        catch (Exception)
        {
            // Exceptions must not cross into native code; the library turns -1 into an I/O error.
            return -1;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static long SeekCallback(nint context, long offset, int origin)
    {
        try
        {
            var stream = (Stream)GCHandle.FromIntPtr(context).Target!;
            return stream.Seek(offset, (SeekOrigin)origin);
        }
        catch (Exception)
        {
            return -1;
        }
    }
}
