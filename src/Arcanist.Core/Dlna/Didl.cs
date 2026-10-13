using System.Globalization;
using System.Text;
using System.Xml;

namespace Arcanist.Core.Dlna;

internal abstract record DidlObject(string Id, string ParentId, string Title);

/// <param name="Class">The UPnP class, e.g. <c>object.container.album.musicAlbum</c>.</param>
internal sealed record DidlContainer(string Id, string ParentId, string Title, string Class, int ChildCount, string? Artist = null, string? ArtUrl = null)
    : DidlObject(Id, ParentId, Title);

/// <param name="BitRate">kbit/s.</param>
internal sealed record DidlResource(string Url, string ProtocolInfo, int DurationMs, long? Size, int? BitRate, int? SampleRate, int? Channels, int? BitDepth);

internal sealed record DidlItem(
    string Id,
    string ParentId,
    string Title,
    string? Artist,
    string? Album,
    string? AlbumArtist,
    string? Genre,
    int? TrackNumber,
    int? Year,
    string? ArtUrl,
    IReadOnlyList<DidlResource> Resources) : DidlObject(Id, ParentId, Title);

/// <summary>Writes DIDL-Lite, the XML that describes containers and items to UPnP devices.</summary>
internal static class Didl
{
    private const string DidlNs = "urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/";
    private const string Dc = "http://purl.org/dc/elements/1.1/";
    private const string Upnp = "urn:schemas-upnp-org:metadata-1-0/upnp/";
    private const string DlnaNs = "urn:schemas-dlna-org:metadata-1-0/";

    public static string Write(IEnumerable<DidlObject> objects)
    {
        var builder = new StringBuilder();
        using (var writer = XmlWriter.Create(builder, new XmlWriterSettings { OmitXmlDeclaration = true, CheckCharacters = false }))
        {
            writer.WriteStartElement("DIDL-Lite", DidlNs);
            writer.WriteAttributeString("xmlns", "dc", null, Dc);
            writer.WriteAttributeString("xmlns", "upnp", null, Upnp);
            writer.WriteAttributeString("xmlns", "dlna", null, DlnaNs);
            foreach (var item in objects)
            {
                switch (item)
                {
                    case DidlContainer container:
                        WriteContainer(writer, container);
                        break;
                    case DidlItem track:
                        WriteItem(writer, track);
                        break;
                }
            }

            writer.WriteEndElement();
        }

        return builder.ToString();
    }

    private static void WriteContainer(XmlWriter writer, DidlContainer container)
    {
        writer.WriteStartElement("container", DidlNs);
        writer.WriteAttributeString("id", container.Id);
        writer.WriteAttributeString("parentID", container.ParentId);
        writer.WriteAttributeString("restricted", "1");
        writer.WriteAttributeString("searchable", "0");
        writer.WriteAttributeString("childCount", container.ChildCount.ToString(CultureInfo.InvariantCulture));
        writer.WriteElementString("title", Dc, Clean(container.Title));
        writer.WriteElementString("class", Upnp, container.Class);
        if (container.Artist is { } artist)
            writer.WriteElementString("artist", Upnp, Clean(artist));
        WriteArt(writer, container.ArtUrl);
        writer.WriteEndElement();
    }

    private static void WriteItem(XmlWriter writer, DidlItem item)
    {
        writer.WriteStartElement("item", DidlNs);
        writer.WriteAttributeString("id", item.Id);
        writer.WriteAttributeString("parentID", item.ParentId);
        writer.WriteAttributeString("restricted", "1");
        writer.WriteElementString("title", Dc, Clean(item.Title));
        writer.WriteElementString("class", Upnp, "object.item.audioItem.musicTrack");
        if (item.Artist is { } artist)
        {
            writer.WriteElementString("creator", Dc, Clean(artist));
            writer.WriteElementString("artist", Upnp, Clean(artist));
        }

        if (item.AlbumArtist is { } albumArtist)
        {
            writer.WriteStartElement("artist", Upnp);
            writer.WriteAttributeString("role", "AlbumArtist");
            writer.WriteString(Clean(albumArtist));
            writer.WriteEndElement();
        }

        if (item.Album is { } album)
            writer.WriteElementString("album", Upnp, Clean(album));
        if (item.Genre is { } genre)
            writer.WriteElementString("genre", Upnp, Clean(genre));
        if (item.TrackNumber is { } number)
            writer.WriteElementString("originalTrackNumber", Upnp, number.ToString(CultureInfo.InvariantCulture));
        if (item.Year is { } year)
            writer.WriteElementString("date", Dc, $"{year:0000}-01-01");
        WriteArt(writer, item.ArtUrl);

        foreach (var resource in item.Resources)
        {
            writer.WriteStartElement("res", DidlNs);
            writer.WriteAttributeString("protocolInfo", resource.ProtocolInfo);
            if (resource.DurationMs > 0)
                writer.WriteAttributeString("duration", Duration(resource.DurationMs));
            if (resource.Size is { } size)
                writer.WriteAttributeString("size", size.ToString(CultureInfo.InvariantCulture));
            // DIDL bit rates are in bytes per second.
            if (resource.BitRate is { } bitRate)
                writer.WriteAttributeString("bitrate", (bitRate * 125).ToString(CultureInfo.InvariantCulture));
            if (resource.SampleRate is { } rate)
                writer.WriteAttributeString("sampleFrequency", rate.ToString(CultureInfo.InvariantCulture));
            if (resource.Channels is { } channels)
                writer.WriteAttributeString("nrAudioChannels", channels.ToString(CultureInfo.InvariantCulture));
            if (resource.BitDepth is { } depth)
                writer.WriteAttributeString("bitsPerSample", depth.ToString(CultureInfo.InvariantCulture));
            writer.WriteString(resource.Url);
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static void WriteArt(XmlWriter writer, string? url)
    {
        if (url is null)
            return;
        writer.WriteStartElement("albumArtURI", Upnp);
        writer.WriteAttributeString("dlna", "profileID", DlnaNs, "JPEG_SM");
        writer.WriteString(url);
        writer.WriteEndElement();
    }

    /// <summary>DIDL durations: <c>H:MM:SS.fff</c>.</summary>
    public static string Duration(int milliseconds)
    {
        var time = TimeSpan.FromMilliseconds(milliseconds);
        return string.Create(CultureInfo.InvariantCulture, $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds:000}");
    }

    /// <summary>Drops characters XML cannot carry (control characters in tags).</summary>
    private static string Clean(string text) =>
        text.All(XmlConvert.IsXmlChar) ? text : string.Concat(text.Where(XmlConvert.IsXmlChar));
}
