using System.Text.Json;

namespace Arcanist.Extensibility;

/// <summary>
/// An extension's settings page in the admin panel, described as data: the panel draws it with its own components
/// and look (no code of the extension runs in the panel). Register it keyed by the extension's id:
/// <c>services.AddKeyedScoped&lt;IExtensionAdminPage, MyAdminPage&gt;(context.Id)</c>, and declare
/// <c>adminPage</c> in the manifest. Every call comes from an administrator; saving and actions also need a recent
/// authentication. Texts are the extension's, in <see cref="AdminPageContext.Language"/>.
/// </summary>
public interface IExtensionAdminPage
{
    /// <summary>The page as it is now: its sections, the fields with their values, statuses and actions.</summary>
    Task<AdminPage> GetAsync(AdminPageContext context, CancellationToken cancellationToken);

    /// <summary>Saves the page's fields (by their keys); only those the administrator can edit come.</summary>
    Task<AdminPageResult> SaveAsync(AdminPageContext context, IReadOnlyDictionary<string, JsonElement> values, CancellationToken cancellationToken) =>
        Task.FromResult(AdminPageResult.Failed("not_supported"));

    /// <summary>Runs one of the page's actions, with the fields' values as they are in the panel when it asks for them.</summary>
    Task<AdminPageResult> RunAsync(AdminPageContext context, string action, IReadOnlyDictionary<string, JsonElement> values,
        CancellationToken cancellationToken) =>
        Task.FromResult(AdminPageResult.Failed("not_supported"));
}

/// <summary>Who asks for the page and in what language.</summary>
/// <param name="Language">The administrator's language (<c>es</c>, <c>en</c>…), to write the page in.</param>
/// <param name="UserId">The administrator.</param>
/// <param name="Services">The request's services (the server's application services among them).</param>
public sealed record AdminPageContext(string Language, Guid UserId, IServiceProvider Services);

/// <summary>An extension's settings page.</summary>
/// <param name="Sections">Its sections, in order.</param>
/// <param name="RefreshSeconds">Read the page again this often while it is open (to follow a progress); null never.</param>
public sealed record AdminPage(IReadOnlyList<AdminSection> Sections, int? RefreshSeconds = null)
{
    /// <summary>What the extension still needs before it works (tools to download, an account to set…), or null when nothing: the panel offers it right after the extension starts, and at the top of the page, with the administrator's consent.</summary>
    public AdminSetup? Setup { get; init; }
}

/// <summary>Something an extension needs before it works, run with the administrator's consent.</summary>
/// <param name="Title">What it is («Install the tools»).</param>
/// <param name="Description">Why it is needed and what it does (downloads, from where, how big).</param>
/// <param name="Items">What will be installed or done, one line each.</param>
/// <param name="Action">The page's action that does it (<see cref="IExtensionAdminPage.RunAsync"/>).</param>
/// <param name="ActionLabel">Its button.</param>
public sealed record AdminSetup(string Title, string Description, IReadOnlyList<string> Items, string Action, string ActionLabel);

/// <summary>A card of the page.</summary>
/// <param name="Id">Stable among the page's sections.</param>
/// <param name="Title">Its heading.</param>
/// <param name="Description">A line under the heading.</param>
/// <param name="Blocks">What it shows, in order.</param>
/// <param name="Actions">Buttons at the end of the section.</param>
public sealed record AdminSection(
    string Id,
    string Title,
    string? Description = null,
    IReadOnlyList<AdminBlock>? Blocks = null,
    IReadOnlyList<AdminAction>? Actions = null);

/// <summary>What a block is.</summary>
public enum AdminBlockKind
{
    /// <summary>A setting the administrator edits (<see cref="AdminBlock.Type"/>).</summary>
    Field,
    /// <summary>A label and a value, with a tone (installed, failing…).</summary>
    Status,
    /// <summary>A paragraph, with a tone (a warning, a legal note…).</summary>
    Notice,
    /// <summary>A label and how far something went (<see cref="AdminBlock.Percent"/>).</summary>
    Progress,
    /// <summary>A label and a list of short texts.</summary>
    List,
}

/// <summary>How a field is edited, and the JSON its value is.</summary>
public enum AdminFieldType
{
    /// <summary>One line of text (a string).</summary>
    Text,
    /// <summary>Never shown back: the value comes empty, and saving it empty keeps what there is.</summary>
    Secret,
    /// <summary>A number, within <see cref="AdminBlock.Min"/> and <see cref="AdminBlock.Max"/>.</summary>
    Number,
    /// <summary>On or off (a boolean).</summary>
    Switch,
    /// <summary>One of <see cref="AdminBlock.Options"/> (its value, a string).</summary>
    Select,
    /// <summary>Some of <see cref="AdminBlock.Options"/> (an array of their values).</summary>
    MultiSelect,
    /// <summary>Several lines of text (a string).</summary>
    TextArea,
    /// <summary>An address (a string).</summary>
    Url,
    /// <summary>One of the server's libraries (the panel offers them), by id.</summary>
    Library,
}

/// <summary>The color a status or notice is shown in.</summary>
public enum AdminTone
{
    /// <summary>Plain.</summary>
    Neutral,
    /// <summary>Working, done.</summary>
    Good,
    /// <summary>Needs attention.</summary>
    Warning,
    /// <summary>Failing, missing.</summary>
    Bad,
}

/// <summary>How an action's button looks.</summary>
public enum AdminActionStyle
{
    /// <summary>The main thing to do.</summary>
    Primary,
    /// <summary>Anything else.</summary>
    Secondary,
    /// <summary>Removes or undoes something.</summary>
    Destructive,
}

/// <summary>A piece of a section, built with its Field, Status, Notice, Progress and List methods.</summary>
/// <param name="Kind">What it is.</param>
/// <param name="Key">A field's key, the one its value is saved under.</param>
/// <param name="Label">The label of a field, status, progress or list.</param>
/// <param name="Help">A line under it.</param>
/// <param name="Type">How a field is edited.</param>
/// <param name="Value">A field's current value, as JSON.</param>
/// <param name="Options">The choices of a select.</param>
/// <param name="Placeholder">What an empty text field shows.</param>
/// <param name="Min">The smallest number a field takes.</param>
/// <param name="Max">The largest number a field takes.</param>
/// <param name="ReadOnly">A field shown but not edited.</param>
/// <param name="Text">The value of a status, the paragraph of a notice, or a progress's line.</param>
/// <param name="Tone">The color of a status or notice.</param>
/// <param name="Percent">How far a progress went, from 0 to 100.</param>
/// <param name="Items">The texts of a list.</param>
public sealed record AdminBlock(
    AdminBlockKind Kind,
    string? Key = null,
    string? Label = null,
    string? Help = null,
    AdminFieldType? Type = null,
    JsonElement? Value = null,
    IReadOnlyList<AdminOption>? Options = null,
    string? Placeholder = null,
    double? Min = null,
    double? Max = null,
    bool ReadOnly = false,
    string? Text = null,
    AdminTone Tone = AdminTone.Neutral,
    double? Percent = null,
    IReadOnlyList<string>? Items = null)
{
    /// <summary>A setting; <paramref name="value"/> is serialized to JSON (camelCase).</summary>
    public static AdminBlock Field(string key, string label, AdminFieldType type, object? value, string? help = null,
        IReadOnlyList<AdminOption>? options = null, string? placeholder = null, double? min = null, double? max = null, bool readOnly = false) =>
        new(AdminBlockKind.Field, key, label, help, type, value is null ? null : JsonSerializer.SerializeToElement(value, JsonSerializerOptions.Web),
            options, placeholder, min, max, readOnly);

    /// <summary>A label and its value.</summary>
    public static AdminBlock Status(string label, string text, AdminTone tone = AdminTone.Neutral, string? help = null) =>
        new(AdminBlockKind.Status, Label: label, Text: text, Tone: tone, Help: help);

    /// <summary>A paragraph.</summary>
    public static AdminBlock Notice(string text, AdminTone tone = AdminTone.Neutral) => new(AdminBlockKind.Notice, Text: text, Tone: tone);

    /// <summary>A progress bar (<paramref name="percent"/> from 0 to 100).</summary>
    public static AdminBlock Progress(string label, double percent, string? text = null) =>
        new(AdminBlockKind.Progress, Label: label, Percent: Math.Clamp(percent, 0, 100), Text: text);

    /// <summary>A label and some short texts.</summary>
    public static AdminBlock List(string label, IReadOnlyList<string> items) => new(AdminBlockKind.List, Label: label, Items: items);
}

/// <summary>A choice of a select.</summary>
/// <param name="Value">What is saved.</param>
/// <param name="Label">What is shown.</param>
public sealed record AdminOption(string Value, string Label);

/// <summary>A button of a section.</summary>
/// <param name="Id">What <see cref="IExtensionAdminPage.RunAsync"/> receives.</param>
/// <param name="Label">Its text.</param>
/// <param name="Style">How it looks.</param>
/// <param name="Confirm">A question the panel asks before running it; null runs it at once.</param>
public sealed record AdminAction(string Id, string Label, AdminActionStyle Style = AdminActionStyle.Secondary, string? Confirm = null);

/// <summary>How saving or an action went.</summary>
/// <param name="Ok">It went well.</param>
/// <param name="Message">What happened, for a notice in the panel.</param>
/// <param name="Errors">Problems by field key, in the extension's words.</param>
public sealed record AdminPageResult(bool Ok, string? Message = null, IReadOnlyDictionary<string, string>? Errors = null)
{
    /// <summary>It went well.</summary>
    public static AdminPageResult Done(string? message = null) => new(true, message);

    /// <summary>It did not: <paramref name="message"/> says why, and <paramref name="errors"/> which fields.</summary>
    public static AdminPageResult Failed(string message, IReadOnlyDictionary<string, string>? errors = null) => new(false, message, errors);
}
