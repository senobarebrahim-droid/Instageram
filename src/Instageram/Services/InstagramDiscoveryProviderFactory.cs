namespace Instageram;

public static class InstagramDiscoveryProviderFactory
{
    public static IInstagramDiscoveryProvider Create()
    {
        return new ImportedInstagramDiscoveryProvider();
    }
}
