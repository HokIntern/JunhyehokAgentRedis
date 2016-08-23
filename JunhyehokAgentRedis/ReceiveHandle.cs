using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Junhaehok;
using static Junhaehok.Packet;
using static Junhaehok.HhhHelper;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading;
using System.IO;
using System.Diagnostics;

namespace JunhyehokAgentRedis
{
    class ReceiveHandle
    {
        ClientHandle client;
        Packet recvPacket;

        public static Socket admin;
        public static Socket front;
        public static bool frontAlive;
        public static RedisHandlerForFE redis;
        public static string mmfName;
        public static MemoryMappedFile mmf;
        static string connection_type;
        readonly Header NoResponseHeader = new Header(ushort.MaxValue, 0);
        readonly Packet NoResponsePacket = new Packet(new Header(ushort.MaxValue, 0), null);

        public ReceiveHandle(RedisHandlerForFE redisHandler, string mmfNombre, string conn_type)
        {
            redis = redisHandler;
            mmfName = mmfNombre;
            connection_type = conn_type;
            //Initialize MMF
            mmf = MemoryMappedFile.CreateOrOpen(mmfName, Marshal.SizeOf(typeof(AAServerInfoResponse)));
            //Initialize Lock
            bool mutexCreated;
            Mutex mutex = new Mutex(true, "MMF_IPC" + mmfName, out mutexCreated);
            mutex.ReleaseMutex();
        }

        public ReceiveHandle(ClientHandle client, Packet recvPacket)
        {
            this.client = client;
            this.recvPacket = recvPacket;
        }
        //==========================================SERVER_START 1200===========================================
        //==========================================SERVER_START 1200===========================================
        //==========================================SERVER_START 1200===========================================
        public Packet ResponseServerStart(Packet recvPacket)
        {
            if (!frontAlive)
            {
                if (connection_type == "web")
                {
                    string filePath = Path.Combine(Environment.CurrentDirectory, "JunhyehokWebServerRedis.exe");
                    string arg = "-cp 38080 -mmf " + mmfName;
                    Process.Start(filePath, arg);
                }
                else if (connection_type == "tcp")
                {
                    string filePath = Path.Combine(Environment.CurrentDirectory, "JunhyehokServerRedis.exe");
                    string arg = "-cp 30000 -mmf " + mmfName;
                    Process.Start(filePath, arg);
                }
                frontAlive = true;
            }
            return NoResponsePacket;
        }
        //=========================================SERVER_RESTART 1240==========================================
        //=========================================SERVER_RESTART 1240==========================================
        //=========================================SERVER_RESTART 1240==========================================
        public Packet ResponseServerRestart(Packet recvPacket)
        {
            Packet throwaway;
            if (frontAlive)
            {
                throwaway = ResponseServerStop(recvPacket);
                frontAlive = false;
            }
            Thread.Sleep(500);
            throwaway = ResponseServerStart(recvPacket); //turns frontAlive to true in function
            return NoResponsePacket;
        }
        //==========================================SERVER_STOP 1270============================================
        //==========================================SERVER_STOP 1270============================================
        //==========================================SERVER_STOP 1270============================================
        public Packet ResponseServerStop(Packet recvPacket)
        {
            if (frontAlive)
            {
                Packet kill = new Packet(new Header(Code.SERVER_STOP, 0), null);
                front.SendBytes(kill);
                frontAlive = false;
            }
            return NoResponsePacket;
        }
        //==========================================SERVER_INFO 1300============================================
        //==========================================SERVER_INFO 1300============================================
        //==========================================SERVER_INFO 1300============================================
        public Packet ResponseServerInfo(Packet recvPacket)
        {
            AAServerInfoResponse aaServerInfoResp = new AAServerInfoResponse();
            byte[] aaServerInfoRespBytes = new byte[Marshal.SizeOf(aaServerInfoResp)];

            // Create named MMF
            Console.WriteLine("[MEMORYMAPPED FILE] Reading from MMF: ({0})...", mmfName);
            // Create accessor to MMF
            using (var accessor = mmf.CreateViewAccessor(0, Marshal.SizeOf(aaServerInfoResp)))
            {
                // Wait for the Lock
                Mutex mutex = Mutex.OpenExisting("MMF_IPC" + mmfName);
                mutex.WaitOne();

                // Read from MMF
                accessor.ReadArray<byte>(0, aaServerInfoRespBytes, 0, aaServerInfoRespBytes.Length);
                mutex.ReleaseMutex();
            }

            return new Packet(new Header(Code.SERVER_INFO_SUCCESS, (ushort)aaServerInfoRespBytes.Length), aaServerInfoRespBytes);
            //aaServerInfoResp = (AAServerInfoResponse)Serializer.ByteToStructure(aaServerInfoRespBytes, typeof(AAServerInfoResponse));
        }
        //============================================RANKINGS 1400=============================================
        //============================================RANKINGS 1400=============================================
        //============================================RANKINGS 1400=============================================
        public Packet ResponseRankings(Packet recvPacket)
        {
            byte[] rankingBytes = redis.UserRanking(10);
            Header header = new Header(Code.RANKINGS_SUCCESS, (ushort)rankingBytes.Length);
            Packet response = new Packet(header, rankingBytes);
            return response;
        }

        //=============================================SWITCH CASE============================================
        //=============================================SWITCH CASE============================================
        //=============================================SWITCH CASE============================================
        public Packet GetResponse()
        {
            Packet responsePacket = new Packet();
            string remoteHost = ((IPEndPoint)client.So.RemoteEndPoint).Address.ToString();
            string remotePort = ((IPEndPoint)client.So.RemoteEndPoint).Port.ToString();
            bool debug = true;

            if (debug && recvPacket.header.code != Code.HEARTBEAT && recvPacket.header.code != Code.HEARTBEAT_SUCCESS && recvPacket.header.code != ushort.MaxValue - 1)
            {
                Console.WriteLine("\n[Client] {0}:{1}", remoteHost, remotePort);
                Console.WriteLine("==RECEIVED: \n" + PacketDebug(recvPacket));
            }

            switch (recvPacket.header.code)
            {
                //------------No action from client----------
                case ushort.MaxValue - 1:
                    responsePacket = new Packet(new Header(Code.HEARTBEAT, 0), null);
                    break;
                case Code.HEARTBEAT_SUCCESS:
                    responsePacket = NoResponsePacket;
                    break;

                //------------SERVER---------
                case Code.SERVER_INFO:
                    responsePacket = ResponseServerInfo(recvPacket);
                    break;
                case Code.SERVER_START:
                    responsePacket = ResponseServerStart(recvPacket);
                    break;
                case Code.SERVER_RESTART:
                    responsePacket = ResponseServerRestart(recvPacket);
                    break;
                case Code.SERVER_STOP:
                    responsePacket = ResponseServerStop(recvPacket);
                    break;
                //-----------RANKINGS--------
                case Code.RANKINGS:
                    responsePacket = ResponseRankings(recvPacket);
                    break;

                default:
                    if (debug)
                        Console.WriteLine("Unknown code: {0}\n", recvPacket.header.code);
                    break;
            }

            //===============Build Response/Set Surrogate/Return================
            if (debug && responsePacket.header.code != ushort.MaxValue && responsePacket.header.code != Code.HEARTBEAT && responsePacket.header.code != Code.HEARTBEAT_SUCCESS)
            {
                Console.WriteLine("\n[Client] {0}:{1}", remoteHost, remotePort);
                Console.WriteLine("==SEND: \n" + PacketDebug(responsePacket));
            }

            return responsePacket;
        }
        private Packet ForwardPacket(Packet recvPacket)
        {
            admin.SendBytes(recvPacket);
            return NoResponsePacket;
        }
    }
}
