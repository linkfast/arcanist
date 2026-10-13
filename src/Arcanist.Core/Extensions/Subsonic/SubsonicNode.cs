using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml;

namespace Arcanist.Core.Extensions.Subsonic;

/// <summary>
/// An element of a Subsonic response, written as XML or as JSON following Subsonic's mapping: attributes become
/// properties, repeated elements become arrays named after the element (always arrays, even empty), single elements
/// become objects and text becomes <c>value</c>.
/// </summary>
internal sealed class SubsonicNode(string name)
{
    private readonly List<(string Name, object Value)> _attributes = [];
    private readonly List<(string Name, object Content)> _children = [];
    private string? _text;

    public string Name { get; } = name;

    /// <summary>Sets an attribute; null values are left out.</summary>
    public SubsonicNode Attr(string name, object? value)
    {
        if (value is not null)
            _attributes.Add((name, value));
        return this;
    }

    public SubsonicNode Text(string? text)
    {
        _text = text;
        return this;
    }

    /// <summary>A single child element (a JSON object).</summary>
    public SubsonicNode Child(SubsonicNode? child)
    {
        if (child is { IsFragment: true })
            return Adopt(child);
        if (child is not null)
            _children.Add((child.Name, child));
        return this;
    }

    /// <summary>Repeated child elements (a JSON array, present even when empty).</summary>
    public SubsonicNode List(string name, IEnumerable<SubsonicNode> items)
    {
        _children.Add((name, items.ToList()));
        return this;
    }

    /// <summary>Repeated elements with a simple value each, e.g. <c>&lt;folder&gt;1&lt;/folder&gt;</c> (a JSON array of values).</summary>
    public SubsonicNode Values(string name, IEnumerable<object> values)
    {
        _children.Add((name, new ValueList(values.ToList())));
        return this;
    }

    private sealed record ValueList(List<object> Values);

    /// <summary>A nameless node whose content goes straight into the response, for payloads that are lists.</summary>
    public static SubsonicNode Fragment() => new("");

    public bool IsFragment => Name.Length == 0;

    /// <summary>Takes the attributes and children of a fragment.</summary>
    public SubsonicNode Adopt(SubsonicNode fragment)
    {
        _attributes.AddRange(fragment._attributes);
        _children.AddRange(fragment._children);
        return this;
    }

    public void WriteXml(XmlWriter writer, string? ns = null)
    {
        writer.WriteStartElement(Name, ns);
        foreach (var (name, value) in _attributes)
            writer.WriteAttributeString(name, XmlText(value));
        if (_text is not null)
            writer.WriteString(Clean(_text));

        foreach (var (name, content) in _children)
        {
            switch (content)
            {
                case SubsonicNode node:
                    node.WriteXml(writer, ns);
                    break;
                case List<SubsonicNode> nodes:
                    foreach (var node in nodes)
                        node.WriteXml(writer, ns);
                    break;
                case ValueList list:
                    foreach (var value in list.Values)
                        writer.WriteElementString(name, ns, XmlText(value));
                    break;
            }
        }

        writer.WriteEndElement();
    }

    public void WriteJson(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        WriteJsonMembers(writer);
        writer.WriteEndObject();
    }

    internal void WriteJsonMembers(Utf8JsonWriter writer)
    {
        foreach (var (name, value) in _attributes)
        {
            writer.WritePropertyName(name);
            WriteJsonValue(writer, value);
        }

        if (_text is not null)
            writer.WriteString("value", _text);

        foreach (var (name, content) in _children)
        {
            writer.WritePropertyName(name);
            switch (content)
            {
                case SubsonicNode node:
                    node.WriteJson(writer);
                    break;
                case List<SubsonicNode> nodes:
                    writer.WriteStartArray();
                    foreach (var node in nodes)
                        node.WriteJson(writer);
                    writer.WriteEndArray();
                    break;
                case ValueList list:
                    writer.WriteStartArray();
                    foreach (var value in list.Values)
                        WriteJsonValue(writer, value);
                    writer.WriteEndArray();
                    break;
            }
        }
    }

    private static void WriteJsonValue(Utf8JsonWriter writer, object value)
    {
        switch (value)
        {
            case bool b:
                writer.WriteBooleanValue(b);
                break;
            case int i:
                writer.WriteNumberValue(i);
                break;
            case long l:
                writer.WriteNumberValue(l);
                break;
            case double d:
                writer.WriteNumberValue(Math.Round(d, 4));
                break;
            case float f:
                writer.WriteNumberValue(Math.Round(f, 4));
                break;
            case DateTime t:
                writer.WriteStringValue(Date(t));
                break;
            case Guid g:
                writer.WriteStringValue(g.ToString());
                break;
            default:
                writer.WriteStringValue(value.ToString());
                break;
        }
    }

    private static string XmlText(object value) => value switch
    {
        bool b => b ? "true" : "false",
        DateTime t => Date(t),
        double d => Math.Round(d, 4).ToString(CultureInfo.InvariantCulture),
        float f => Math.Round(f, 4).ToString(CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => Clean(value.ToString() ?? ""),
    };

    /// <summary>ISO 8601 in UTC with milliseconds; more precision trips some clients' parsers.</summary>
    public static string Date(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    /// <summary>Removes characters XML 1.0 cannot carry (control characters in tags), which would break the whole document.</summary>
    private static string Clean(string text)
    {
        if (text.All(XmlConvert.IsXmlChar))
            return text;
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Where(XmlConvert.IsXmlChar))
            builder.Append(c);
        return builder.ToString();
    }
}
