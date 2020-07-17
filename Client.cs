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
        private TcpClient tcpClient { get; }
        private Thread clientReadThread { get; }
        private Timer staleTimer { get; }
        public ushort UnitID { get; private set; } = 0;
        public string Name { get; set; } = string.Empty;
        public System.Net.EndPoint RemoteEndpoint
        {
            get
            {
                return tcpClient.Client.RemoteEndPoint;
            }
        }

        public bool Connected
        {
            get
            {
                return this.tcpClient?.Client != null ? this.tcpClient.Connected : false;
            }
        }

        public Client(TcpClient client)
        {
            this.tcpClient = client;
            this.clientReadThread = new Thread(new ThreadStart(this.clientReader));
            this.staleTimer = new Timer(new TimerCallback(StaleTimer), null, 1000, Timeout.Infinite);
            this.clientReadThread.Start();
        }

        public delegate void OnDisconnectDelegate(Client client);
        public event OnDisconnectDelegate OnDisconnect;

        public delegate bool? OnConnectionRequestDelegate(Client client, Protocol.Packets.ConnectionRequest request);
        public OnConnectionRequestDelegate OnConnectionRequest;

        public OnDisconnectDelegate OnConnect;

        public delegate void OnMessageDelegate(IPOCS.Protocol.Message msg);
        public event OnMessageDelegate OnMessage;
        //public ObjectTypes.Concentrator unit { get; private set; } = null;

        void StaleTimer(object state)
        {
            this.tcpClient.Close();
        }

        private void clientReader()
        {
            try
            {
                while (this.Connected)
                {
                    var buffer = new byte[255];
                    int recievedCount = 0;
                    try
                    {
                        recievedCount = this.tcpClient.GetStream().Read(buffer, 0, 1);
                    }
                    catch { break; }
                    if (0 == recievedCount)
                        continue;

                    try
                    {
                        recievedCount += this.tcpClient.GetStream().Read(buffer, 1, buffer[0] - 1);
                    }
                    catch { break; }
                    if (0 == recievedCount)
                        continue;

                    // Message received. Parse it.
                    var message = IPOCS.Protocol.Message.create(buffer.Take(recievedCount).ToArray());

                    // If unit has not yet sent a ConnectionRequest
                    if (this.UnitID == 0)
                    {
                        var pkt = message.packets.FirstOrDefault((p) => p is IPOCS.Protocol.Packets.ConnectionRequest) as IPOCS.Protocol.Packets.ConnectionRequest;
                        if (pkt == null)
                        {
                            // First message must be a Connection Request
                            break;
                        }

                        this.UnitID = ushort.Parse(message.RXID_OBJECT);

                        if (OnConnectionRequest != null)
                        {
                            if (!(OnConnectionRequest?.Invoke(this, pkt)).Value)
                            {
                                break;
                            }
                        }

                        this.staleTimer.Change(Timeout.Infinite, Timeout.Infinite);

                        var responseMsg = new IPOCS.Protocol.Message();
                        responseMsg.RXID_OBJECT = this.UnitID.ToString();
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
            this.tcpClient.Close();
        }

        public void Send(IPOCS.Protocol.Message msg)
        {
            var buffer = msg.serialize().ToArray();
            try
            {
                this.tcpClient.GetStream().Write(buffer, 0, buffer.Length);
            } catch { }
        }
    }
}
