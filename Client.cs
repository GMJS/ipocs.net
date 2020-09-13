using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IPOCS
{
    public class Client
    {
        private TcpClient TcpClient { get; }
        private Thread ClientReadThread { get; }
        private Timer StaleTimer { get; }
        public string Name { get; set; } = string.Empty;
        public System.Net.EndPoint RemoteEndpoint
        {
            get
            {
                return TcpClient.Client.RemoteEndPoint;
            }
        }

        public bool Connected
        {
            get
            {
                return this.TcpClient?.Client != null ? this.TcpClient.Connected : false;
            }
        }

        public Client(TcpClient client)
        {
            this.TcpClient = client;
            this.ClientReadThread = new Thread(new ThreadStart(this.ClientReader));
            this.StaleTimer = new Timer(new TimerCallback(StaleTimerFunc), null, 1000, Timeout.Infinite);
            this.ClientReadThread.Start();
        }

        public delegate void OnDisconnectDelegate(Client client);
        public event OnDisconnectDelegate OnDisconnect;

        public delegate bool? OnConnectionRequestDelegate(Client client, Protocol.Packets.ConnectionRequest request);
        public OnConnectionRequestDelegate OnConnectionRequest;

        public OnDisconnectDelegate OnConnect;

        public delegate void OnMessageDelegate(IPOCS.Protocol.Message msg);
        public event OnMessageDelegate OnMessage;
        //public ObjectTypes.Concentrator unit { get; private set; } = null;

        void StaleTimerFunc(object state)
        {
            this.TcpClient.Close();
        }

        private void ClientReader()
        {
            try
            {
                while (this.Connected)
                {
                    var buffer = new byte[255];
                    int recievedCount = 0;
                    try
                    {
                        recievedCount = this.TcpClient.GetStream().Read(buffer, 0, 1);
                    }
                    catch { break; }
                    if (0 == recievedCount)
                        continue;

                    try
                    {
                        recievedCount += this.TcpClient.GetStream().Read(buffer, 1, buffer[0] - 1);
                    }
                    catch { break; }
                    if (0 == recievedCount)
                        continue;

                    // Message received. Parse it.
                    var message = IPOCS.Protocol.Message.create(buffer.Take(recievedCount).ToArray());

                    // If unit has not yet sent a ConnectionRequest
                    if (string.IsNullOrWhiteSpace(this.Name))
                    {
                        var pkt = message.packets.FirstOrDefault((p) => p is Protocol.Packets.ConnectionRequest) as Protocol.Packets.ConnectionRequest;
                        if (pkt == null)
                        {
                            // First message must be a Connection Request
                            break;
                        }

                        this.Name = message.RXID_OBJECT;

                        if (OnConnectionRequest != null)
                        {
                            if (!(OnConnectionRequest?.Invoke(this, pkt)).Value)
                            {
                                break;
                            }
                        }

                        this.StaleTimer.Change(Timeout.Infinite, Timeout.Infinite);

                        var responseMsg = new Protocol.Message();
                        responseMsg.RXID_OBJECT = Name;
                        responseMsg.packets.Add(new IPOCS.Protocol.Packets.ConnectionResponse
                        {
                            RM_PROTOCOL_VERSION = pkt.RM_PROTOCOL_VERSION
                        });
                        this.Send(responseMsg);

                        OnConnect?.Invoke(this);
                    }
                    else
                        // And if not, hand it to listeners
                        OnMessage?.Invoke(message);
                }
            } catch (Exception e)
            {
                throw;
            }
            finally
            {
            }
            OnDisconnect?.Invoke(this);
        }

        public void Disconnect()
        {
            this.TcpClient.Close();
        }

        public void Send(IPOCS.Protocol.Message msg)
        {
            var buffer = msg.serialize().ToArray();
            try
            {
                this.TcpClient.GetStream().Write(buffer, 0, buffer.Length);
            } catch { }
        }
    }
}
