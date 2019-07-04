using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using QueueReadingChannelsSample.Configuration;
using QueueReadingChannelsSample.Sqs;

namespace QueueReadingChannelsSample
{
    public class Program
    {    
        public static void Main(string[] args)
        {
            try
            {
                CreateHostBuilder(args).Build().Run();
            }
            catch (OperationCanceledException)
            {
                // swallow
            }
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    services.Configure<HostOptions>(option =>
                    {
                        option.ShutdownTimeout = TimeSpan.FromSeconds(30);
                    });

                    services.Configure<QueueReaderConfig>(hostContext.Configuration.GetSection("QueueReading"));
                    services.Configure<MessageProcessingConfig>(hostContext.Configuration.GetSection("MessageProcessing"));
                    services.Configure<MessageChannelConfig>(hostContext.Configuration.GetSection("MessageChannel"));

                    services.AddSingleton<IPollingSqsReader, FakePollingSqsReader>();
                    services.AddSingleton<BoundedMessageChannel>();

                    // these are stopped in reverse order. We want the reader to stop first
                    // we will then allow the processor time to complete reading from the channel
                    services.AddHostedService<MessageProcessorService>();
                    services.AddHostedService<QueueReaderService>();                    
                });
    }
}
