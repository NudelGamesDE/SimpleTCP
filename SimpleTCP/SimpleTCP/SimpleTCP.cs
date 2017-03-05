
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

using StateMachineManager;

namespace SimpleTCP
{
    public abstract class SimpleConnection
    {
        public const string Version = "1.4.5";

        public static Action<string> MessageHandler;
        public static Action<Exception> ExceptionHandler;

        public abstract bool Send(byte[] aData);
        private Action<byte[]> RealReceiveAction;

        public Action<byte[]> ReceiveAction
        {
            get
            {
                return isPolling ? PollReceiveAction : RealReceiveAction;
            }
            set { RealReceiveAction = value; }
        }
        public virtual bool Connected { get; protected set; }
        public virtual bool Connecting { get; protected set; }
        public bool Stoped { get; private set; }

        public virtual void Stop()
        {
            if (Stoped) return;
            Stoped = true;
            Connected = false;
            Connecting = false;
            lock (OpenConnectionsLocker)
                OpenConnections.Remove(this);
        }

        private Action<byte[]> PollReceiveAction;
        private bool isPolling;
        private readonly object PollLocker = new object();
        public T Poll<T>(byte[] aData, int aTimeout, Func<T> aStateMachineGenerator) where T : IStateMachine<byte>
        {
            return Poll(aData, aTimeout, Send, aStateMachineGenerator);
        }


        protected T Poll<T>(byte[] aData, int aTimeout, Predicate<byte[]> aSendAction, Func<T> aStateMachineGenerator) where T : IStateMachine<byte>
        {
            if (aStateMachineGenerator == null || aSendAction == null) return default(T);
            lock (PollLocker)
            {
                var _pollResetEvent = new ManualResetEvent(false);
                var _manager = new StateMachineManager<T, byte>(aStateMachineGenerator);
                var _pollReturn = default(T);

                PollReceiveAction = aMessage =>
                {

                    var _list = _manager.ApplyObjects(aMessage);
                    if (_list.Count <= 0) return;
                    _pollReturn = _list[0];
                    _pollResetEvent.Set();
                };

                isPolling = true;
                if (!aSendAction(aData))
                {
                    var _handler = MessageHandler; if (_handler != null) _handler("Couldn't send Polling Message");
                    isPolling = false;
                    return default(T);
                }
                _pollResetEvent.WaitOne(aTimeout);
                isPolling = false;
                return _pollReturn;
            }
        }


        private static readonly object OpenConnectionsLocker = new object();
        private static readonly List<SimpleConnection> OpenConnections = new List<SimpleConnection>();
        protected void AddToOpenConnections()
        {
            lock (OpenConnectionsLocker)
                OpenConnections.Add(this);
        }

        public static void StopAll()
        {
            var _handler = MessageHandler; if (_handler != null) _handler("Stopping all Connections");
            lock (OpenConnectionsLocker)
                foreach (var v in OpenConnections.ToArray())
                    v.Stop();
        }

    }


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

    public class SimpleServer : SimpleConnection
    {
        public int Port { get; private set; }

        private Socket socket;
        private readonly TcpListener listener;
        private SimpleServer childConnection;
        private readonly SimpleServer ParentConnection;
        private IPEndPoint EndPoint;

        public override bool Connected
        {
            get
            {
                var _child = childConnection;
                return base.Connected || (_child != null && _child.Connected);
            }
            protected set { base.Connected = value; }
        }

        public override bool Connecting
        {
            get
            {
                var _child = childConnection;
                return base.Connecting || (_child != null && _child.Connecting);
            }
            protected set { base.Connecting = value; }
        }

        public Action<byte[], IPEndPoint> ReceiveActionWithEndPoint;

        private void DoAction(byte[] aData, IPEndPoint aEndPoint)
        {
            if (ParentConnection != null)
            {
                ParentConnection.DoAction(aData, aEndPoint);
                return;
            }

            var _actionSimple = ReceiveAction;
            var _actionEndPoint = ReceiveActionWithEndPoint;

            if (_actionSimple != null) _actionSimple(aData);
            if (_actionEndPoint != null) _actionEndPoint(aData, aEndPoint);
        }


        private SimpleServer(int aPort, SimpleServer aParent)
        {
            Port = aPort;
            ParentConnection = aParent;
            listener = aParent.listener;
            Start();
        }
        public SimpleServer(int aPort)
        {
            Port = aPort;
            AddToOpenConnections();
            listener = new TcpListener(IPAddress.Any, Port);
            listener.Start();
            Start();
        }

        private void Start()
        {
            Connecting = true;
            new Thread(() =>
            {
                try
                {
                    while (!listener.Pending())
                    {
                        if (Stoped) return;
                        Thread.Sleep(100);
                    }
                    Connected = true;
                    socket = listener.AcceptSocket();
                    childConnection = new SimpleServer(Port, this);
                    EndPoint = socket.RemoteEndPoint as IPEndPoint;
                    var _handler = MessageHandler; if (_handler != null) _handler(string.Format("Connected with {0}/{1} -> {2}", EndPoint.Address, Port, EndPoint.Port));
                    StartReceive();
                }
                catch (Exception ex)
                {
                    var _handler = ExceptionHandler; if (_handler != null) _handler(ex);
                }
                Connecting = false;
            }).Start();
        }
        private static readonly object ChildParentLocker = new object();
        private void StartReceive()
        {
            if (Stoped) return;
            const int buffersize = 1000;
            var buffer = new byte[buffersize];
            socket.BeginReceive(buffer, 0, buffersize, SocketFlags.None, state =>
            {
                try
                {
                    var _bytesReceived = socket.EndReceive(state);
                    if (_bytesReceived > 0)
                    {
                        var _message = new byte[_bytesReceived];
                        for (var i = 0; i < _bytesReceived; i++)
                            _message[i] = buffer[i];
                        DoAction(_message, EndPoint);
                    }

                    if (_bytesReceived != 0)
                        StartReceive();
                    else
                        throw new Exception("got 0 data");
                }
                catch (Exception ex)
                {
                    lock (ChildParentLocker)
                    {
                        if (ParentConnection != null)
                            ParentConnection.childConnection = childConnection;
                    }
                    var _handler = ExceptionHandler; if (_handler != null) _handler(ex);
                    Connected = false;
                }
            }, null);
        }

        public void CloseConnectionTo(IPEndPoint aEndPoint)
        {
            lock (ChildParentLocker)
                UnlockedCloseConnectionTo(aEndPoint);
        }
        private void UnlockedCloseConnectionTo(IPEndPoint aEndPoint)
        {
            if (EndPoint == aEndPoint)
            {
                socket.Close();
                if (ParentConnection != null)
                    ParentConnection.childConnection = childConnection;
                return;
            }
            if (childConnection != null)
                childConnection.UnlockedCloseConnectionTo(aEndPoint);
        }


        private readonly object SendLocker = new object();
        public override bool Send(byte[] aData)
        {
            return Send(aData, null);
        }

        public bool Send(byte[] aData, IPEndPoint aEndPoint)
        {
            lock (SendLocker)
                return UnlockedSend(aData, aEndPoint);
        }

        public T Poll<T>(byte[] aData, int aTimeout, Func<T> aStateMachineGenerator, IPEndPoint aEndPoint) where T : IStateMachine<byte>
        {
            return Poll(aData, aTimeout, aSubData => Send(aSubData, aEndPoint), aStateMachineGenerator);
        }


        private bool UnlockedSend(byte[] aData, IPEndPoint aEndPoint)
        {
            if (Stoped)
            {
                var _handler = MessageHandler; if (_handler != null) _handler("Server already stopped");
                return false;
            }

            if (socket != null)
                if (aEndPoint == null || (EndPoint != null && EndPoint.Equals(aEndPoint)))
                {
                    try
                    {
                        socket.Send(aData);
                    }
                    catch (Exception ex)
                    {
                        var _handler = ExceptionHandler; if (_handler != null) _handler(ex);
                        return false;
                    }
                    if (aEndPoint != null) return true;
                }
            if (aEndPoint == null)
            {
                lock (ChildParentLocker)
                    if (childConnection != null) childConnection.UnlockedSend(aData, null);
                return true;
            }

            lock (ChildParentLocker)
                return childConnection != null && childConnection.UnlockedSend(aData, aEndPoint);

        }

        public List<IPEndPoint> GetEndPoints()
        {
            List<IPEndPoint> ret;
            lock (ChildParentLocker)
            {
                if (childConnection == null)
                {
                    ret = new List<IPEndPoint>();
                }
                else
                {
                    ret = childConnection.GetEndPoints();
                }
            }
            if (EndPoint != null) ret.Add(EndPoint);
            return ret;
        }


        public override void Stop()
        {
            var _handler = MessageHandler; if (_handler != null) _handler(string.Format("Stopped Server {0}", Port));
            lock (ChildParentLocker)
                UnlockedStop();
            listener.Stop();
        }

        private void UnlockedStop()
        {
            if (childConnection != null) childConnection.UnlockedStop();
            if (socket != null) socket.Close();
            base.Stop();
        }
    }

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




