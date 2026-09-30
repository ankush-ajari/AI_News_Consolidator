namespace AiIntelligence.Application.Intelligence;

public interface ILLMClient
{
    Task<LLMResponse> CompleteAsync(LLMRequest request, CancellationToken cancellationToken);
}
