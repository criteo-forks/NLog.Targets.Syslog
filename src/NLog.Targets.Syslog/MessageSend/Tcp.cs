// Licensed under the BSD license
// See the LICENSE file in the project root for more information

using System;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog.Targets.Syslog.Extensions;
using NLog.Targets.Syslog.Settings;

namespace NLog.Targets.Syslog.MessageSend
{
    internal class Tcp : MessageTransmitter
    {
        private static readonly byte[] LineFeedBytes = { 0x0A };

        private readonly int connectionCheckTimeout;
        private readonly KeepAliveConfig keepAliveConfig;
        private readonly bool useTls;
        private readonly Func<X509Certificate2Collection> retrieveClientCertificates;
        private readonly int dataChunkSize;
        private readonly FramingMethod framing;
        private TcpClient tcp;
        private Stream stream;

        protected override bool Ready
        {
            get { return tcp?.Connected == true && tcp.Client.IsConnected(connectionCheckTimeout) == true; }
        }

        public Tcp(TcpConfig tcpConfig) : base(tcpConfig.Server, tcpConfig.Port, tcpConfig.ReconnectInterval)
        {
            connectionCheckTimeout = tcpConfig.ConnectionCheckTimeout;
            keepAliveConfig = tcpConfig.KeepAlive;
            useTls = tcpConfig.Tls.Enabled;
            retrieveClientCertificates = tcpConfig.Tls.RetrieveClientCertificates;
            framing = tcpConfig.Framing;
            dataChunkSize = tcpConfig.DataChunkSize;
        }

        protected override Task Init()
        {
            tcp = new TcpClient();
            var socketInitialization = SocketInitialization.ForCurrentOs(tcp.Client);
            socketInitialization.DisableAddressSharing();
            socketInitialization.DiscardPendingDataOnClose();
            socketInitialization.SetKeepAlive(keepAliveConfig);

            return tcp
                .ConnectAsync(IpAddress, Port)
                .Then(_ => stream = SslDecorate(tcp), CancellationToken.None);
        }

        protected override void Send(ByteArray message, CancellationToken token)
        {
            if (token.IsCancellationRequested)
                return;

            FramingTask(message);
            Write(0, message, token);
        }

        protected override void Terminate()
        {
            DisposeSslStreamNotTcpClientInnerStream();
            DisposeTcpClientAndItsInnerStream();
        }

        private Stream SslDecorate(TcpClient tcpClient)
        {
            var tcpStream = tcpClient.GetStream();

            if (!useTls)
                return tcpStream;

            // Do not dispose TcpClient inner stream when disposing SslStream (TcpClient disposes it)
            var sslStream = new SslStream(tcpStream, true);
            sslStream.AuthenticateAsClient(Server, retrieveClientCertificates(), SslProtocols.Tls12, false);

            return sslStream;
        }

        private void FramingTask(ByteArray message)
        {
            if (framing == FramingMethod.NonTransparent)
            {
                message.Append(LineFeedBytes);
                return;
            }

            var octetCount = message.Length;
            var prefix = new ASCIIEncoding().GetBytes($"{octetCount} ");
            stream.Write(prefix, 0, prefix.Length);
        }

        private void Write(int offset, ByteArray data, CancellationToken token)
        {
            if (token.IsCancellationRequested)
                return;

            var toBeWrittenTotal = data.Length - offset;
            var isLastWrite = toBeWrittenTotal <= dataChunkSize;
            var count = isLastWrite ? toBeWrittenTotal : dataChunkSize;
            while(offset < data.Length)
            { 
                stream.Write(data, offset, count);
                offset += dataChunkSize;
            }
        }

        private void DisposeSslStreamNotTcpClientInnerStream()
        {
            if (useTls)
                stream?.Dispose();
        }

        private void DisposeTcpClientAndItsInnerStream()
        {
            tcp?.Close();
        }
    }
}