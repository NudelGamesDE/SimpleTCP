using System;
using System.Net;
using System.Net.Sockets;

namespace SimpleTCP
{
    public class SimpleUDP : SimpleConnection
    {
        private UdpClient client;
        private int port;

        public Action<byte[], IPEndPoint> ReceiveActionWithEndPoint;

        public SimpleUDP()
        {
            client = new UdpClient();
        }

        public SimpleUDP(int aPort)
        {
            client = new UdpClient(aPort);
            port = aPort;
            StartListening();
        }

        public SimpleUDP(string aIp, int aPort)
        {
            client = new UdpClient(aIp, aPort);
            port = aPort;
            StartListening();
        }

        private void StartListening()
        {
            client.BeginReceive(Receive, new object());
        }
        private void Receive(IAsyncResult ar)
        {
            IPEndPoint _endPoint = new IPEndPoint(IPAddress.Any, port);
            var _data = client.EndReceive(ar, ref _endPoint);
            if (Stoped) return;
            var _actionSimple = ReceiveAction;
            var _actionEndPoint = ReceiveActionWithEndPoint;
            if (_actionSimple != null) _actionSimple(_data);
            if (_actionEndPoint != null) _actionEndPoint(_data, _endPoint);
            StartListening();
        }

        public override bool Send(byte[] aData)
        {
            if (Stoped) return false;
            try
            {
                client.Send(aData, aData.Length);
            }
            catch (Exception ex)
            {
                var _handler = ExceptionHandler; if (_handler != null) _handler(ex);
                return false;
            }

            return true;
        }

        public bool Send(byte[] aData, IPEndPoint aEndPoint)
        {
            if (Stoped) return false;
            try
            {
                client.Send(aData, aData.Length, aEndPoint);
            }
            catch (Exception ex)
            {
                var _handler = ExceptionHandler; if (_handler != null) _handler(ex);
                return false;
            }

            return true;
        }

        public override void Stop()
        {
            client.Close();
            base.Stop();
        }

    }

}
