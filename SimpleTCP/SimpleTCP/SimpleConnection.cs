using System;
using System.Collections.Generic;
using System.Threading;

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
}
