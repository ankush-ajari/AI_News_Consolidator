using AiIntelligence.Domain.Enums;

namespace AiIntelligence.Domain.Models;

public sealed class TrendEvidence
{
    private TrendEvidence()
    {
        Topic = string.Empty;
        Period = string.Empty;
        Finding = string.Empty;
        QuantitativeEvidence = string.Empty;
        EvidenceSummary = string.Empty;
        PublicationName = string.Empty;
        SourceUrl = new Uri("about:blank");
    }

    public TrendEvidence(
        Guid id,
        Guid sourceItemId,
        string topic,
        string period,
        TrendEvidencePeriodProvenance periodProvenance,
        string finding,
        string evidenceSummary,
        decimal confidence,
        Uri sourceUrl,
        string quantitativeEvidence = "",
        string publicationName = "")
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Trend evidence id is required.", nameof(id));
        }

        if (sourceItemId == Guid.Empty)
        {
            throw new ArgumentException("Source item id is required.", nameof(sourceItemId));
        }

        if (string.IsNullOrWhiteSpace(topic))
        {
            throw new ArgumentException("Topic is required.", nameof(topic));
        }

        if (string.IsNullOrWhiteSpace(finding))
        {
            throw new ArgumentException("Finding is required.", nameof(finding));
        }

        if (confidence is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(confidence), "Confidence must be between 0 and 1.");
        }

        Id = id;
        SourceItemId = sourceItemId;
        Topic = topic.Trim();
        Period = period.TrimOrEmpty();
        PeriodProvenance = periodProvenance;
        Finding = finding.Trim();
        QuantitativeEvidence = quantitativeEvidence.TrimOrEmpty();
        EvidenceSummary = evidenceSummary.TrimOrEmpty();
        Confidence = confidence;
        SourceUrl = sourceUrl ?? throw new ArgumentNullException(nameof(sourceUrl));
        PublicationName = publicationName.TrimOrEmpty();
    }

    public Guid Id { get; private set; }

    public Guid SourceItemId { get; private set; }

    public string Topic { get; private set; }

    public string Period { get; private set; }

    public TrendEvidencePeriodProvenance PeriodProvenance { get; private set; }

    public string Finding { get; private set; }

    public string QuantitativeEvidence { get; private set; }

    public string EvidenceSummary { get; private set; }

    public decimal Confidence { get; private set; }

    public Uri SourceUrl { get; private set; }

    public string PublicationName { get; private set; }
}
