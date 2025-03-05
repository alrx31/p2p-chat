using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using p2p_chat;

public class Program
{
    const int UDP_PORT = 9000;
    const int TCP_PORT = 9001;
    
    static string localName;
    static string localIP;
    static UdpClient udpClient;

    static ConcurrentDictionary<IPEndPoint, TcpClient> tcpClients = new ConcurrentDictionary<IPEndPoint, TcpClient>();

    public static async Task Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: dotnet run -- <name> <ip>");
            return;
        }

        var name = args[0];
        localName = name;
        var ip = args[1];
        localIP = ip;

        AppDomain.CurrentDomain.ProcessExit += (s, e) =>
        {
            foreach (var tcpClient in tcpClients.Values)
            {
                tcpClient.Close();
            }
            Console.WriteLine("Application is closing.");
        };
        
        Console.WriteLine($"Starting {name} on {ip}");

        udpClient = new UdpClient(new IPEndPoint(IPAddress.Parse(localIP), UDP_PORT));
        await Task.WhenAll(
            SendUdpBroadcasts(udpClient),
            StartUdpListener(udpClient),
            StartTcpListener(localIP),
            HandleUserInput()
        );
    }

    #region UDP
    private static async Task SendUdpBroadcasts(UdpClient udpClient)
    {
        await Task.Delay(500);
        var message = CreateMessage(MessageTypes.Message, localName);

        foreach (var targetIp in GetLoopbackIPAddresses())
        {
            if (targetIp.Equals(localIP)) continue;
            _ = Task.Run(()=>SendUdpRequestAsync(udpClient, targetIp, message));
        }
    }

    private static async Task SendUdpRequestAsync(UdpClient udpClient, string ip, byte[] message)
    {
        var endpoint = new IPEndPoint(IPAddress.Parse(ip), UDP_PORT);
        try
        {
            await udpClient.SendAsync(message, message.Length, endpoint);
            //Console.WriteLine($"Sent UDP message to {ip}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending to {ip}: {ex.Message}");
        }
    }

    private static async Task StartUdpListener(UdpClient ip)
    {
        var listener = ip;
        Console.WriteLine($"Listening for UDP connections on {localIP}:{UDP_PORT}");

        try
        {
            while (true)
            {
                var result = await listener.ReceiveAsync();
                var endpoint = result.RemoteEndPoint;
                var message = Encoding.UTF8.GetString(result.Buffer);

                Console.WriteLine($"Received UDP message from {endpoint}: {message}");

                await CreateTcpConnection(endpoint);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in UDP listener for {ip}: {ex.Message}");
        }
    }

    #endregion

    #region TCP
    public static async Task StartTcpListener(string ip)
    {
        var client = new TcpListener(IPAddress.Parse(ip), TCP_PORT);
        client.Start();
        Console.WriteLine($"start listening on {ip}:{TCP_PORT}");

        try
        {
            
                var tcpClient = await client.AcceptTcpClientAsync();
                var endPoint = tcpClient.Client.RemoteEndPoint as IPEndPoint;
                var remoteIp = endPoint.Address;
                
                Console.WriteLine($"Connected to {endPoint.Address}:{endPoint.Port}");

                if (!tcpClients.ContainsKey(endPoint))
                {
                    tcpClients.TryAdd(endPoint, tcpClient);
                    _ = Task.Run(() => ReceiveTcpMessage(tcpClient, endPoint));
                }
                else
                {
                    Console.WriteLine($"Already connected to {remoteIp}");
                    tcpClient.Close();
                }
            
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    public static async Task ReceiveTcpMessage(TcpClient tcpClient, IPEndPoint endPoint)
    {
        var stream = tcpClient.GetStream();
        var buffer = new byte[1024];

        try
        {
            while (true)
            {
                var byteCount = await stream.ReadAsync(buffer, 0, buffer.Length);
                if (byteCount == 0) break;

                var messageType = (MessageTypes)buffer[0];
                var content = Encoding.UTF8.GetString(buffer, 3, byteCount-3);

                switch (messageType)
                {
                    case MessageTypes.UserEntered:
                        HandleConnectMessage(content, endPoint);
                        break;
                    case MessageTypes.Message:
                        var originalColor = Console.ForegroundColor;
                        Console.ForegroundColor = UserColorManager.GetColorForUser(content);
                        Console.WriteLine(content);
                        Console.ForegroundColor = originalColor;

                        foreach (var client in tcpClients
                                     .Where(kv => !kv.Key.Equals(endPoint)))
                        {
                            await SendTcpMessage(client.Value, buffer[0..byteCount]);
                        }
                        break;
                    case MessageTypes.UserLeft:
                        Console.WriteLine(content);
                        tcpClients.TryRemove(endPoint, out _);
                        tcpClient.Close();
                        //_ = Task.Run(() => SendUdpBroadcasts(udpClient));
                        return;
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error while receiving TCP message from {endPoint}: {e.Message}");
        }
        finally
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Connection with {endPoint} closed");
            tcpClients.TryRemove(endPoint, out _);
            tcpClient.Close();
        }
    }

    public static void HandleConnectMessage(string content, IPEndPoint endPoint)
    {
        var parts = content.Split(':');
        if (parts.Length != 2) return;
        
        var (name, ip) = (parts[0], parts[1]);
        
        var newEp = new IPEndPoint(IPAddress.Parse(ip),TCP_PORT);
        if (!tcpClients.ContainsKey(newEp))
        {
            _ = Task.Run(() => CreateTcpConnection(newEp));
        }
    }

    public static async Task CreateTcpConnection(IPEndPoint endPoint)
    {
        var targetEndPoint = new IPEndPoint(endPoint.Address, TCP_PORT);
            
        var remoteIp = endPoint.Address;

        if (targetEndPoint.Address.ToString() == localIP) return;
        
        if (tcpClients.ContainsKey(targetEndPoint))
        {
            Console.WriteLine($"Already connected to {endPoint}");
            return;
        }

        var tcpClient = new TcpClient();
        try
        {
            Console.WriteLine(targetEndPoint.ToString());
            await tcpClient.ConnectAsync(targetEndPoint);
            Console.WriteLine($"Connected to {remoteIp}:{TCP_PORT}");

            tcpClients.TryAdd(targetEndPoint, tcpClient);

            _ = Task.Run(() => ReceiveTcpMessage(tcpClient, targetEndPoint));

            var message = CreateMessage(MessageTypes.Message, $"{DateTime.Now:HH:mm:ss} [{localName}] connected");
            _ = Task.Run(()=>SendTcpMessage(tcpClient, message));
        }
        catch (Exception e)
        {
            Console.WriteLine($"Failed to connect to {targetEndPoint.Address}:{targetEndPoint.Port} {e.Message}");
            tcpClient.Close();
        }
    }

    public static async Task SendTcpMessage(TcpClient tcpClient, byte[] message)
    {
        try
        {
            var stream = tcpClient.GetStream();
            await stream.WriteAsync(message, 0, message.Length);
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error while sending TCP message: {e.Message}");
        }
    }

    #endregion

    #region UserInput

    public static async Task HandleUserInput()
    {
        while (true)
        {
            var message =await Console.In.ReadLineAsync();
            if (string.IsNullOrEmpty(message)) continue;

            if (message == "/exit")
            {
                var disconnectMessage = CreateMessage(MessageTypes.UserLeft, $"{DateTime.Now:HH:mm:ss} [{localName}] disconnected");
                foreach (var tcpClient in tcpClients.Values.ToArray())
                {
                    await SendTcpMessage(tcpClient, disconnectMessage);
                }

                Console.WriteLine("Exiting...");
                Environment.Exit(0);
            }
            
            var formattedMessage = $"{DateTime.Now:HH:mm:ss} [{localName}] {message}";
            var messageBytes = CreateMessage(MessageTypes.Message, formattedMessage);

            foreach (var tcpClient in tcpClients.Values.ToArray())
            {
                await SendTcpMessage(tcpClient, messageBytes);
            }
        }
    }

    #endregion

    #region Additional

    private static string[] GetLoopbackIPAddresses()
    {
        string[] ips = new string[255];
        for (int i = 1; i <= 255; i++)
        {
            ips[i - 1] = $"127.0.0.{i}";
        }

        return ips;
    }

    private static byte[] CreateMessage(MessageTypes type, string name)
    {
        var messageType = type;
        var message = Encoding.UTF8.GetBytes(name);
        byte[] result = new byte[3 + message.Length];

        result[0] = (byte)messageType;
        result[1] = (byte)((message.Length >> 8) & 0xFF);
        result[2] = (byte)(message.Length & 0xFF);

        Array.Copy(message, 0, result, 3, message.Length);

        return result;
    }

    #endregion
}