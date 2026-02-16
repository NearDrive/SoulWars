namespace Game.Server;

public interface IServerEndpoint
{
    string EndpointKey { get; }

    bool IsClosed { get; }

    bool TryDequeueToServer(out byte[] msg);

    void EnqueueToClient(byte[] msg);

    void Close();
}

public interface IClientEndpoint
{
    void EnqueueToServer(byte[] msg);

    bool TryDequeueFromServer(out byte[] msg);

    void Close();
}
