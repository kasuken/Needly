namespace Needly.Domain;

/// <summary>
/// Provides the stable identity used to coalesce GitHub events into one active action.
/// </summary>
public readonly record struct ActionKey
{
    private const int MaximumLength = 128;

    private ActionKey(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Gets the canonical key value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Creates a key from the immutable identity of an action.
    /// </summary>
    /// <param name="type">The work type.</param>
    /// <param name="repositoryId">The subject repository identifier.</param>
    /// <param name="subjectType">The GitHub subject type.</param>
    /// <param name="subjectNumber">The repository-scoped issue or pull request number.</param>
    /// <param name="assigneeType">The assignee type.</param>
    /// <param name="assigneeId">The user or team identifier.</param>
    /// <returns>A deterministic key for the supplied identity.</returns>
    public static ActionKey Create(
        ActionType type,
        Guid repositoryId,
        GitHubSubjectType subjectType,
        int subjectNumber,
        ActionAssigneeType assigneeType,
        Guid assigneeId)
    {
        DomainGuard.Required(repositoryId, nameof(repositoryId));
        DomainGuard.Positive(subjectNumber, nameof(subjectNumber));
        DomainGuard.Required(assigneeId, nameof(assigneeId));

        return new ActionKey(
            $"{(int)type}:{repositoryId:N}:{(int)subjectType}:{subjectNumber}:{(int)assigneeType}:{assigneeId:N}");
    }

    /// <summary>
    /// Parses a previously persisted canonical action key.
    /// </summary>
    /// <param name="value">The persisted key value.</param>
    /// <returns>The parsed action key.</returns>
    public static ActionKey Parse(string value)
    {
        var normalizedValue = DomainGuard.Required(value, MaximumLength, nameof(value));
        var segments = normalizedValue.Split(':');
        if (segments.Length != 6 ||
            !int.TryParse(segments[0], out var type) || !Enum.IsDefined((ActionType)type) ||
            !Guid.TryParseExact(segments[1], "N", out var repositoryId) || repositoryId == Guid.Empty ||
            !int.TryParse(segments[2], out var subjectType) || !Enum.IsDefined((GitHubSubjectType)subjectType) ||
            !int.TryParse(segments[3], out var subjectNumber) || subjectNumber <= 0 ||
            !int.TryParse(segments[4], out var assigneeType) || !Enum.IsDefined((ActionAssigneeType)assigneeType) ||
            !Guid.TryParseExact(segments[5], "N", out var assigneeId) || assigneeId == Guid.Empty)
        {
            throw new FormatException("The action key is not in canonical form.");
        }

        return Create(
            (ActionType)type,
            repositoryId,
            (GitHubSubjectType)subjectType,
            subjectNumber,
            (ActionAssigneeType)assigneeType,
            assigneeId);
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}