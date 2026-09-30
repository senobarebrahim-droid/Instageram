using System;
using System.Collections.Generic;

namespace Instageram.Services;

public sealed class CentralBrain
{
    private static readonly Lazy<CentralBrain> _instance =
        new(() => new CentralBrain());

    public static CentralBrain Instance => _instance.Value;

    public long ActiveCampaignId { get; private set; } = 0;

    public void SetActiveCampaign(long id)
    {
        ActiveCampaignId = id;
    }

    private CentralBrain()
{
    SyncCampaign();
}

    public void SyncCampaign()
    {
        ActiveCampaignId = DatabaseService.GetLatestCampaignId();
    }

    public long Remaining()
    {
        if(ActiveCampaignId <= 0)
            SyncCampaign();

        return DatabaseService.GetCampaignRemaining(ActiveCampaignId);
    }

    public void Complete()
    {
        if(ActiveCampaignId <= 0)
            SyncCampaign();

        DatabaseService.RegisterCampaignCompletion(ActiveCampaignId);
    }
}
