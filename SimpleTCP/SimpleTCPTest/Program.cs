using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SimpleTCP;


namespace SimpleTCPTest
{
    class Program
    {
        public class testSM : IStateMachine<int>
        {
            private int sum = 0;
            public int Total = 100;

            public bool ApplyObject(int aInput)
            {
                sum += aInput;
                return sum <= Total;
            }

            public bool IsFinished()
            {
                return sum == Total;
            }
        }

        public class TestStateMachine : IStateMachine<byte>
        {
            private bool finished;

            public string data = "";

            public bool ApplyObject(byte aByte)
            {
                data += (char)aByte;

                finished = data == "1234";

                return aByte != (byte)'2' || data.Length > 1;
            }

            public bool IsFinished()
            {
                return finished;
            }
        }

        static void Main(string[] args)
        {
            Console.WriteLine("Version: {0}", SimpleTCP.SimpleConnection.Version);

            var _manager = new StateMachineManager<testSM, int>(() => new testSM());

            var _list = _manager.ApplyObjects(new[] { 2, 8, 90, 1, 1 });

            Console.WriteLine("StateMachineTest {0}", _list.Count == 2 ? "OK" : "FAILED");


            var testTime = 100;

            var _c1m = 0;
            var _c2m = 0;
            int _sm = 0;

            var _server = new SimpleServer(1807)
            {
                ReceiveAction = aData =>
                {
                    Console.WriteLine("Server: " + aData.Aggregate("", (current, v) => current + (char)v));
                    _sm++;
                }
            };

            Thread.Sleep(testTime);
            var _client1 = new SimpleClient("127.0.0.1", 1807)
            {
                ReceiveAction = aData =>
                {
                    Console.WriteLine("Client1: " + aData.Aggregate("", (current, v) => current + (char)v));
                    _c1m++;
                }
            };
            Thread.Sleep(testTime);
            _client1.Send(StringToByte("client1 message 1/4"));
            Thread.Sleep(testTime);
            _client1.Send(StringToByte("client1 message 2/4"));

            Thread.Sleep(testTime);
            var _client2 = new SimpleClient("127.0.0.1", 1807)
            {
                ReceiveAction = aData =>
                {
                    Console.WriteLine("Client2: " + aData.Aggregate("", (current, v) => current + (char)v));
                    _c2m++;
                }
            };

            Thread.Sleep(testTime);
            _client2.Send(StringToByte("client2 message 1/2"));
            Thread.Sleep(testTime);
            _client2.Send(StringToByte("client2 message 2/2"));
            Thread.Sleep(testTime);
            _client1.Send(StringToByte("client1 message 3/4"));

            Thread.Sleep(testTime);
            _server.Send(StringToByte("server message 1/2"));

            Thread.Sleep(testTime);
            Console.WriteLine("Server has {0} connections", _server.GetEndPoints().Count);



            IPEndPoint _testPoint = null;
            _server.ReceiveActionWithEndPoint = (aData, aEndPoint) =>
            {
                _testPoint = aEndPoint;
            };
            Thread.Sleep(testTime);
            _client1.Send(StringToByte("client1 message 4/4"));
            Thread.Sleep(testTime);
            if (_testPoint != null)
            {
                _server.Send(StringToByte("server message 2/2 (only for Client1)"), _testPoint);
            }
            else
            {
                Console.WriteLine("Server didn't get IpEndPoint");
            }

            var PollingTestOK = false;
            _client2.ReceiveAction = null;
            _server.ReceiveAction = null;
            Console.WriteLine("TestPolling:");
            Thread.Sleep(testTime);
            new Thread(() =>
            {
                Thread.Sleep(testTime);
                _server.Send(StringToByte("123"));
                Thread.Sleep(testTime);
                _server.Send(StringToByte("45"));
            }).Start();
            var _pollingReturn = _client1.Poll(StringToByte("Pollling Request"), testTime * 10, () => new TestStateMachine());
            if (_pollingReturn == null)
            {
                Console.WriteLine("Polling didn't receive Data");
            }
            else
            {
                Console.WriteLine("Polling returned: {0}", _pollingReturn.data);
                PollingTestOK = _pollingReturn.data == "1234";
            }


            Console.WriteLine("_clien1 should be Connected: {0}", _client1.Connected ? "Connected" : "Not Connected");
            _server.Stop();
            Thread.Sleep(testTime);
            _client1.Send(StringToByte("Not important Message"));
            Thread.Sleep(testTime);

            Console.WriteLine("_clien1 should not be Connected: {0}", _client1.Connected ? "Connected" : "Not Connected");

            Thread.Sleep(testTime);
            SimpleConnection.StopAll();
            Console.WriteLine("stopped all");
            Thread.Sleep(testTime);
            

            Console.WriteLine("\n");



            var _udpServer = new SimpleUDP(1808);
            var _udpClient1 = new SimpleUDP("127.0.0.1", 1808);
            var _udpClient2 = new SimpleUDP("255.255.255.255", 1808);

            var _udpms = 0;
            _udpServer.ReceiveAction = aData =>
            {
                Console.WriteLine("UDP-Server: " + aData.Aggregate("", (current, v) => current + (char)v));
                _udpms++;
            };
            Thread.Sleep(testTime);
            _udpClient1.Send(StringToByte("UDP message 1/2"));
            Thread.Sleep(testTime);
            _udpClient2.Send(StringToByte("UDP message 2/2"));

            Thread.Sleep(testTime);

            Console.WriteLine("\n{0}% Messages to server,\n{1}% messages to client 1,\n{2}% messages to client 2,\n{3}% Polling Test,\n{4}% UDP Messages", _sm / 6f * 100f, _c1m / 2f * 100f, _c2m / 1f * 100f, PollingTestOK ? 100f : 0f, _udpms / 2f * 100f);

            Console.WriteLine("Programm has to end in 5 Seconds");
            Thread.Sleep(5000);
        }

        static byte[] StringToByte(string aString)
        {
            return aString.Select(c => (byte)c).ToArray();
        }
    }
}
