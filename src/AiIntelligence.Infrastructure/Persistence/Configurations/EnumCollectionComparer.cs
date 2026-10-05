using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace AiIntelligence.Infrastructure.Persistence.Configurations;

internal sealed class EnumCollectionComparer<TEnum> : ValueComparer<IReadOnlyCollection<TEnum>>
    where TEnum : struct, Enum
{
    public static readonly EnumCollectionComparer<TEnum> Instance = new();

    private EnumCollectionComparer()
        : base(
            (left, right) => left != null && right != null && left.SequenceEqual(right),
            collection => collection.Aggregate(0, (hash, value) => HashCode.Combine(hash, value.GetHashCode())),
            collection => collection.ToArray())
    {
    }
}
