// Licensed under the BSD license
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using NLog.Common;
using NLog.Layouts;
using NLog.Targets.Syslog.Extensions;
using NLog.Targets.Syslog.MessageCreation;
using NLog.Targets.Syslog.MessageSend;
using NLog.Targets.Syslog.Policies;
using NLog.Targets.Syslog.Settings;

namespace NLog.Targets.Syslog
{
    internal class AsyncLogger
    {
        private readonly Layout layout;
        private readonly Throttling throttling;
        private readonly CancellationTokenSource cts;
        private readonly CancellationToken token;
        private readonly BlockingCollection<AsyncLogEventInfo> queue;
        private readonly ByteArray buffer;
        private readonly MessageTransmitter messageTransmitter;
        private readonly Task processingQueueTask;

        public AsyncLogger(Layout loggingLayout, EnforcementConfig enforcementConfig, MessageBuilder messageBuilder, MessageTransmitterConfig messageTransmitterConfig)
        {
            layout = loggingLayout;
            cts = new CancellationTokenSource();
            token = cts.Token;
            throttling = Throttling.FromConfig(enforcementConfig.Throttling);
            queue = NewBlockingCollection();
            buffer = new ByteArray(enforcementConfig.TruncateMessageTo);
            messageTransmitter = MessageTransmitter.FromConfig(messageTransmitterConfig);
            processingQueueTask = Task.Factory.StartNew(() => ProcessQueue(messageBuilder), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        public void Log(AsyncLogEventInfo asyncLogEvent)
        {
            throttling.Apply(queue.Count, timeout => Enqueue(asyncLogEvent, timeout));
        }

        private BlockingCollection<AsyncLogEventInfo> NewBlockingCollection()
        {
            var throttlingLimit = throttling.Limit;

            return throttling.BoundedBlockingCollectionNeeded ?
                new BlockingCollection<AsyncLogEventInfo>(throttlingLimit) :
                new BlockingCollection<AsyncLogEventInfo>();
        }

        private void ProcessQueue(MessageBuilder messageBuilder)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var asyncLogEventInfo = queue.Take(token);
                    var logEventMsgSet =
                        new LogEventMsgSet(asyncLogEventInfo, buffer, messageBuilder, messageTransmitter);
                    logEventMsgSet.Build(layout).Send(token);
                    InternalLogger.Debug("Successfully sent message '{0}'", logEventMsgSet);
                }
                catch (Exception ex)
                {
                    InternalLogger.Warn(ex, "ProcessQueue faulted within try");
                }
            }
            InternalLogger.Debug("Task canceled");
        }

        private void Enqueue(AsyncLogEventInfo asyncLogEventInfo, int timeout)
        {
            queue.TryAdd(asyncLogEventInfo, timeout, token);
            InternalLogger.Debug(() => $"Enqueued '{asyncLogEventInfo.ToFormattedMessage()}'");
        }

        public void Dispose()
        {
            cts.Cancel();
            cts.Dispose();
            queue.Dispose();
            buffer.Dispose();
            messageTransmitter.Dispose();
        }
    }
}