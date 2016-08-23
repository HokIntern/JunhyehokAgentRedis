using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Junhaehok;
using static Junhaehok.HhhHelper;
using static Junhaehok.Packet;

namespace JunhyehokAgentRedis
{
    class ClientHandle
    {
        bool debug = true;
        bool isFront;
        Heartbeat heartbeat;
        int heartbeatMiss = 0;
        Socket so;
        int bytecount;
        ReceiveHandle recvHandler;
        string remoteHost;
        string remotePort;

        public Socket So { get { return so; } }

        public enum Heartbeat : int
        {
            Short = 2000, Medium = 20000, Long = 200000, Infinite
        }

        public ClientHandle(Socket s, bool isFront=false, Heartbeat heartbeat = Heartbeat.Infinite)
        {
            so = s;
            this.isFront = isFront;
            this.heartbeat = heartbeat;
            remoteHost = ((IPEndPoint)so.RemoteEndPoint).Address.ToString();
            remotePort = ((IPEndPoint)so.RemoteEndPoint).Port.ToString();
            Console.WriteLine("[Client] Connection established with {0}:{1}\n", remoteHost, remotePort);
        }

        public async void StartSequence()
        {
            Packet recvRequest;
            bool doSignout = true;
            while (true)
            {
                recvRequest = await SocketRecvAsync();

                if (ushort.MaxValue == recvRequest.header.code)
                    break;

                //=================Process Request/Get Response=================
                ReceiveHandle recvHandle = new ReceiveHandle(this, recvRequest);
                Packet respPacket = recvHandle.GetResponse();

                //=======================Send Response==========================
                if (ushort.MaxValue != respPacket.header.code) //if it isnt a NoResponsePacket
                {
                    byte[] respBytes = PacketToBytes(respPacket);
                    bool sendSuccess = SendBytes(respBytes);
                    if (!sendSuccess)
                    {
                        Console.WriteLine("Send failed.");
                        break;
                    }
                }

                //=======================Check Connection=======================
                if (!isConnected())
                {
                    Console.WriteLine("Connection lost with {0}:{1}", remoteHost, remotePort);
                    break;
                }
            }
            CloseConnection();
        }

        private async Task<Packet> SocketRecvAsync()
        {
            Packet disconnectedFlagPacket = new Packet(new Header(ushort.MaxValue, 0), null);
            //=========================Receive==============================
            Header recvHeader;
            Packet recvRequest;

            //========================get HEADER============================
            byte[] headerBytes = await GetBytesAsync(HEADER_SIZE, heartbeat);
            if (null == headerBytes)
                return disconnectedFlagPacket;
            recvHeader = BytesToHeader(headerBytes);
            recvRequest.header = recvHeader;

            //========================get DATA==============================
            byte[] dataBytes = await GetBytesAsync(recvHeader.size, heartbeat);
            if (null == dataBytes)
                return disconnectedFlagPacket;
            recvRequest.data = dataBytes;

            return recvRequest;
        }

        public void CloseConnection()
        {
            if (isFront)
            {
                UpdateMMF(false);
                ReceiveHandle.frontAlive = false;
            }
            //=================Close Connection/Exit Thread==================
            Console.WriteLine("Closing connection with {0}:{1}", remoteHost, remotePort);
            so.Shutdown(SocketShutdown.Both);
            so.Close();
            Console.WriteLine("Connection closed\n");
        }

        private async Task<byte[]> GetBytesAsync(int length, Heartbeat heartbeat)
        {
            byte[] bytes = new byte[length];
            if (length != 0) //this check has to exist. otherwise Receive timeouts for 60seconds while waiting for nothing
            {
                try
                {
                    if (heartbeat != Heartbeat.Infinite)
                    {
                        so.ReceiveTimeout = (int)heartbeat;
                        bytecount = await Task.Run(() => so.Receive(bytes));
                    }
                    else
                        bytecount = await Task.Run(() => so.Receive(bytes));

                    //assumes that the line above(so.Receive) will throw exception 
                    //if times out, so the line below(reset hearbeatMiss) will not be reached
                    //if an exception is thrown.
                    heartbeatMiss = 0;
                }
                catch (Exception e)
                {
                    if (!isConnected())
                    {
                        Console.WriteLine("\n" + e.Message);
                        return null;
                    }
                    else
                    {
                        if (bytes.Length != 0)
                        {
                            heartbeatMiss++;
                            if (heartbeatMiss == 3)
                            {
                                Console.WriteLine("[HEARBEAT MISSED] {0}:{1}", remoteHost, remotePort);
                                UpdateMMF(false);
                                ReceiveHandle.frontAlive = false;
                                return null;
                            }

                            //puts -1 bytes into 1st and 2nd bytes (CODE)
                            byte[] noRespBytes = BitConverter.GetBytes((ushort)ushort.MaxValue - 1);
                            bytes[FieldIndex.CODE] = noRespBytes[0];
                            bytes[FieldIndex.CODE + 1] = noRespBytes[1];
                        }
                    }
                }
            }
            return bytes;
        }

        private bool SendBytes(byte[] bytes)
        {
            try
            {
                bytecount = so.Send(bytes);
            }
            catch (Exception e)
            {
                Console.WriteLine("\n" + e.Message);
                return false;
            }
            return true;
        }

        private bool isConnected()
        {
            try
            {
                return !(so.Poll(1, SelectMode.SelectRead) && so.Available == 0);
            }
            catch (SocketException) { return false; }
            catch (Exception) { return false; }
        }
        private void UpdateMMF(bool alive = true)
        {
            AAServerInfoResponse aaServerInfoResp;
            aaServerInfoResp.alive = alive;
            aaServerInfoResp.userCount = 0;
            aaServerInfoResp.roomCount = 0;
            byte[] aaServerInfoRespBytes = Serializer.StructureToByte(aaServerInfoResp);

            Console.WriteLine("[MEMORYMAPPED FILE] Writing to MMF: ({0})...", ReceiveHandle.mmfName);

            Mutex mutex = Mutex.OpenExisting("MMF_IPC" + ReceiveHandle.mmfName);
            mutex.WaitOne();

            // Create Accessor to MMF
            using (var accessor = ReceiveHandle.mmf.CreateViewAccessor(0, aaServerInfoRespBytes.Length))
            {
                // Write to MMF
                accessor.WriteArray<byte>(0, aaServerInfoRespBytes, 0, aaServerInfoRespBytes.Length);
            }
            mutex.ReleaseMutex();
        }
    }
}
