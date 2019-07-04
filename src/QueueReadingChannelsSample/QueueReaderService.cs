using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QueueReadingChannelsSample.Configuration;
using QueueReadingChannelsSample.Sqs;

namespace QueueReadingChannelsSample
{
    public class QueueReaderService : BackgroundService
    {
        private readonly ILogger<QueueReaderService> _logger;
        private readonly IPollingSqsReader _pollingSqsReader;
        private readonly BoundedMessageChannel _boundedMessageChannel;

        private readonly int _maxTaskInstances;

        public QueueReaderService(
            ILogger<QueueReaderService> logger,
            IOptions<QueueReaderConfig> queueReadingConfig,
            IPollingSqsReader pollingSqsReader,
            BoundedMessageChannel boundedMessageChannel)
        {
            _logger = logger;
            _maxTaskInstances = queueReadingConfig.Value.MaxConcurrentReaders;
            _pollingSqsReader = pollingSqsReader;
            _boundedMessageChannel = boundedMessageChannel;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            stoppingToken.Register(() => _logger.LogWarning("Queue reader stopping!"));

            _logger.LogInformation("Starting queue reading service.");

            try
            {
                _logger.LogInformation("Starting {InstanceCount} queue reading tasks.", _maxTaskInstances);
                var tasks = Enumerable.Range(1, _maxTaskInstances).Select(PollForAndWriteMessages);
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                // swallow as nothing needs to know if the operation was cancelled in this background service;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An exception occurred during queue reading");
            }
            finally // ensure we always complete the writer even if exception occurs.
            {
                _boundedMessageChannel.CompleteWriter();
                _logger.LogInformation("Completed the queue reading service.");
            }

            async Task PollForAndWriteMessages(int instance)
            {
                var count = 0;

                try
                {
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        // safe to cancel at this point if the polling reader has not yet received messages
                        var messages = await _pollingSqsReader.PollForMessagesAsync(stoppingToken);

                        _logger.LogInformation("Read {MessageCount} messages from the queue in task instance {Instance}.", messages.Length, instance);

                        // once we have some messages we won't pass cancellation so we add them to the channel and have time to process them during shutdown
                        await _boundedMessageChannel.WriteMessagesAsync(messages);

                        count += messages.Length;
                    }
                }
                finally
                {
                    _logger.LogInformation("Finished writing in instance {Instance}. Wrote {TotalMessages} msgs.", count, instance);
                }
            }
        }
    }
}
