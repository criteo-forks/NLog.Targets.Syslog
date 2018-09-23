// Licensed under the BSD license
// See the LICENSE file in the project root for more information

using System;
using System.Threading;
using System.Threading.Tasks;
using NLog.Common;
using NLog.Layouts;
using NLog.Targets.Syslog.Extensions;
using NLog.Targets.Syslog.MessageCreation;
using NLog.Targets.Syslog.MessageSend;

namespace NLog.Targets.Syslog
{
    internal class LogEventMsgSet
    {
        private AsyncLogEventInfo asyncLogEvent;
        private readonly ByteArray buffer;
        private readonly MessageBuilder messageBuilder;
        private readonly MessageTransmitter messageTransmitter;
        private int currentMessage;
        private string[] logEntries;

        public LogEventMsgSet(AsyncLogEventInfo asyncLogEvent, ByteArray buffer, MessageBuilder messageBuilder, MessageTransmitter messageTransmitter)
        {
            this.asyncLogEvent = asyncLogEvent;
            this.buffer = buffer;
            this.messageBuilder = messageBuilder;
            this.messageTransmitter = messageTransmitter;
            currentMessage = 0;
        }

        public LogEventMsgSet Build(Layout layout)
        {
            logEntries = messageBuilder.BuildLogEntries(asyncLogEvent.LogEvent, layout);
            return this;
        }

        public void Send(CancellationToken token)
        {
            while (currentMessage < logEntries.Length)
            {
                if (token.IsCancellationRequested)
                    return;

                try
                {
                    messageBuilder.PrepareMessage(buffer, asyncLogEvent.LogEvent, logEntries[currentMessage++]);
                    messageTransmitter.SendMessage(buffer, token);
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }
                }
                catch (Exception ex)
                {
                    asyncLogEvent.Continuation(ex);
                    throw;
                }
            }

            asyncLogEvent.Continuation(null);
        }

        public override string ToString()
        {
            return asyncLogEvent.ToFormattedMessage();
        }
    }
}