using System.Collections.Concurrent;

namespace Instageram;

public enum OrganicCandidateStatus
{
    Discovered,
    Selected,
    WaitingForUser,
    Completed,
    Rejected,
    Failed
}

public sealed class OrganicCandidate
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Username { get; init; } = string.Empty;

    public string ProfileUrl { get; init; } = string.Empty;

    public string Country { get; init; } = string.Empty;

    public OrganicCandidateStatus Status { get; set; }

    public DateTime CreatedAtUtc { get; init; }
        = DateTime.UtcNow;

    public DateTime? UpdatedAtUtc { get; set; }

    public string? Error { get; set; }
}

public sealed class OrganicFollowEngine
{
    private readonly ConcurrentQueue<OrganicCandidate> _queue = new();

    public bool IsRunning { get; private set; }

    public int QueueCount => _queue.Count;

    public event Action<string>? Log;

    public event Action<OrganicCandidate>? CandidateChanged;

    public int EnqueueDiscovered(
        IEnumerable<OrganicCandidate> candidates)
    {
        if(candidates == null)
            throw new ArgumentNullException(nameof(candidates));

        var count=0;

        foreach(var candidate in candidates)
        {
            if(candidate == null)
                continue;

            if(string.IsNullOrWhiteSpace(candidate.Username))
                continue;

            if(string.IsNullOrWhiteSpace(candidate.ProfileUrl))
                continue;

            _queue.Enqueue(candidate);
            count++;
        }

        Log?.Invoke($"Queued {count} candidates.");

        return count;
    }

    public OrganicCandidate? GetNext()
    {
        if(!_queue.TryDequeue(out var candidate))
            return null;

        candidate.Status=
            OrganicCandidateStatus.WaitingForUser;

        candidate.UpdatedAtUtc=
            DateTime.UtcNow;

        CandidateChanged?.Invoke(candidate);

        return candidate;
    }

    public void Start()
    {
        IsRunning=true;
        Log?.Invoke("Manual Instagram workflow started.");
    }

    public void Pause()
    {
        IsRunning=false;
        Log?.Invoke("Workflow paused.");
    }

    public void Stop()
    {
        IsRunning=false;
        Log?.Invoke("Workflow stopped.");
    }

    public void MarkCompleted(
        OrganicCandidate candidate)
    {
        candidate.Status=
            OrganicCandidateStatus.Completed;

        candidate.UpdatedAtUtc=
            DateTime.UtcNow;

        CandidateChanged?.Invoke(candidate);
    }

    public void MarkRejected(
        OrganicCandidate candidate)
    {
        candidate.Status=
            OrganicCandidateStatus.Rejected;

        candidate.UpdatedAtUtc=
            DateTime.UtcNow;

        CandidateChanged?.Invoke(candidate);
    }

    public void MarkFailed(
        OrganicCandidate candidate,
        string error)
    {
        candidate.Status=
            OrganicCandidateStatus.Failed;

        candidate.Error=error;

        candidate.UpdatedAtUtc=
            DateTime.UtcNow;

        CandidateChanged?.Invoke(candidate);
    }

    public string BuildInvitation(
        string ownerProfileUrl,
        OrganicCandidate candidate)
    {
        if(string.IsNullOrWhiteSpace(ownerProfileUrl))
            throw new ArgumentException(
                "Owner profile URL is required.",
                nameof(ownerProfileUrl));

        return
            $"Hi @{candidate.Username}, " +
            $"you may find this Instagram profile interesting: " +
            ownerProfileUrl;
    }
}
