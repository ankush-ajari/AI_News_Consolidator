using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace AiIntelligence.Infrastructure.Persistence.Configurations;

internal sealed class StringCollectionComparer : ValueComparer<IReadOnlyCollection<string>>
{
    public static readonly StringCollectionComparer Instance = new();

    private StringCollectionComparer()
        : base(
            (left, right) => left != null && right != null && left.SequenceEqual(right),
            collection => collection.Aggregate(0, (hash, value) => HashCode.Combine(hash, value.GetHashCode(StringComparison.Ordinal))),
            collection => collection.ToArray())
    {
    }
}
