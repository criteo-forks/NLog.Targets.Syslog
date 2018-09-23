// Licensed under the BSD license
// See the LICENSE file in the project root for more information

using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NLog.Targets.Syslog.Extensions;
using NLog.Targets.Syslog.Settings;

namespace NLog.Targets.Syslog.MessageSend
{
    internal class Udp : MessageTransmitter
    {
        private readonly int connectionCheckTimeout;
        private UdpClient udp;

        protected override bool Ready
        {
            get { return udp?.Client?.IsConnected(connectionCheckTimeout) == true; }
        }

        public Udp(UdpConfig udpConfig) : base(udpConfig.Server, udpConfig.Port, udpConfig.ReconnectInterval)
        {
            connectionCheckTimeout = udpConfig.ConnectionCheckTimeout;
        }

        protected override void Init()
        {
            udp = new UdpClient(IpAddress, Port);
        }

        protected override void Send(ByteArray message, CancellationToken token)
        {
            if (token.IsCancellationRequested)
                return;
            udp.Send(message, message.Length);
        }

        protected override void Terminate()
        {
            udp?.Close();
        }
    }
}