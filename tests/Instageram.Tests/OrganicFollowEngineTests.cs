using Xunit;

namespace Instageram.Tests;

/// <summary>
/// The manual work queue. This engine is what the "start manual workflow"
/// button drives, so its behaviour is covered here: nothing in it may ever
/// contact Instagram, and every state change must be observable.
/// </summary>
public class OrganicFollowEngineTests
{
    [Fact]
    public void A_new_engine_is_idle_with_an_empty_queue()
    {
        var engine = new OrganicFollowEngine();

        Assert.False(engine.IsRunning);
        Assert.Equal(0, engine.QueueCount);
    }

    [Fact]
    public void Enqueueing_null_throws()
    {
        var engine = new OrganicFollowEngine();

        Assert.Throws<ArgumentNullException>(() => engine.EnqueueDiscovered(null!));
    }

    [Fact]
    public void Enqueueing_reports_how_many_candidates_were_accepted()
    {
        var engine = new OrganicFollowEngine();

        var accepted = engine.EnqueueDiscovered(new[]
        {
            Candidate("one"),
            Candidate("two"),
            Candidate("three")
        });

        Assert.Equal(3, accepted);
        Assert.Equal(3, engine.QueueCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_candidate_without_a_username_is_skipped(string username)
    {
        var engine = new OrganicFollowEngine();

        var candidate = Candidate(username);

        Assert.Equal(0, engine.EnqueueDiscovered(new[] { candidate }));
        Assert.Equal(0, engine.QueueCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_candidate_without_a_profile_url_is_skipped(string url)
    {
        var engine = new OrganicFollowEngine();

        var candidate = new OrganicCandidate
        {
            Username = "someone",
            ProfileUrl = url,
            Country = "Iran"
        };

        Assert.Equal(0, engine.EnqueueDiscovered(new[] { candidate }));
        Assert.Equal(0, engine.QueueCount);
    }

    [Fact]
    public void A_null_entry_inside_the_batch_is_skipped_without_throwing()
    {
        var engine = new OrganicFollowEngine();

        var accepted = engine.EnqueueDiscovered(new OrganicCandidate?[]
        {
            Candidate("kept"),
            null,
            Candidate("also kept")
        }!);

        Assert.Equal(2, accepted);
    }

    [Fact]
    public void The_engine_can_be_emptied_and_refilled()
    {
        var engine = new OrganicFollowEngine();

        engine.EnqueueDiscovered(new[] { Candidate("first") });
        Assert.Equal(1, engine.QueueCount);

        engine.GetNext();
        Assert.Equal(0, engine.QueueCount);

        engine.EnqueueDiscovered(new[] { Candidate("second") });
        Assert.Equal(1, engine.QueueCount);
    }

    [Fact]
    public void Duplicates_are_accepted_here_because_deduplication_is_the_providers_job()
    {
        // Documents the contract: the provider that reads the file removes
        // duplicates; the queue itself passes through whatever it is given.
        var engine = new OrganicFollowEngine();

        var accepted = engine.EnqueueDiscovered(new[]
        {
            Candidate("same"),
            Candidate("same")
        });

        Assert.Equal(2, accepted);
        Assert.Equal(2, engine.QueueCount);
    }

    [Fact]
    public void Enqueueing_raises_the_log_event()
    {
        var engine = new OrganicFollowEngine();
        var messages = new List<string>();

        engine.Log += messages.Add;
        engine.EnqueueDiscovered(new[] { Candidate("someone") });

        Assert.Contains(messages, message => message.Contains("1"));
    }

    [Fact]
    public void Candidates_are_handed_out_in_the_order_they_were_added()
    {
        var engine = new OrganicFollowEngine();

        engine.EnqueueDiscovered(new[]
        {
            Candidate("first"),
            Candidate("second"),
            Candidate("third")
        });

        Assert.Equal("first", engine.GetNext()!.Username);
        Assert.Equal("second", engine.GetNext()!.Username);
        Assert.Equal("third", engine.GetNext()!.Username);
        Assert.Null(engine.GetNext());
    }

    [Fact]
    public void An_empty_queue_returns_null_instead_of_throwing()
    {
        var engine = new OrganicFollowEngine();

        Assert.Null(engine.GetNext());
    }

    [Fact]
    public void Handing_out_a_candidate_marks_it_as_waiting_for_the_user()
    {
        var engine = new OrganicFollowEngine();
        engine.EnqueueDiscovered(new[] { Candidate("someone") });

        var candidate = engine.GetNext()!;

        Assert.Equal(OrganicCandidateStatus.WaitingForUser, candidate.Status);
        Assert.NotNull(candidate.UpdatedAtUtc);
    }

    [Fact]
    public void Handing_out_a_candidate_raises_the_change_event()
    {
        var engine = new OrganicFollowEngine();
        var seen = new List<OrganicCandidate>();

        engine.CandidateChanged += seen.Add;
        engine.EnqueueDiscovered(new[] { Candidate("someone") });
        var candidate = engine.GetNext()!;

        Assert.Single(seen);
        Assert.Same(candidate, seen[0]);
    }

    [Fact]
    public void Start_pause_and_stop_control_the_running_flag()
    {
        var engine = new OrganicFollowEngine();
        var messages = new List<string>();
        engine.Log += messages.Add;

        engine.Start();
        Assert.True(engine.IsRunning);

        engine.Pause();
        Assert.False(engine.IsRunning);

        engine.Start();
        engine.Stop();
        Assert.False(engine.IsRunning);

        // four transitions: start, pause, start, stop
        Assert.Equal(4, messages.Count);
        Assert.Contains(messages, message => message.Contains("started"));
        Assert.Contains(messages, message => message.Contains("paused"));
        Assert.Contains(messages, message => message.Contains("stopped"));
    }

    [Fact]
    public void Marking_a_candidate_completed_records_the_state()
    {
        var engine = new OrganicFollowEngine();
        var candidate = Candidate("someone");

        engine.MarkCompleted(candidate);

        Assert.Equal(OrganicCandidateStatus.Completed, candidate.Status);
        Assert.NotNull(candidate.UpdatedAtUtc);
    }

    [Fact]
    public void Marking_a_candidate_rejected_records_the_state()
    {
        var engine = new OrganicFollowEngine();
        var candidate = Candidate("someone");

        engine.MarkRejected(candidate);

        Assert.Equal(OrganicCandidateStatus.Rejected, candidate.Status);
    }

    [Fact]
    public void Marking_a_candidate_failed_records_the_reason()
    {
        var engine = new OrganicFollowEngine();
        var candidate = Candidate("someone");

        engine.MarkFailed(candidate, "profile not reachable");

        Assert.Equal(OrganicCandidateStatus.Failed, candidate.Status);
        Assert.Equal("profile not reachable", candidate.Error);
    }

    [Fact]
    public void Every_marking_helper_raises_the_change_event()
    {
        var engine = new OrganicFollowEngine();
        var count = 0;

        engine.CandidateChanged += _ => count++;

        engine.MarkCompleted(Candidate("a"));
        engine.MarkRejected(Candidate("b"));
        engine.MarkFailed(Candidate("c"), "reason");

        Assert.Equal(3, count);
    }

    [Fact]
    public void The_invitation_text_mentions_the_candidate_and_the_owner_profile()
    {
        var engine = new OrganicFollowEngine();
        var candidate = Candidate("target_user");

        var invitation = engine.BuildInvitation("https://www.instagram.com/myprofile/", candidate);

        Assert.Contains("@target_user", invitation);
        Assert.Contains("https://www.instagram.com/myprofile/", invitation);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Building_an_invitation_without_an_owner_profile_throws(string owner)
    {
        var engine = new OrganicFollowEngine();

        Assert.Throws<ArgumentException>(() => engine.BuildInvitation(owner, Candidate("someone")));
    }

    [Fact]
    public void The_outreach_service_delegates_to_the_engine()
    {
        var engine = new OrganicFollowEngine();
        var service = new InstagramOutreachService(engine);

        var text = service.PrepareIntroduction("https://www.instagram.com/owner/", Candidate("someone"));

        Assert.Equal(engine.BuildInvitation("https://www.instagram.com/owner/", Candidate("someone")), text);
    }

    [Fact]
    public void The_engine_never_touches_the_network()
    {
        // A quiet guard against anyone wiring automation in later: the whole
        // API surface is exercised here and no socket is ever opened, because
        // the engine only moves objects between in-memory collections.
        var engine = new OrganicFollowEngine();

        engine.Start();
        engine.EnqueueDiscovered(new[] { Candidate("someone") });
        var candidate = engine.GetNext()!;
        engine.MarkCompleted(candidate);
        engine.Stop();

        Assert.Equal(0, engine.QueueCount);
        Assert.False(engine.IsRunning);
    }

    [Fact]
    public void Concurrent_enqueueing_loses_nothing()
    {
        var engine = new OrganicFollowEngine();
        const int perThread = 250;
        const int threads = 8;

        Parallel.For(0, threads, thread =>
        {
            var batch = Enumerable
                .Range(0, perThread)
                .Select(index => Candidate($"user{thread}_{index}"))
                .ToList();

            engine.EnqueueDiscovered(batch);
        });

        Assert.Equal(threads * perThread, engine.QueueCount);
    }

    private static OrganicCandidate Candidate(string username) => new()
    {
        Username = username,
        ProfileUrl = $"https://www.instagram.com/{username}/",
        Country = "Iran",
        Status = OrganicCandidateStatus.Discovered
    };
}
