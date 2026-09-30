using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace AiIntelligence.Infrastructure.Persistence.Configurations;

internal sealed class UriCollectionComparer : ValueComparer<IReadOnlyCollection<Uri>>
{
    public static readonly UriCollectionComparer Instance = new();

    private UriCollectionComparer()
        : base(
            (left, right) => left != null && right != null && left.Select(uri => uri.AbsoluteUri).SequenceEqual(right.Select(uri => uri.AbsoluteUri)),
            collection => collection.Aggregate(0, (hash, uri) => HashCode.Combine(hash, uri.AbsoluteUri.GetHashCode(StringComparison.Ordinal))),
            collection => collection.ToArray())
    {
    }
}
