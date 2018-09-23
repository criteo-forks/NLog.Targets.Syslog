// Licensed under the BSD license
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NLog.Targets.Syslog.Extensions;
using NLog.Targets.Syslog.Settings;

namespace NLog.Targets.Syslog.MessageSend
{
    internal abstract class MessageTransmitter
    {
        private static readonly Dictionary<ProtocolType, Func<MessageTransmitterConfig, MessageTransmitter>> TransmitterFactory;
        protected static readonly TimeSpan ZeroSecondsTimeSpan = TimeSpan.FromSeconds(0);

        private volatile bool neverConnected;
        private readonly TimeSpan recoveryTime;
        protected volatile bool disposed;

        protected string Server { get; }

        protected string IpAddress => Dns.GetHostAddresses(Server).FirstOrDefault()?.ToString();

        protected int Port { get; }

        protected abstract bool Ready { get; }

        static MessageTransmitter()
        {
            TransmitterFactory = new Dictionary<ProtocolType, Func<MessageTransmitterConfig, MessageTransmitter>>
            {
                { ProtocolType.Udp, messageTransmitterConfig => new Udp(messageTransmitterConfig.Udp) },
                { ProtocolType.Tcp, messageTransmitterConfig => new Tcp(messageTransmitterConfig.Tcp) }
            };
        }

        public static MessageTransmitter FromConfig(MessageTransmitterConfig messageTransmitterConfig)
        {
            return TransmitterFactory[messageTransmitterConfig.Protocol](messageTransmitterConfig);
        }

        protected MessageTransmitter(string server, int port, int reconnectInterval)
        {
            neverConnected = true;
            recoveryTime = TimeSpan.FromMilliseconds(reconnectInterval);
            Server = server;
            Port = port;
        }

        public void SendMessage(ByteArray message, CancellationToken token)
        {
            if (token.IsCancellationRequested)
                return;

            if (!neverConnected && Ready)
            {
                Send(message, token);
                return;
            }
                

            var delay = neverConnected ? ZeroSecondsTimeSpan : recoveryTime;
            neverConnected = false;
            Thread.Sleep(delay);
            ReInit();
            Send(message, token);
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            Terminate();
        }

        protected abstract Task Init();

        protected abstract void Send(ByteArray message, CancellationToken token);

        protected abstract void Terminate();

        private Task ReInit()
        {
            Terminate();
            return Init();
        }
    }
}