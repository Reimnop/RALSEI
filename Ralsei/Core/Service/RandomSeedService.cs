using Microsoft.Extensions.Logging;

namespace Ralsei.Core.Service;

public class RandomSeedService(ILogger<RandomSeedService> logger)
{
    public event EventHandler<ulong>? SeedChanged;
    
    public ulong Seed
    {
        get;
        set
        {
            if (field == value)
                return;
            
            field = value;
            SeedChanged?.Invoke(this, field);
            
            logger.LogInformation("Using random seed '{Seed}'", field);
        }
    }
}