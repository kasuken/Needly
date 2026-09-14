using System.Text.Json;
using System.Text.Json.Serialization;
using Needly.Domain;

namespace Needly.Infrastructure.Actions;

internal static class ActionFilterJsonSerializer
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    internal static string Serialize(ActionFilter filter) =>
        JsonSerializer.Serialize(Normalize(filter), Options);

    internal static ActionFilter Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException("The action filter is empty.");
        }

        try
        {
            return Normalize(JsonSerializer.Deserialize<ActionFilter>(json, Options)
                ?? throw new InvalidDataException("The action filter is empty."));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The action filter JSON is malformed.", exception);
        }
    }

    private static ActionFilter Normalize(ActionFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (filter.SchemaVersion is not (1 or 2 or ActionFilter.CurrentSchemaVersion))
        {
            throw new InvalidDataException(
                $"Action filter schema version {filter.SchemaVersion} is not supported.");
        }

        // Version 1 filters predate the schema version 2 (issue #32) criteria below. Their JSON never
        // contains those properties, so the deserializer already defaults them to "no constraint"
        // (empty arrays, Any enum members). Normalizing simply upgrades the stamped version.
        var isLegacyVersion1 = filter.SchemaVersion == 1;

        var types = Required(filter.Types, nameof(filter.Types));
        var states = Required(filter.States, nameof(filter.States));
        if (types.Any(type => !Enum.IsDefined(type)) || states.Any(state => !Enum.IsDefined(state)) ||
            !Enum.IsDefined(filter.AssigneeScope) || !Enum.IsDefined(filter.BotInvolvement) ||
            !Enum.IsDefined(filter.IsDraft) || !Enum.IsDefined(filter.RequestedViaCodeowners))
        {
            throw new InvalidDataException("The action filter contains an unsupported option.");
        }

        var sizeBuckets = Required(filter.SizeBuckets, nameof(filter.SizeBuckets));
        if (sizeBuckets.Any(bucket => !Enum.IsDefined(bucket)))
        {
            throw new InvalidDataException("The action filter contains an unsupported option.");
        }

        if (filter.WaitingAtLeast <= TimeSpan.Zero)
        {
            throw new InvalidDataException("The waiting threshold must be positive.");
        }

        // Schema version 3 additions (issue #34). Versions 1 and 2 predate the RiskLevels criterion;
        // their JSON never contains it, so the deserializer already defaults it to "no constraint"
        // (an empty array). Issue #35, running concurrently, also bumps to schema version 3 and
        // upgrades its own new field here in the same way.
        var isPreVersion3 = filter.SchemaVersion is 1 or 2;
        var riskLevels = Required(filter.RiskLevels, nameof(filter.RiskLevels));
        if (riskLevels.Any(level => !Enum.IsDefined(level)))
        {
            throw new InvalidDataException("The action filter contains an unsupported option.");
        }

        return filter with
        {
            SchemaVersion = ActionFilter.CurrentSchemaVersion,
            Types = types.Distinct().Order().ToArray(),
            States = states.Distinct().Order().ToArray(),
            Repositories = NormalizeNames(filter.Repositories, nameof(filter.Repositories)),
            Organizations = NormalizeNames(filter.Organizations, nameof(filter.Organizations)),
            Authors = NormalizeNames(filter.Authors, nameof(filter.Authors)),
            Labels = isLegacyVersion1 ? [] : NormalizeNames(filter.Labels, nameof(filter.Labels)),
            SizeBuckets = isLegacyVersion1 ? [] : sizeBuckets.Distinct().Order().ToArray(),
            Milestones = isLegacyVersion1 ? [] : NormalizeNames(filter.Milestones, nameof(filter.Milestones)),
            RiskLevels = isPreVersion3 ? [] : riskLevels.Distinct().Order().ToArray()
        };
    }

    private static T[] Required<T>(T[]? values, string propertyName) =>
        values ?? throw new InvalidDataException($"Action filter property '{propertyName}' cannot be null.");

    private static string[] NormalizeNames(string[]? values, string propertyName)
    {
        var required = Required(values, propertyName);
        if (required.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidDataException($"Action filter property '{propertyName}' contains an empty value.");
        }

        return required
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = false
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}