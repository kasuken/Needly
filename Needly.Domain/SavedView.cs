namespace Needly.Domain;

/// <summary>Represents an ordered, named action filter owned by one Needly user.</summary>
public sealed class SavedView
{
    private SavedView()
    {
    }

    /// <summary>Gets the saved view identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the owning Needly user identifier.</summary>
    public Guid NeedlyUserId { get; private set; }

    /// <summary>Gets the display name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Gets the normalized name used for per-user uniqueness.</summary>
    public string NormalizedName { get; private set; } = string.Empty;

    /// <summary>Gets the versioned structured filter JSON.</summary>
    public string FilterJson { get; private set; } = string.Empty;

    /// <summary>Gets the user-defined display order.</summary>
    public int SortOrder { get; private set; }

    /// <summary>Gets when the view was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the view was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Creates a saved view.</summary>
    public static SavedView Create(
        Guid id,
        Guid needlyUserId,
        string name,
        string filterJson,
        int sortOrder,
        DateTimeOffset createdAt)
    {
        if (sortOrder < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sortOrder));
        }

        var timestamp = DomainGuard.Timestamp(createdAt);
        var validatedName = DomainGuard.Required(name, 100, nameof(name));
        return new SavedView
        {
            Id = DomainGuard.Required(id, nameof(id)),
            NeedlyUserId = DomainGuard.Required(needlyUserId, nameof(needlyUserId)),
            Name = validatedName,
            NormalizedName = NormalizeName(validatedName),
            FilterJson = DomainGuard.Required(filterJson, 16000, nameof(filterJson)),
            SortOrder = sortOrder,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };
    }

    /// <summary>Updates the view name and filter.</summary>
    public void Update(string name, string filterJson, DateTimeOffset updatedAt)
    {
        var validatedName = DomainGuard.Required(name, 100, nameof(name));
        Name = validatedName;
        NormalizedName = NormalizeName(validatedName);
        FilterJson = DomainGuard.Required(filterJson, 16000, nameof(filterJson));
        UpdatedAt = DomainGuard.NotBefore(updatedAt, CreatedAt, nameof(updatedAt));
    }

    /// <summary>Changes the view's display order.</summary>
    public void Reorder(int sortOrder, DateTimeOffset updatedAt)
    {
        if (sortOrder < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sortOrder));
        }

        SortOrder = sortOrder;
        UpdatedAt = DomainGuard.NotBefore(updatedAt, CreatedAt, nameof(updatedAt));
    }

    private static string NormalizeName(string name) => name.ToUpperInvariant();
}