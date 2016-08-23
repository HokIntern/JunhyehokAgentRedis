using System;
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Junhaehok;
using System.Threading;
using System.IO;
using System.Net.Sockets;
using System.Net;
using System.Diagnostics;

namespace JunhyehokAgentRedis
{
    class Program
    {
        static void Main(string[] args)
        {
            string host = null;     //Default
            string clientPort = "40000";  //Default
            string mmfName = "JunhyehokMmf"; //Default
            string connection_type = "tcp";
            TcpServer echoc;

            //=========================GET ARGS=================================
            if (args.Length == 0)
            {
                Console.WriteLine("Format: JunhyehokAgent -mmf [MMF name] -ct [Connection Type]");
                Environment.Exit(0);
            }

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--help":
                        Console.WriteLine("Format: JunhyehokAgent -mmf [MMF name] -ct [Connection Type]");
                        Environment.Exit(0);
                        break;
                    case "-mmf":
                        mmfName = args[++i];
                        break;
                    case "-ct":
                        connection_type = args[++i];
                        break;
                    default:
                        Console.Error.WriteLine("ERROR: incorrect inputs \nFormat: JunhyehokAgent -mmf [MMF name] -ct [Connection Type]");
                        Environment.Exit(0);
                        break;
                }
            }

            //======================SOCKET BIND/LISTEN==========================
            /* if only given port, host is ANY */
            echoc = new TcpServer(host, clientPort);

            //=======================REDIS CONNECT================================
            Console.WriteLine("Connecting to Redis...");
            RedisHandlerForFE redis = new RedisHandlerForFE();

            //======================INITIALIZE==================================
            Console.WriteLine("Initializing lobby and rooms...");
            ReceiveHandle recvHandle = new ReceiveHandle(redis, mmfName, connection_type);

            //=====================START FRONTEND SERVER========================
            Console.WriteLine("Starting Frontend Server...");
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
            else
            {
                Console.WriteLine("ERROR: Wrong Connection type. Exiting...");
                Environment.Exit(0);
            }
            //=====================FRONTEND ACCEPT==============================
            Socket frontSo = null;
            ClientHandle front = null;

            //===================CLIENT SOCKET ACCEPT===========================
            Console.WriteLine("Accepting clients...");

            while (true)
            {
                //always accept front first, and then accept admins (discard sockets before front is up)
                Socket s = echoc.so.Accept();
                if (((IPEndPoint)s.RemoteEndPoint).Address.ToString() == "127.0.0.1")
                {
                    if (frontSo != null) frontSo.Close(); //close old front socket (when server dead due to missed heatbeat or Stop signal)

                    frontSo = s;
                    front = new ClientHandle(frontSo, true, ClientHandle.Heartbeat.Short);
                    ReceiveHandle.front = frontSo;
                    ReceiveHandle.frontAlive = true;
                    front.StartSequence();
                }
                else if (null != front)
                {
                    ClientHandle client = new ClientHandle(s);
                    ReceiveHandle.admin = s;
                    client.StartSequence();
                }
                else
                    continue;
            }
        }
        public static Socket Connect(string info)
        {
            string host;
            int port;
            string[] hostport = info.Split(':');
            host = hostport[0];
            if (!int.TryParse(hostport[1], out port))
            {
                Console.Error.WriteLine("port must be int. given: {0}", hostport[1]);
                Environment.Exit(0);
            }

            Socket so = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            IPAddress ipAddress = IPAddress.Parse(host);

            Console.WriteLine("Establishing connection to {0}:{1} ...", host, port);

            try
            {
                so.Connect(ipAddress, port);
                Console.WriteLine("Connection established.\n");
            }
            catch (Exception)
            {
                Console.WriteLine("Peer is not alive.");
            }

            return so;
        }
    }
}
