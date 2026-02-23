using Microsoft.Extensions.Configuration;

namespace TestIT.ApiTests.Helpers;

public class TestConfiguration
{
    private static IConfiguration? _configuration;

    public static IConfiguration Configuration
    {
        get
        {
            if (_configuration == null)
            {
                _configuration = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .Build();
            }
            return _configuration;
        }
    }

    public static string GetBaseUrl()
    {
        return Configuration["ApiSettings:BaseUrl"] ?? "https://exampractice.com/api";
    }

    /// <summary>
    /// Gets performance SLA threshold in milliseconds for a given category.
    /// Can be overridden via appsettings.json (PerformanceSla:{category}Ms) or
    /// environment variables (PerformanceSla__{category}Ms).
    /// </summary>
    /// <param name="category">SLA category: Read, List, Write, or Heavy</param>
    /// <param name="defaultMs">Default threshold in milliseconds</param>
    /// <returns>SLA threshold in milliseconds</returns>
    public static int GetSlaMs(string category, int defaultMs)
    {
        var key = $"PerformanceSla:{category}Ms";
        var value = Configuration[key];

        if (int.TryParse(value, out var slaMs))
        {
            return slaMs;
        }

        return defaultMs;
    }

}
