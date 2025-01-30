using System;
using System.Linq;
using System.Text;
using Crestron.SimplSharp;
using Crestron.SimplSharp.CrestronSockets;
using IPAddress = System.Net.IPAddress;


namespace ExampleSourceCode.Utilities
{
    /// <summary><para>
    /// Very simple class to handle communications with a TCP/IP client.
    /// Wraps around the Crestron TCPClient class.
    /// </para></summary>
    public class TcpIpWrapper
    {
        //The internal Crestron TCP client 
        private TCPClient _client;

        //Allows the auto reconnect method to know if the connection was manually stopped.
        private bool _userManuallyDisconnected;

        //Prevents multiple triggers of the auto reconnect method.
        private bool _reconnectAttemptInProgress;

        private CTimer _autoReconnectionTimer;
    
        /// <summary><para>
        /// The <see cref="IPAddress"/> this client will use to connect to the remote device.
        /// </para></summary>
        private IPAddress IpAddress { get; set; }
    
        /// <summary><para>
        /// The port this client will use to connect to the remote device.
        /// </para></summary>
        private int Port { get; set; }

        /// <summary><para>
        /// Property to set the auto reconnect behaviour of this client.
        /// </para></summary>
        
        /// <summary><para>
        /// Property to set the buffer size.
        /// </para></summary>
        private int BufferSize { get; set; }
        
        /// <summary><para>
        /// Property to set the auto reconnect behaviour.
        /// </para></summary>
        public bool AutoReconnect { get; set; }
    
        /// <summary><para>
        /// Amount of time (in seconds) to wait before attempting a reconnect.
        /// </para></summary>
        public ushort AutoReconnectInterval { get; set; } = 10;
        
        /// <summary><para>
        /// Amount of time (in seconds) to wait before a request officially times out.
        /// </para></summary>
        public int TimeoutInterval { get; private set; } = 1000;

        /// <summary><para>
        /// Property to tell if the client is currently connected.
        /// </para></summary>
        public bool IsClientCurrentlyConnected => _client.ClientStatus == SocketStatus.SOCKET_STATUS_CONNECTED;
    
        /// <summary><para>
        /// Event that is invoked whenever the client receives data.
        /// </para></summary>
        public event Action<string> OnTextReceived;

        public event Action OnClientConnected;

        /// <summary><para>
        /// Construct a new TCP Wrapper object for streamlined socket communications with networked devices.
        /// </para></summary>
        /// <param name="address">The <see cref="IPAddress"/> of the remote device.</param>
        /// <param name="port">The port this device is listening on.</param>
        /// <param name="bufferSize">Size of the internal buffer.</param>
        public TcpIpWrapper(IPAddress address, int port, int bufferSize)
        {
            IpAddress = address;
            Port = port;
            BufferSize = bufferSize;
            _autoReconnectionTimer = new CTimer(_ => Connect(), Timeout.Infinite);
            CrestronEnvironment.ProgramStatusEventHandler += type =>
            {
                if (type == eProgramStatusEventType.Stopping)
                    Disconnect();
            };
        }

        private void ClientOnSocketStatusChange(TCPClient mytcpclient, SocketStatus clientsocketstatus)
        {
            if (clientsocketstatus is not (SocketStatus.SOCKET_STATUS_LINK_LOST or SocketStatus.SOCKET_STATUS_BROKEN_REMOTELY or SocketStatus.SOCKET_STATUS_BROKEN_LOCALLY)) return;
            if (AutoReconnect)
                AutoReconnectMethod();
        }

        public void Connect()
        {
            if (_client != null && IsClientCurrentlyConnected)
            {
                CrestronConsole.PrintLine("TCP client is already connected.");
                return;
            }

            _client = new TCPClient(IpAddress.ToString(), Port, BufferSize);
            _client.SocketSendOrReceiveTimeOutInMs = TimeoutInterval; //1 second timeout
            _client.SocketStatusChange += ClientOnSocketStatusChange;
            _userManuallyDisconnected = false;
            _reconnectAttemptInProgress = true;
            _client.ConnectToServerAsync(ClientConnectResult);
        }

        private void ClientReceiveDataAsync(TCPClient theClient, int numberofbytesreceived)
        {
            CrestronConsole.PrintLine("Received {0} bytes", numberofbytesreceived);
            //Async method for receiving client data.

            //Don't need to do anything if there aren't any bytes to receive.
            if (numberofbytesreceived > 0)
            {
                //Create an array of the bytes received by the client.
                var theBuffer = _client.IncomingDataBuffer.Take(numberofbytesreceived).ToArray();

                //Convert the array of bytes to ascii (text) format.
                var toAscii = Encoding.ASCII.GetString(theBuffer);

                CrestronConsole.PrintLine("Received {0}", toAscii);

                //Invoke the text received event.
                OnTextReceived?.Invoke(toAscii);
            }

            //Call this method again to wait for more data.
            theClient.ReceiveDataAsync(ClientReceiveDataAsync);
        }

        private void ClientConnectResult(TCPClient theClient)
        {
            _reconnectAttemptInProgress = false;
            CrestronConsole.PrintLine("TCP client connect result:" + _client.ClientStatus);
            if (_client.ClientStatus == SocketStatus.SOCKET_STATUS_CONNECTED)
            {
                //Trigger the onConnectedEvent
                OnClientConnected?.Invoke();
                //Start listening for data.
                _client.ReceiveDataAsync(ClientReceiveDataAsync);
                return;
            }
            
            //Client failed to connect, so let's check if auto-reconnect is enabled.
            if (AutoReconnect)
                AutoReconnectMethod();

        }

        public void Disconnect()
        {
            if (_client.ClientStatus != SocketStatus.SOCKET_STATUS_CONNECTED)
                //Already disconnected
                return;
            _autoReconnectionTimer.Stop();
            _userManuallyDisconnected = true;
            _client.DisconnectFromServer();
        }

        public void SetTimeout(int seconds)
        {
            TimeoutInterval = seconds * 1000;
            if (_client != null) 
                _client.SocketSendOrReceiveTimeOutInMs = TimeoutInterval;
        }

        public void SendText(string toSend)
        {
            if (_client.ClientStatus != SocketStatus.SOCKET_STATUS_CONNECTED)
                throw new SocketException("Client is disconnected, cannot send text.");
        
            var bytes = Encoding.GetEncoding("ASCII").GetBytes(toSend);
            CrestronConsole.PrintLine("Sending over TCP: " + BitConverter.ToString(bytes));
            _client.SendDataAsync(bytes, bytes.Length, (client, sent) => {});
        }

        private void AutoReconnectMethod()
        {
            if (_reconnectAttemptInProgress || _userManuallyDisconnected) return;
            _autoReconnectionTimer.Reset(AutoReconnectInterval);
        }

        public void Dispose()
        {
            _client.Dispose();
        }
        
    }
}