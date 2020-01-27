using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace CloudMoePortProxy
{
    class Program
    {
        static ProxyConfig proxyConfig = new ProxyConfig(); 
        static void Main(string[] args)
        {
            Console.WriteLine("CloudMoe Port Proxy");
            Console.WriteLine($"Version: {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString()}");
            Console.WriteLine("By TGSAN");
            Console.WriteLine();
            if (args.Length < 4)
            {
                Console.WriteLine(
                    "Usage:\r\n" +
                    Path.GetFileName(Process.GetCurrentProcess().MainModule.FileName) + " <Forwarder IP Address> <Forwarder Port> <Listen IP Address> <Listen Port> [-v]" +
                    "-v\tEnable Verbose."
                    );
                return;
            }
            try
            {
                proxyConfig.ForwarderAddress = IPAddress.Parse(args[0]);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("[E] Parse Forwarder IP Address E: " + e.Message);
                return;
            }
            try 
            {
                proxyConfig.ForwarderPort = int.Parse(args[1]);
            }
            catch(Exception e)
            {
                Console.Error.WriteLine("[E] Parse Forwarder Port E: " + e.Message);
                return;
            }
            try
            {
                proxyConfig.ListenAddress = IPAddress.Parse(args[2]);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("[E] Parse Listen IP Address E: " + e.Message);
                return;
            }
            try
            {
                proxyConfig.ListenPort = int.Parse(args[3]);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("[E] Parse Listen Port E: " + e.Message);
                return;
            }
            if (args.Length >= 5 && args[4].ToString() == "-v")
            {
                proxyConfig.Verbose = true;
            }
            new TcpForwarderSlim().Start(
            new IPEndPoint(proxyConfig.ListenAddress, proxyConfig.ListenPort),
            new IPEndPoint(proxyConfig.ForwarderAddress, proxyConfig.ForwarderPort));
        }
        public static void ConsoleWriteLine(string str)
        {
            if (proxyConfig.Verbose)
            {
                Console.WriteLine(str);
            }
        }
    }
    public class TcpForwarderSlim
    {
        private readonly Socket _mainSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        public void Start(IPEndPoint local, IPEndPoint remote)
        {
            Console.WriteLine("Configuration:");
            Console.WriteLine("Listen: " + local.ToString());
            Console.WriteLine("Forwarder: " + remote.ToString());
            Console.WriteLine();
            Console.WriteLine("[I] Starting...");
            _mainSocket.Bind(local);
            _mainSocket.Listen(10);
            Console.WriteLine("[I] Listen on " + local.ToString());
            while (true)
            {
                var source = _mainSocket.Accept();
                try
                {
                    var destination = new TcpForwarderSlim();
                    var state = new State(source, destination._mainSocket);
                    destination.Connect(remote, source);
                    Program.ConsoleWriteLine("[I] Connect " + local.ToString() + " <=> " + remote.ToString());
                    source.BeginReceive(state.Buffer, 0, state.Buffer.Length, 0, OnDataReceive, state);
                    Program.ConsoleWriteLine("[I] Disconnected " + local.ToString() + " <=> " + remote.ToString());
                }
                catch
                {
                    source.Close();
                    //Program.ConsoleWriteLine("[I] Forwarder Target Disconnected, wait for 3s.");
                    //Thread.Sleep(3000);
                }
            }
        }

        private void Connect(EndPoint remoteEndpoint, Socket destination)
        {
            var state = new State(_mainSocket, destination);
            _mainSocket.Connect(remoteEndpoint);
            _mainSocket.BeginReceive(state.Buffer, 0, state.Buffer.Length, SocketFlags.None, OnDataReceive, state);
        }

        private static void OnDataReceive(IAsyncResult result)
        {
            var state = (State)result.AsyncState;
            try
            {
                var bytesRead = state.SourceSocket.EndReceive(result);
                if (bytesRead > 0)
                {
                    //Start an asyncronous send.
                    var sendAr = state.DestinationSocket.BeginSend(state.Buffer, 0, bytesRead, SocketFlags.None, null, null);

                    //Get or create a new buffer for the state object.
                    var oldBuffer = state.ReplaceBuffer();

                    state.SourceSocket.BeginReceive(state.Buffer, 0, state.Buffer.Length, 0, OnDataReceive, state);

                    //Wait for the send to finish.
                    state.DestinationSocket.EndSend(sendAr);

                    //Return byte[] to the pool.
                    state.AddBufferToPool(oldBuffer);
                }
            }
            catch
            {
                state.DestinationSocket.Close();
                state.SourceSocket.Close();
            }
        }

        private class State
        {
            private readonly ConcurrentBag<byte[]> _bufferPool = new ConcurrentBag<byte[]>();
            private readonly int _bufferSize;
            public Socket SourceSocket { get; private set; }
            public Socket DestinationSocket { get; private set; }
            public byte[] Buffer { get; private set; }

            public State(Socket source, Socket destination)
            {
                SourceSocket = source;
                DestinationSocket = destination;
                _bufferSize = Math.Min(SourceSocket.ReceiveBufferSize, DestinationSocket.SendBufferSize);
                Buffer = new byte[_bufferSize];
            }

            /// <summary>
            /// Replaces the buffer in the state object.
            /// </summary>
            /// <returns>The previous buffer.</returns>
            public byte[] ReplaceBuffer()
            {
                byte[] newBuffer;
                if (!_bufferPool.TryTake(out newBuffer))
                {
                    newBuffer = new byte[_bufferSize];
                }
                var oldBuffer = Buffer;
                Buffer = newBuffer;
                return oldBuffer;
            }

            public void AddBufferToPool(byte[] buffer)
            {
                _bufferPool.Add(buffer);
            }
        }
    }
}

