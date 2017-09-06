using BlowFishCS;
using ENet;
using Frenzy.Packets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Frenzy
{
    class FrenzyClient
    {
        private Host _enetHost;
        private Peer _peer;
        private BlowFish _blowfish;
        private bool _connected = false;
        private UInt32 _myNetId = 0;
        private UInt64 _mySummonerId;

        private delegate void OnPacket(byte[] packet, Channel channel);
        private Dictionary<PacketCommand, OnPacket> _callbacks;

        public FrenzyClient(string[] args)
        {
            Console.WriteLine("===============================================");
            Console.WriteLine("Frenzy - Clientless League Of Legends Bot v0.1");
            Console.WriteLine("===============================================");
            _callbacks = new Dictionary<PacketCommand, OnPacket>()
            {
                {PacketCommand.KeyCheck, OnKeyCheck},
            };
            var str = args[3].Split(' ');
            Start(str[0], str[1], str[2], str[3]);
        }

        public async void Start(string ip, string port, string encryptionKey, string summonerId)
        {
            _mySummonerId = Convert.ToUInt64(summonerId);
            var blowfishKey = Convert.FromBase64String(encryptionKey);
            _blowfish = new BlowFish(blowfishKey);

            // Initialize ENetCS
            Library.Initialize();

            _enetHost = new Host();
            _enetHost.Create(null, 1);

            var address = new Address();
            address.SetHost(ip);
            address.Port = Convert.ToUInt16(port);

            Console.WriteLine("Trying to connect to server... (" + ip + ":" + port + ")");

            _peer = _enetHost.Connect(address, 8);

            PeerState previous = PeerState.Disconnected;

            while (_enetHost.Service(0) >= 0)
            {
                if (previous != _peer.State)
                {
                    if(_peer.State == PeerState.Connecting)
                    {
                        //Send(, Channel.Handshake);
                    }
                    Console.WriteLine(_peer.State);
                }

                Event enetEvent;
                try
                {
                    while (_enetHost.CheckEvents(out enetEvent) > 0)
                    {
                        switch (enetEvent.Type)
                        {
                            case EventType.Connect:
                                OnConnect(blowfishKey);
                                break;
                            case EventType.Receive:
                                OnRecieve(enetEvent);
                                break;
                            case EventType.Disconnect:
                                Console.WriteLine("ENet Event: Disconnected");
                                break;
                        }
                    }
                }
                catch (InvalidOperationException)
                {
                    break;
                }
            }
        }

        private void OnConnect(byte[] blowfishKey)
        {
            Console.WriteLine("[{0}] Connected to server.", "Maufeat");
            var keyCheck = KeyCheck.Create(_mySummonerId, _blowfish.Encrypt_ECB(_mySummonerId), blowfishKey);
            var keyBytes = Deserialize<KeyCheck>(keyCheck);
            Send(keyBytes, Channel.Handshake);
        }

        private void OnRecieve(Event enetEvent)
        {
            var packet = enetEvent.Packet.GetBytes();
            var channel = (Channel)enetEvent.ChannelID;

            if (packet.Length >= 8 && channel != Channel.Handshake)
                packet = _blowfish.Decrypt_ECB(packet);

            if (packet.Length < 1)
                return;

            var cmd = (PacketCommand)BitConverter.ToUInt16(packet, 0);
            Console.WriteLine("Got Cmd: {0}", cmd);
            try
            {
                if (_callbacks.ContainsKey(cmd))
                    _callbacks[cmd](packet, channel);
            }
            catch { }
            // done!
            enetEvent.Packet.Dispose();
        }

        private void OnKeyCheck(byte[] packet, Channel channel)
        {
            if (channel != Channel.Handshake)
                return;

            var keyCheck = Serialize<KeyCheck>(packet);
            var checkBytes = BitConverter.GetBytes(keyCheck.checkId);

            Console.WriteLine("UserId for client: ({0}). Player Id: {1}", Convert.ToUInt64(_blowfish.Decrypt_ECB(checkBytes)), keyCheck.playerNo);
        }

        private void Send(byte[] packet, Channel channel = Channel.C2S)
        {
            if (packet.Length >= 8 && channel != Channel.Handshake)
                packet = _blowfish.Encrypt_ECB(packet);

            _peer.Send((byte)channel, packet);
        }
        
        private T Serialize<T>(byte[] packet) where T : struct
        {
            var size = Marshal.SizeOf(typeof(T));
            var ptr = Marshal.AllocHGlobal(size);

            Marshal.Copy(packet, 0, ptr, size);
            var data = (T)Marshal.PtrToStructure(ptr, typeof(T));
            Marshal.FreeHGlobal(ptr);

            return data;
        }

        private byte[] Deserialize<T>(T packet) where T : struct
        {
            var size = Marshal.SizeOf(typeof(T));
            var data = new byte[size];

            var ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(packet, ptr, true);
            Marshal.Copy(ptr, data, 0, size);
            Marshal.FreeHGlobal(ptr);

            return data;
        }
    }

    public enum Channel : byte
    {
        Handshake,
        C2S,
        Gameplay,
        S2C,
        LowPriority,
        Communication,
        LoadingScreen = 6
    }

    public enum PacketCommand : ushort
    {
        KeyCheck = 0x00,

        C2S_InGame = 0x08,
        S2C_EndSpawn = 0x11,
        C2S_QueryStatusReq = 0x14,
        S2C_SkillUp = 0x15,
        C2S_Ping_Load_Info = 0x16,

        Batch = 0xFF
    }
}
