using CleanArchCqrs.Application.Common.Models.Files;

namespace CleanArchCqrs.Application.Features.Files.Queries.SearchFiles;

public static class ReciprocalRankFusion
{
    public static IReadOnlyList<RankedSearchFile> Rank(IEnumerable<SearchCandidate> candidates)
    {
        // Collapse chunks BEFORE assigning branch rank, so long files get one vote.
        var votes = candidates.GroupBy(c => c.Source, StringComparer.Ordinal)
            .SelectMany(branch => branch.GroupBy(c => c.FileId)
                .Select(file => file.OrderByDescending(c => c.Score).ThenBy(c => c.ChunkOrdinal).First())
                .OrderByDescending(c => c.Score).ThenBy(c => c.FileId)
                .Select((candidate, index) => new { Candidate = candidate, Vote = 1.0 / (60 + index + 1) }));
        return votes.GroupBy(v => v.Candidate.FileId)
            .Select(file => new RankedSearchFile(file.Key, file.First().Candidate.DocumentVersion,
                file.Sum(v => v.Vote), file.Select(v => v.Candidate).ToArray()))
            .OrderByDescending(file => file.Score).ThenBy(file => file.FileId).ToArray();
    }
}
