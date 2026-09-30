namespace AiIntelligence.Domain.ValueObjects;

public sealed record ContentHash
{
    public ContentHash(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Content hash is required.", nameof(value));
        }

        Value = value.Trim();
    }

    public string Value { get; }

    public override string ToString() => Value;
}
