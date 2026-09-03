namespace Needly.Domain;

internal static class DomainGuard
{
    public static Guid Required(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty identifier is required.", parameterName);
        }

        return value;
    }

    public static long Positive(long value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "The value must be positive.");
        }

        return value;
    }

    public static int Positive(int value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "The value must be positive.");
        }

        return value;
    }

    public static string Required(string? value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty value is required.", parameterName);
        }

        var trimmedValue = value.Trim();
        if (trimmedValue.Length > maximumLength)
        {
            throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", parameterName);
        }

        return trimmedValue;
    }

    public static string? Optional(string? value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Required(value, maximumLength, parameterName);
    }

    public static DateTimeOffset Timestamp(DateTimeOffset value) => value.ToUniversalTime();

    public static DateTimeOffset NotBefore(
        DateTimeOffset value,
        DateTimeOffset minimum,
        string parameterName)
    {
        var timestamp = Timestamp(value);
        if (timestamp < minimum)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "The timestamp cannot precede creation.");
        }

        return timestamp;
    }
}