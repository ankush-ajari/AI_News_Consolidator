using AiIntelligence.Domain.Enums;

namespace AiIntelligence.Application.Intelligence;

public sealed class DeterministicTrendFamilyClassifier : ITrendFamilyClassifier
{
    public TrendFamily Classify(string topic, string finding, string evidenceSummary, IReadOnlyCollection<AIConceptTag> conceptTags)
    {
        var normalized = NormalizeText(string.Join(' ', topic, finding, evidenceSummary));

        if (ContainsAny(normalized, "osworld", "agent task", "task completion", "agent benchmark"))
        {
            return TrendFamily.AgentTaskPerformance;
        }

        if (ContainsAny(normalized, "agent reliability", "agent failure", "failure rate", "hallucination", "tool errors"))
        {
            return TrendFamily.AgentReliability;
        }

        if (ContainsAny(normalized, "u s china", "us china", "u.s china")
            && ContainsAny(normalized, "model", "performance", "benchmark", "leaderboard", "frontier"))
        {
            return TrendFamily.USChinaModelGap;
        }

        if (ContainsAny(normalized, "open closed", "open model", "closed model", "open vs closed", "closed vs open"))
        {
            return TrendFamily.OpenClosedModelGap;
        }

        if (ContainsAny(normalized, "benchmark reliability", "benchmark gaming", "invalid question", "invalid item", "leaderboard gaming")
            || (ContainsAny(normalized, "reliability") && ContainsAny(normalized, "benchmark", "evaluation", "score")))
        {
            return TrendFamily.BenchmarkReliability;
        }

        if (ContainsAny(normalized, "benchmark saturation", "benchmark lifespan", "benchmark outpaced"))
        {
            return TrendFamily.BenchmarkSaturation;
        }

        if (ContainsAny(normalized, "arena elo", "elo rating", "model convergence", "top models clustered", "leaderboard convergence"))
        {
            return TrendFamily.ModelPerformanceConvergence;
        }

        if (ContainsAny(normalized, "consumer adoption", "consumer uptake", "consumer usage", "consumer ai adoption", "adoption pace", "adoption speed")
            || ContainsAny(normalized, "adoption rate", "adoption reached", "generative ai adoption"))
        {
            return TrendFamily.ConsumerAIAdoption;
        }

        if (ContainsAny(normalized, "consumer value", "consumer surplus", "per user value"))
        {
            return TrendFamily.ConsumerAIValue;
        }

        if (ContainsAny(normalized, "organizational adoption", "enterprise adoption", "organizational ai", "enterprise ai"))
        {
            return TrendFamily.OrganizationalAIAdoption;
        }

        if (ContainsAny(normalized, "private ai investment", "ai investment", "ai funding", "venture funding", "mega round", "billion dollar")
            && ContainsAny(normalized, "u s", "us", "china", "europe", "uk", "japan", "korea", "india", "country", "countries"))
        {
            return TrendFamily.GeographicAIInvestment;
        }

        if (ContainsAny(normalized, "corporate ai investment", "ai investment growth", "investment doubled", "private investment", "ai funding", "funding growth", "mega round", "billion dollar"))
        {
            return TrendFamily.CorporateAIInvestment;
        }

        if (ContainsAny(normalized, "geographic investment", "regional investment", "investment concentration", "u.s. dominance", "us dominance"))
        {
            return TrendFamily.GeographicAIInvestment;
        }

        if (ContainsAny(normalized, "productivity", "time saved", "efficiency gains"))
        {
            return TrendFamily.AIProductivity;
        }

        if (ContainsAny(normalized, "workforce", "labor", "jobs", "employment"))
        {
            return TrendFamily.WorkforceImpact;
        }

        if (ContainsAny(normalized, "compute spend", "infrastructure spend", "gpu spend", "capex"))
        {
            return TrendFamily.ComputeInfrastructureSpend;
        }

        if (ContainsAny(normalized, "robot", "robotics real world", "reality gap", "household task"))
        {
            return TrendFamily.RoboticsRealWorldGap;
        }

        if (ContainsAny(normalized, "autonomous vehicle", "driverless", "av deployment"))
        {
            return TrendFamily.AutonomousVehicleDeployment;
        }

        if (ContainsAny(normalized, "jagged intelligence", "reasoning vs", "simple task"))
        {
            return TrendFamily.JaggedIntelligence;
        }

        return TrendFamily.Unknown;
    }

    private static string NormalizeText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim().ToLowerInvariant();
        var buffer = new char[trimmed.Length];
        var index = 0;
        var previousWasSeparator = false;

        foreach (var ch in trimmed)
        {
            if (char.IsLetterOrDigit(ch))
            {
                buffer[index++] = ch;
                previousWasSeparator = false;
                continue;
            }

            if (!previousWasSeparator)
            {
                buffer[index++] = ' ';
                previousWasSeparator = true;
            }
        }

        return new string(buffer, 0, index).Trim();
    }

    private static bool ContainsAny(string normalized, params string[] tokens)
    {
        return tokens.Any(token => normalized.Contains(token, StringComparison.Ordinal));
    }
}
