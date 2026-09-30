namespace Instageram;

public sealed class InstagramOutreachService
{
    private readonly OrganicFollowEngine _engine;

    public InstagramOutreachService(
        OrganicFollowEngine engine)
    {
        _engine=engine;
    }

    public string PrepareIntroduction(
        string ownerProfileUrl,
        OrganicCandidate candidate)
    {
        return _engine.BuildInvitation(
            ownerProfileUrl,
            candidate);
    }
}
