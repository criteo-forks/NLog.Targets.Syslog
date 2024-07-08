// Licensed under the BSD license
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NLog.Common;
using NLog.Layouts;
using NLog.Targets.Syslog.Extensions;
using NLog.Targets.Syslog.MessageCreation;
using NLog.Targets.Syslog.MessageSend;
using NLog.Targets.Syslog.MessageStorage;
using NLog.Targets.Syslog.Policies;
using NLog.Targets.Syslog.Settings;

namespace NLog.Targets.Syslog
{
    internal class AsyncLogger
    {
        private readonly Layout layout;
        private readonly CancellationTokenSource cts;
        private readonly CancellationToken token;
        private readonly Channel<AsyncLogEventInfo> channel;
        private readonly ByteArray buffer;
        private readonly MessageTransmitter messageTransmitter;
        private readonly LogEventInfo flushCompletionMarker;

        public AsyncLogger(Layout loggingLayout, EnforcementConfig enforcementConfig, MessageBuilder messageBuilder, MessageTransmitterConfig messageTransmitterConfig)
        {
            layout = loggingLayout;
            cts = new CancellationTokenSource();
            token = cts.Token;
            channel = NewChannel(enforcementConfig.Throttling);
            buffer = new ByteArray(enforcementConfig.TruncateMessageTo);
            messageTransmitter = MessageTransmitter.FromConfig(messageTransmitterConfig);
            flushCompletionMarker = new LogEventInfo(LogLevel.Off, string.Empty, nameof(flushCompletionMarker));
            Task.Run(() => ProcessQueueAsync(messageBuilder));
        }

        public void Log(AsyncLogEventInfo asyncLogEventInfo)
        {
            Enqueue(asyncLogEventInfo);
        }

        public Task FlushAsync()
        {
            var flushTcs = new TaskCompletionSource<object>();
            Enqueue(flushCompletionMarker.WithContinuation(_ => flushTcs.SetResult(null)));
            return flushTcs.Task;
        }

        private Channel<AsyncLogEventInfo> NewChannel(ThrottlingConfig throttlingConfig)
        {
            return throttlingConfig.Strategy switch
            {
                ThrottlingStrategy.None => Channel.CreateUnbounded<AsyncLogEventInfo>(),
                ThrottlingStrategy.Discard => Channel.CreateBounded<AsyncLogEventInfo>(throttlingConfig.Limit),
                _ => throw new NotSupportedException($"Throttling strategy {throttlingConfig.Strategy} is not supported anymore")
            };
        }

        private async Task ProcessQueueAsync(MessageBuilder messageBuilder)
        {
            while (true)
            {
                try
                {
                    await ProcessQueueAsync(messageBuilder, new TaskCompletionSource());
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    InternalLogger.Warn(e, "[Syslog] ProcessQueueAsync faulted within try");
                }
            }
        }

        private async Task ProcessQueueAsync(MessageBuilder messageBuilder, TaskCompletionSource tcs)
        {
            await foreach (var asyncLogEventInfo in channel.Reader.ReadAllAsync(token))
            {
                LogEventMsgSet logEventMsgSet = new(asyncLogEventInfo, buffer, messageBuilder, messageTransmitter);
                try
                {
                    await logEventMsgSet.Build(layout).SendAsync(token);
                    if (InternalLogger.IsDebugEnabled)
                    {
                        InternalLogger.Debug("[Syslog] Successfully handled message '{0}'", logEventMsgSet);
                    }
                }
                catch (OperationCanceledException)
                {
                    InternalLogger.Debug("[Syslog] Task canceled");
                    tcs.SetCanceled(token);
                    return;
                }
                catch (Exception e)
                {
                    InternalLogger.Warn(e.GetBaseException(), "[Syslog] Task faulted");
                }
            }
        }

        private void Enqueue(AsyncLogEventInfo asyncLogEventInfo)
        {
            bool enqueued = channel.Writer.TryWrite(asyncLogEventInfo);

            if (InternalLogger.IsDebugEnabled)
            {
                InternalLogger.Debug("[Syslog] {0} '{1}'", enqueued ? "Enqueued" : "Failed enqueuing", asyncLogEventInfo.ToFormattedMessage());
            }

            if (!enqueued)
            {
                asyncLogEventInfo.Continuation(new InvalidOperationException("Failed enqueuing"));
            }
        }

        public void Dispose()
        {
            cts.Cancel();
            channel.Writer.Complete();
            messageTransmitter.Dispose();
            buffer.Dispose();
            cts.Dispose();
        }
    }
}
