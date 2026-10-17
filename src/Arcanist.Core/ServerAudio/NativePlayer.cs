using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Arcanist.Core.ServerAudio;

/// <summary>
/// Bindings to <c>native/arcanist-player</c> (Rust): <c>arcanist_player.dll</c>, <c>libarcanist_player.so</c> or
/// <c>libarcanist_player.dylib</c> next to the server. Strings come back as JSON allocated by the library and released
/// with <see cref="Free"/>. On Linux it needs ALSA (<c>libasound.so.2</c>): without it the library does not load and
/// server audio is unavailable, nothing else.
/// </summary>
internal static unsafe partial class NativePlayer
{
    private const string Library = "arcanist_player";

    public const int StatusOk = 0;
    public const int StatusDevice = 1;
    public const int StatusRetry = 2;

    /// <summary>Reads the media through managed callbacks, on the player's thread; see <c>PlayerSource</c> in <c>lib.rs</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Source
    {
        public nint Context;
        public delegate* unmanaged[Cdecl]<nint, byte*, int, long> Read;
        public delegate* unmanaged[Cdecl]<nint, long, int, long> Seek;
        public long Length;
        public int Seekable;
        public delegate* unmanaged[Cdecl]<nint, void> Release;
    }

    [LibraryImport(Library, EntryPoint = "arcanist_player_version")]
    public static partial uint Version();

    [LibraryImport(Library, EntryPoint = "arcanist_player_devices")]
    public static partial int Devices(out nint json);

    [LibraryImport(Library, EntryPoint = "arcanist_player_open", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int Open(string? device, uint bufferMs, out nint handle, out nint json);

    [LibraryImport(Library, EntryPoint = "arcanist_player_close")]
    public static partial void Close(nint handle);

    [LibraryImport(Library, EntryPoint = "arcanist_player_play", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int Play(nint handle, Source* source, string? extension, ulong token, ulong startMs, ulong endMs, ulong positionMs);

    [LibraryImport(Library, EntryPoint = "arcanist_player_queue", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int Queue(nint handle, Source* source, string? extension, ulong token, ulong startMs, ulong endMs);

    [LibraryImport(Library, EntryPoint = "arcanist_player_clear_queue")]
    public static partial int ClearQueue(nint handle);

    [LibraryImport(Library, EntryPoint = "arcanist_player_seek")]
    public static partial int Seek(nint handle, ulong token, ulong positionMs);

    [LibraryImport(Library, EntryPoint = "arcanist_player_pause")]
    public static partial int Pause(nint handle, int paused);

    [LibraryImport(Library, EntryPoint = "arcanist_player_volume")]
    public static partial int Volume(nint handle, float volume, int muted);

    [LibraryImport(Library, EntryPoint = "arcanist_player_stop")]
    public static partial int Stop(nint handle);

    [LibraryImport(Library, EntryPoint = "arcanist_player_status")]
    public static partial int Status(nint handle, out nint json);

    [LibraryImport(Library, EntryPoint = "arcanist_player_free")]
    public static partial void Free(nint value);

    /// <summary>Copies and releases a string returned by the library.</summary>
    public static string TakeString(nint value)
    {
        if (value == 0)
            return "";
        try
        {
            return Marshal.PtrToStringUTF8(value) ?? "";
        }
        finally
        {
            Free(value);
        }
    }

    /// <summary>Whether the library could be loaded (it can be missing, or ALSA can be missing on Linux).</summary>
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

    /// <summary>A source for the player: the stream behind a GC handle the library releases when done with it.</summary>
    public static Source SourceFor(Stream stream)
    {
        long length;
        try
        {
            length = stream.CanSeek ? stream.Length : -1;
        }
        catch (NotSupportedException)
        {
            length = -1;
        }

        return new Source
        {
            Context = GCHandle.ToIntPtr(GCHandle.Alloc(stream)),
            Read = &ReadCallback,
            Seek = &SeekCallback,
            Length = length,
            Seekable = stream.CanSeek ? 1 : 0,
            Release = &ReleaseCallback,
        };
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static long ReadCallback(nint context, byte* buffer, int length)
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
    private static long SeekCallback(nint context, long offset, int origin)
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

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void ReleaseCallback(nint context) => Release(context);

    /// <summary>Frees a source's handle and closes its stream (the library calls it; so does a call it refused).</summary>
    public static void Release(nint context)
    {
        try
        {
            var handle = GCHandle.FromIntPtr(context);
            var stream = (Stream?)handle.Target;
            handle.Free();
            stream?.Dispose();
        }
        catch (Exception)
        {
            // Nothing can be done about it from here.
        }
    }
}
