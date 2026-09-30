namespace Instageram;

public class CampaignActivity
{
    public long Id { get; set; }
    public long CampaignId { get; set; }
    public string Type { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public static class ActivityTracker
{
    private static readonly List<CampaignActivity> Activities = new();

    public static void Register(long campaignId,string type,string description)
    {
        Activities.Add(new CampaignActivity
        {
            CampaignId = campaignId,
            Type = type,
            Description = description,
            CreatedAt = DateTime.Now
        });
    }

    public static int Count(long campaignId)
    {
        return Activities.Count(x=>x.CampaignId==campaignId);
    }
}
