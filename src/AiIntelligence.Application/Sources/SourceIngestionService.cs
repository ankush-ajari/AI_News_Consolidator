using AiIntelligence.Application.Persistence;
using AiIntelligence.Domain.Models;
using Microsoft.Extensions.Logging;

namespace AiIntelligence.Application.Sources;

public sealed class SourceIngestionService
{
    private readonly ISourceConnectorFactory _connectorFactory;
    private readonly IRawSourceRepository? _rawSourceRepository;
    private readonly ISourceDefinitionRepository? _sourceDefinitionRepository;
    private readonly ILogger<SourceIngestionService> _logger;

    public SourceIngestionService(
        ISourceConnectorFactory connectorFactory,
        ILogger<SourceIngestionService> logger)
        : this(connectorFactory, null, null, logger)
    {
    }

    public SourceIngestionService(
        ISourceConnectorFactory connectorFactory,
        IRawSourceRepository? rawSourceRepository,
        ILogger<SourceIngestionService> logger)
        : this(connectorFactory, rawSourceRepository, null, logger)
    {
    }

    public SourceIngestionService(
        ISourceConnectorFactory connectorFactory,
        IRawSourceRepository? rawSourceRepository,
        ISourceDefinitionRepository? sourceDefinitionRepository,
        ILogger<SourceIngestionService> logger)
    {
        _connectorFactory = connectorFactory;
        _rawSourceRepository = rawSourceRepository;
        _sourceDefinitionRepository = sourceDefinitionRepository;
        _logger = logger;
    }

    public async Task<SourceIngestionResult> FetchAsync(
        IEnumerable<SourceDefinition> sourceDefinitions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceDefinitions);

        var items = new List<RawSourceItem>();
        var failures = new List<SourceIngestionFailure>();

        foreach (var sourceDefinition in sourceDefinitions.Where(source => source.IsEnabled))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var startedAt = DateTimeOffset.UtcNow;
            _logger.LogInformation(
                "Starting source ingestion for {SourceName} ({SourceType}) at {StartedAt}.",
                sourceDefinition.Name,
                sourceDefinition.SourceType,
                startedAt);

            try
            {
                var connector = _connectorFactory.GetConnector(sourceDefinition.SourceType);
                _logger.LogInformation(
                    "Fetching source: Name: {SourceName}; Class: {SourceClass}; Type: {SourceType}; IsEnabled: {IsEnabled}; Connector: {ConnectorName}",
                    sourceDefinition.Name,
                    sourceDefinition.SourceClass,
                    sourceDefinition.SourceType,
                    sourceDefinition.IsEnabled,
                    connector.GetType().Name);

                var sourceItems = await connector.FetchAsync(sourceDefinition, cancellationToken).ConfigureAwait(false);

                items.AddRange(sourceItems);

                _logger.LogInformation(
                    "Completed source ingestion for {SourceName}. FetchedItemCount: {FetchedItemCount}; SkippedItemCount: {SkippedItemCount}.",
                    sourceDefinition.Name,
                    sourceItems.Count,
                    0);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning(
                    "Source ingestion was cancelled while processing {SourceName}.",
                    sourceDefinition.Name);
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Source ingestion failed for {SourceName} ({SourceType}). FailureReason: {FailureReason}",
                    sourceDefinition.Name,
                    sourceDefinition.SourceType,
                    exception.Message);

                failures.Add(new SourceIngestionFailure(
                    sourceDefinition,
                    exception.Message,
                    exception));
            }
        }

        return new SourceIngestionResult(items, failures);
    }

    public async Task<SourceIngestResult> IngestAsync(
        IEnumerable<SourceDefinition> sourceDefinitions,
        CancellationToken cancellationToken)
    {
        if (_rawSourceRepository is null)
        {
            throw new InvalidOperationException("A raw source repository is required for ingestion persistence.");
        }

        var sourceDefinitionList = sourceDefinitions.ToArray();
        if (_sourceDefinitionRepository is not null)
        {
            foreach (var sourceDefinition in sourceDefinitionList)
            {
                await _sourceDefinitionRepository.UpsertAsync(sourceDefinition, cancellationToken).ConfigureAwait(false);
            }

            await _sourceDefinitionRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var fetchResult = await FetchAsync(sourceDefinitionList, cancellationToken).ConfigureAwait(false);
        var insertedCount = 0;
        var duplicateCount = 0;

        foreach (var item in fetchResult.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation(
                "Evaluating raw item persistence. SourceClass: {SourceClass}; SourceDefinitionId: {SourceDefinitionId}; Url: {Url}; ContentHash: {ContentHash}",
                item.SourceClass,
                item.SourceDefinitionId,
                item.Url,
                item.ContentHash.Value);

            var existingItem = await _rawSourceRepository.FindByCanonicalUrlAndHashAsync(
                item.CanonicalUrl,
                item.ContentHash.Value,
                cancellationToken).ConfigureAwait(false);

            if (existingItem is not null)
            {
                duplicateCount++;
                _logger.LogInformation(
                    "Raw item duplicate decision. Decision: Duplicate; SourceClass: {SourceClass}; SourceDefinitionId: {SourceDefinitionId}; ExistingSourceDefinitionId: {ExistingSourceDefinitionId}; Url: {Url}; ContentHash: {ContentHash}",
                    item.SourceClass,
                    item.SourceDefinitionId,
                    existingItem.SourceDefinitionId,
                    item.Url,
                    item.ContentHash.Value);
                continue;
            }

            await _rawSourceRepository.AddAsync(item, cancellationToken).ConfigureAwait(false);
            insertedCount++;
            _logger.LogInformation(
                "Raw item insert decision. Decision: Insert; SourceClass: {SourceClass}; SourceDefinitionId: {SourceDefinitionId}; Url: {Url}; ContentHash: {ContentHash}",
                item.SourceClass,
                item.SourceDefinitionId,
                item.Url,
                item.ContentHash.Value);
        }

        await _rawSourceRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "Raw source repository SaveChangesAsync completed. InsertedCount: {InsertedCount}; DuplicateCount: {DuplicateCount}",
            insertedCount,
            duplicateCount);

        return new SourceIngestResult(
            fetchResult.Items.Count,
            insertedCount,
            duplicateCount,
            fetchResult.Failures);
    }
}
