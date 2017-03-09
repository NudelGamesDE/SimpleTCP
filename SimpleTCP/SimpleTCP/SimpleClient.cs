using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace SimpleTCP
{
    public class SimpleClient : SimpleConnection
    {
        public string Ip { get; private set; }
        public int Port { get; private set; }

        public bool TryToReconnect = true;

        private Stream stream;
        private TcpClient client;

        public SimpleClient(string aIp, int aPort)
        {
            Ip = aIp;
            Port = aPort;
            AddToOpenConnections();
            Connect();
        }

        private void StartReceive()
        {
            try
            {
                if (Stoped) return;
                var buffer = new byte[1000];
                stream.BeginRead(buffer, 0, 1000, state =>
                {
                    try
                    {
                        var _action = ReceiveAction;
                        var _bytesReceived = stream.EndRead(state);
                        if (_action != null)
                        {
                            var _message = new byte[_bytesReceived];
                            for (var i = 0; i < _bytesReceived; i++)
                                _message[i] = buffer[i];
                            _action(_message);
                        }

                        StartReceive();
                    }
                    catch (Exception ex)
                    {
                        var _handler = ExceptionHandler; if (_handler != null) _handler(ex);
                        Connected = false;
                        if (TryToReconnect)
                        {
                            var _otherHandler = MessageHandler; if (_otherHandler != null) _otherHandler("try to reconnect");
                            Connect();
                        }
                    }
                }, null);
            }
            catch (Exception ex)
            {
                var _handler = ExceptionHandler; if (_handler != null) _handler(ex);
                Connected = false;
                if (TryToReconnect)
                {
                    var _otherHandler = MessageHandler; if (_otherHandler != null) _otherHandler("try to reconnect");
                    Connect();
                }
            }
        }

        private readonly object ConnectLocker = new object();
        private void Connect()
        {
            lock (ConnectLocker)
            {
                if (Connecting || Connected) return;
                Connecting = true;
                new Thread(ConnectThread).Start();
            }
        }

        private void ConnectThread()
        {
            Connecting = true;
            if (client == null)
                client = new TcpClient();
            else
            {
                client.Close();
                stream.Close();
                client = new TcpClient();
            }

            var _tcpConnected = false;
            while (!Stoped && !_tcpConnected)
            {
                try
                {
                    client.Connect(Ip, Port);
                    _tcpConnected = true;
                    var _handler = MessageHandler; if (_handler != null) _handler(string.Format("Connected with {0}/{1}", Ip, Port));
                }
                catch (Exception ex)
                {
                    var _handler = ExceptionHandler; if (_handler != null) _handler(ex);
                }
            }
            if (_tcpConnected)
            {
                stream = client.GetStream();
                stream.WriteTimeout = 1000;
                StartReceive();
                Connected = true;
            }
            lock (ConnectLocker)
                Connecting = false;
        }

        private readonly object SendLocker = new object();
        public override bool Send(byte[] aData)
        {
            if (Stoped) return false;
            Connect();
            return RealSend(aData);
        }

        private bool RealSend(byte[] aData)
        {
            lock (SendLocker)
            {
                if (stream == null) return false;
                if (!client.Connected)
                {
                    Connected = false;
                    return false;
                }
                try
                {
                    stream.Write(aData, 0, aData.Length);
                }
                catch (Exception ex)
                {
                    var _handler = ExceptionHandler; if (_handler != null) _handler(ex);
                    Connected = false;
                    return false;
                }
                return true;
            }
        }

        private readonly object StopLocker = new object();
        public override void Stop()
        {
            var _handler = MessageHandler; if (_handler != null) _handler(string.Format("Stopped Client {0}/{1}", Ip, Port));
            lock (StopLocker)
            {
                if (stream != null) stream.Close();
                if (client != null) client.Close();
                base.Stop();
            }
        }
    }

}
