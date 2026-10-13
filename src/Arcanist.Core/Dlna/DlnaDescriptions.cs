using System.Security;

namespace Arcanist.Core.Dlna;

/// <summary>The UPnP descriptions of the media server and its two services.</summary>
internal static class DlnaDescriptions
{
    public static string Device(Guid deviceId, string friendlyName, string version, string? sourceUrl, string basePath) =>
        $"""
        <?xml version="1.0" encoding="utf-8"?>
        <root xmlns="urn:schemas-upnp-org:device-1-0" xmlns:dlna="urn:schemas-dlna-org:device-1-0">
          <specVersion><major>1</major><minor>0</minor></specVersion>
          <device>
            <deviceType>{Ssdp.MediaServer}</deviceType>
            <dlna:X_DLNADOC>DMS-1.50</dlna:X_DLNADOC>
            <friendlyName>{SecurityElement.Escape(friendlyName)}</friendlyName>
            <manufacturer>Arcanist</manufacturer>
            <manufacturerURL>{SecurityElement.Escape(sourceUrl ?? "https://github.com/linkfast/arcanist")}</manufacturerURL>
            <modelDescription>Arcanist music server</modelDescription>
            <modelName>Arcanist</modelName>
            <modelNumber>{SecurityElement.Escape(version)}</modelNumber>
            <modelURL>{SecurityElement.Escape(sourceUrl ?? "https://github.com/linkfast/arcanist")}</modelURL>
            <serialNumber>{deviceId:N}</serialNumber>
            <UDN>uuid:{deviceId}</UDN>
            <presentationURL>/admin/</presentationURL>
            <serviceList>
              <service>
                <serviceType>{Ssdp.ContentDirectory}</serviceType>
                <serviceId>urn:upnp-org:serviceId:ContentDirectory</serviceId>
                <SCPDURL>{basePath}/content-directory.xml</SCPDURL>
                <controlURL>{basePath}/content-directory/control</controlURL>
                <eventSubURL>{basePath}/content-directory/events</eventSubURL>
              </service>
              <service>
                <serviceType>{Ssdp.ConnectionManager}</serviceType>
                <serviceId>urn:upnp-org:serviceId:ConnectionManager</serviceId>
                <SCPDURL>{basePath}/connection-manager.xml</SCPDURL>
                <controlURL>{basePath}/connection-manager/control</controlURL>
                <eventSubURL>{basePath}/connection-manager/events</eventSubURL>
              </service>
            </serviceList>
          </device>
        </root>
        """;

    private static string Argument(string name, string direction, string variable) =>
        $"<argument><name>{name}</name><direction>{direction}</direction><relatedStateVariable>{variable}</relatedStateVariable></argument>";

    private static string Action(string name, params string[] arguments) =>
        $"<action><name>{name}</name><argumentList>{string.Concat(arguments)}</argumentList></action>";

    private static string Variable(string name, string type, bool events = false, string? values = null) =>
        $"<stateVariable sendEvents=\"{(events ? "yes" : "no")}\"><name>{name}</name><dataType>{type}</dataType>{values}</stateVariable>";

    private static string Service(IEnumerable<string> actions, IEnumerable<string> variables) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
        "<scpd xmlns=\"urn:schemas-upnp-org:service-1-0\"><specVersion><major>1</major><minor>0</minor></specVersion>" +
        $"<actionList>{string.Concat(actions)}</actionList><serviceStateTable>{string.Concat(variables)}</serviceStateTable></scpd>";

    public static readonly string ContentDirectory = Service(
        [
            Action("GetSearchCapabilities", Argument("SearchCaps", "out", "SearchCapabilities")),
            Action("GetSortCapabilities", Argument("SortCaps", "out", "SortCapabilities")),
            Action("GetSystemUpdateID", Argument("Id", "out", "SystemUpdateID")),
            Action("Browse",
                Argument("ObjectID", "in", "A_ARG_TYPE_ObjectID"),
                Argument("BrowseFlag", "in", "A_ARG_TYPE_BrowseFlag"),
                Argument("Filter", "in", "A_ARG_TYPE_Filter"),
                Argument("StartingIndex", "in", "A_ARG_TYPE_Index"),
                Argument("RequestedCount", "in", "A_ARG_TYPE_Count"),
                Argument("SortCriteria", "in", "A_ARG_TYPE_SortCriteria"),
                Argument("Result", "out", "A_ARG_TYPE_Result"),
                Argument("NumberReturned", "out", "A_ARG_TYPE_Count"),
                Argument("TotalMatches", "out", "A_ARG_TYPE_Count"),
                Argument("UpdateID", "out", "A_ARG_TYPE_UpdateID")),
        ],
        [
            Variable("SearchCapabilities", "string"),
            Variable("SortCapabilities", "string"),
            Variable("SystemUpdateID", "ui4", events: true),
            Variable("A_ARG_TYPE_ObjectID", "string"),
            Variable("A_ARG_TYPE_BrowseFlag", "string", values:
                "<allowedValueList><allowedValue>BrowseMetadata</allowedValue><allowedValue>BrowseDirectChildren</allowedValue></allowedValueList>"),
            Variable("A_ARG_TYPE_Filter", "string"),
            Variable("A_ARG_TYPE_Index", "ui4"),
            Variable("A_ARG_TYPE_Count", "ui4"),
            Variable("A_ARG_TYPE_SortCriteria", "string"),
            Variable("A_ARG_TYPE_Result", "string"),
            Variable("A_ARG_TYPE_UpdateID", "ui4"),
        ]);

    public static readonly string ConnectionManager = Service(
        [
            Action("GetProtocolInfo", Argument("Source", "out", "SourceProtocolInfo"), Argument("Sink", "out", "SinkProtocolInfo")),
            Action("GetCurrentConnectionIDs", Argument("ConnectionIDs", "out", "CurrentConnectionIDs")),
            Action("GetCurrentConnectionInfo",
                Argument("ConnectionID", "in", "A_ARG_TYPE_ConnectionID"),
                Argument("RcsID", "out", "A_ARG_TYPE_RcsID"),
                Argument("AVTransportID", "out", "A_ARG_TYPE_AVTransportID"),
                Argument("ProtocolInfo", "out", "A_ARG_TYPE_ProtocolInfo"),
                Argument("PeerConnectionManager", "out", "A_ARG_TYPE_ConnectionManager"),
                Argument("PeerConnectionID", "out", "A_ARG_TYPE_ConnectionID"),
                Argument("Direction", "out", "A_ARG_TYPE_Direction"),
                Argument("Status", "out", "A_ARG_TYPE_ConnectionStatus")),
        ],
        [
            Variable("SourceProtocolInfo", "string", events: true),
            Variable("SinkProtocolInfo", "string", events: true),
            Variable("CurrentConnectionIDs", "string", events: true),
            Variable("A_ARG_TYPE_ConnectionStatus", "string"),
            Variable("A_ARG_TYPE_ConnectionManager", "string"),
            Variable("A_ARG_TYPE_Direction", "string"),
            Variable("A_ARG_TYPE_ProtocolInfo", "string"),
            Variable("A_ARG_TYPE_ConnectionID", "i4"),
            Variable("A_ARG_TYPE_AVTransportID", "i4"),
            Variable("A_ARG_TYPE_RcsID", "i4"),
        ]);
}
