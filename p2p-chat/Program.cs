using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using p2p_chat;

public class Program
{
    #region VARIABLES

    const int UDP_PORT = 9000;
    const int TCP_PORT = 9001;
    const int TCP_S_PORT = 9002;
    
    static string localName;
    static string localIP;
    static UdpClient udpClient;

    static ConcurrentBag<string> history = new ConcurrentBag<string>();
    static ConcurrentDictionary<IPEndPoint, TcpClient> tcpClients = new ConcurrentDictionary<IPEndPoint, TcpClient>();


    #endregion
    
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
        await Task.Delay(100);
        var message = CreateMessage(MessageTypes.UserEntered, localName);

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

                //Console.WriteLine($"Received UDP message from {endpoint}: {message}");
                _ = Task.Run(()=>CreateTcpConnection(endpoint));
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
        Console.WriteLine($"Listening on {ip}:{TCP_PORT}");

        try
        {
            while (true) 
            {
                var tcpClient = await client.AcceptTcpClientAsync();
                var endPoint = tcpClient.Client.RemoteEndPoint as IPEndPoint;
                var remoteIp = endPoint.Address;

                //Console.WriteLine($"(L)Connected to {endPoint.Address}:{endPoint.Port}");
                //PrintDictionary();

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
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error in TCP listener: {e.Message}");
        }
    }


    public static async Task ReceiveTcpMessage(TcpClient tcpClient, IPEndPoint endPoint)
{
    try
    {
        while (true)
        {
            var stream = tcpClient.GetStream();
            var buffer = new byte[1024];
            
            var byteCount = await stream.ReadAsync(buffer, 0, buffer.Length);
            if (byteCount == 0) break;  

            if (byteCount < 3)
            {
                Console.WriteLine("Received incomplete message (less than 3 bytes), skipping...");
                continue;
            }

            var messageType = (MessageTypes)buffer[0];

            int messageLength = (buffer[1] << 8) | buffer[2];

            if (messageLength != (byteCount - 3))
            {
                Console.WriteLine($"Message length mismatch. Expected {messageLength} bytes, but received {byteCount - 3} bytes. Skipping...");
                continue;
            }

            var content = Encoding.UTF8.GetString(buffer, 3, messageLength);

            //Console.WriteLine("RC");
            //PrintDictionary();

            switch (messageType)
            {
                case MessageTypes.History:
                    var messages = content.Split('\n');
                    foreach (var message in messages)
                    {
                        var originalColor1 = Console.ForegroundColor;
                        Console.ForegroundColor = UserColorManager.GetColorForUser(message);
                        Console.WriteLine(message);
                        Console.ForegroundColor = originalColor1;
                    }
                    break;

                case MessageTypes.Message:
                    var originalColor = Console.ForegroundColor;
                    Console.ForegroundColor = UserColorManager.GetColorForUser(content);
                    Console.WriteLine(content);
                    Console.ForegroundColor = originalColor;

                    history.Add(content);
                    break;

                case MessageTypes.UserLeft:
                    Console.WriteLine(content);
                    tcpClients.TryRemove(endPoint, out _);
                    tcpClient.Close();
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
        //Console.WriteLine(tcpClient.Client.AddressFamily);
        try
        {
            //Console.WriteLine(targetEndPoint.ToString());
            await tcpClient.ConnectAsync(targetEndPoint);
            //Console.WriteLine($"(C)Connected to {remoteIp}:{TCP_PORT}");

            tcpClients.TryAdd(targetEndPoint, tcpClient);
            //Console.WriteLine("CTCP");
            //PrintDictionary();
            
            _ = Task.Run(()=>SendChatHistory(tcpClient));
            _ = Task.Run(() => ReceiveTcpMessage(tcpClient, targetEndPoint));

            var message = CreateMessage(MessageTypes.Message, $"\n{DateTime.Now:HH:mm:ss} [{localName}] connected");
            _ = Task.Run(()=>SendTcpMessage(tcpClient, message));
        }
        catch (Exception e)
        {
            Console.WriteLine($"Failed to connect to {targetEndPoint.Address}:{targetEndPoint.Port} {e.Message}");
            tcpClients.TryRemove(targetEndPoint, out _);
            tcpClient.Close();
        }
    } 
    public static async Task SendChatHistory(TcpClient tcpClient)
    {
        var messages = new List<byte[]>();

        foreach (var hItem in history)
        {
            messages.Add(CreateMessage(MessageTypes.History,"\n" + hItem));
        }

        foreach (var message in messages)
        {
            _ = Task.Run(()=>SendTcpMessage(tcpClient, message));
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

    #region USERINPUT

    public static async Task HandleUserInput()
    {
        while (true)
        {
            var message =await Console.In.ReadLineAsync();
            if (string.IsNullOrEmpty(message)) continue;
            
            //Console.WriteLine("UI");
            //PrintDictionary();
            
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
            
            history.Add(formattedMessage);
            foreach (var tcpClient in tcpClients.Values.ToArray())
            {
                await Task.Delay(50);
                _ = Task.Run(()=>SendTcpMessage(tcpClient, messageBytes));
            }
        }
    }

    #endregion

    #region ADDITIONAL

    private static string[] GetLoopbackIPAddresses()
    {
        string[] ips = new string[255];
        for (int i = 1; i <= 20; i++)
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

    static void PrintDictionary()
    {
        foreach (var tcpClient in tcpClients)
        {
            Console.WriteLine(tcpClient.Key);
        }
    }
    
    #endregion
}