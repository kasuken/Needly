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
        if (filter.SchemaVersion != ActionFilter.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Action filter schema version {filter.SchemaVersion} is not supported.");
        }

        var types = Required(filter.Types, nameof(filter.Types));
        var states = Required(filter.States, nameof(filter.States));
        if (types.Any(type => !Enum.IsDefined(type)) || states.Any(state => !Enum.IsDefined(state)) ||
            !Enum.IsDefined(filter.AssigneeScope) || !Enum.IsDefined(filter.BotInvolvement))
        {
            throw new InvalidDataException("The action filter contains an unsupported option.");
        }

        if (filter.WaitingAtLeast <= TimeSpan.Zero)
        {
            throw new InvalidDataException("The waiting threshold must be positive.");
        }

        return filter with
        {
            Types = types.Distinct().Order().ToArray(),
            States = states.Distinct().Order().ToArray(),
            Repositories = NormalizeNames(filter.Repositories, nameof(filter.Repositories)),
            Organizations = NormalizeNames(filter.Organizations, nameof(filter.Organizations)),
            Authors = NormalizeNames(filter.Authors, nameof(filter.Authors))
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