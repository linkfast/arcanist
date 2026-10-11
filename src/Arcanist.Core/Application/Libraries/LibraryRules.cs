using Arcanist.Core.Data.Entities;
using Arcanist.Core.Storage;

namespace Arcanist.Core.Application.Libraries;

/// <summary>Validation shared by the setup wizard and library management.</summary>
public sealed class LibraryRules(ILibraryStorageResolver storages)
{
    public const int NameMaxLength = 128;
    public const int MaxScanIntervalHours = 24 * 30;

    public static void ValidateName(string? name, string field, ValidationErrors errors)
    {
        if (string.IsNullOrWhiteSpace(name))
            errors.Add(field, ErrorCodes.Required);
        else if (name.Trim().Length > NameMaxLength)
            errors.Add(field, ErrorCodes.InvalidFormat);
    }

    public static void ValidateScanInterval(int? hours, string field, ValidationErrors errors)
    {
        if (hours is < 1 or > MaxScanIntervalHours)
            errors.Add(field, ErrorCodes.InvalidFormat);
    }

    /// <summary>
    /// Checks new locations for a library: each must be usable, and none may repeat or nest inside another of the
    /// same library (its files would be scanned twice). Returns the normalized locations, aligned with the input;
    /// invalid entries are null.
    /// </summary>
    /// <param name="existing">Locations the library already has.</param>
    public async Task<IReadOnlyList<string?>> ValidateLocationsAsync(
        IReadOnlyList<string?> locations,
        IReadOnlyList<string> existing,
        Func<int, string> field,
        ValidationErrors errors,
        CancellationToken cancellationToken)
    {
        var storage = storages.For(LibrarySourceKind.Local);
        var accepted = new List<string>(existing);
        var result = new string?[locations.Count];

        for (var i = 0; i < locations.Count; i++)
        {
            var location = locations[i]?.Trim();
            if (string.IsNullOrEmpty(location))
            {
                errors.Add(field(i), ErrorCodes.Required);
                continue;
            }

            // Probing first: it rejects malformed locations before they are normalized.
            var probe = await storage.ProbeAsync(new StorageLocation(LibrarySourceKind.Local, location), cancellationToken);
            if (!probe.IsAvailable)
            {
                errors.Add(field(i), probe.Problem switch
                {
                    StorageProblem.Invalid => ErrorCodes.InvalidFormat,
                    StorageProblem.AccessDenied => ErrorCodes.AccessDenied,
                    _ => ErrorCodes.NotFound,
                });
                continue;
            }

            var normalized = storage.NormalizeLocation(location);
            if (accepted.Any(other => storage.Contains(other, normalized) || storage.Contains(normalized, other)))
            {
                errors.Add(field(i), ErrorCodes.Overlapping);
                continue;
            }

            accepted.Add(normalized);
            result[i] = normalized;
        }

        return result;
    }
}
