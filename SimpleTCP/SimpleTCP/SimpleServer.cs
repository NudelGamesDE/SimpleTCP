using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace SimpleTCP
{
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

}
