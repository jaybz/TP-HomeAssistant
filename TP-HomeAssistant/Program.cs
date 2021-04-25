using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TouchPortalApi;

namespace TP_HomeAssistant
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddHostedService<Worker>();
                    services.ConfigureTouchPointApi((opts) => {
                        opts.ServerIp = "127.0.0.1";
                        opts.ServerPort = 12136;
                        opts.PluginId = "HomeAssistant";
                    });
                });
    }
}
