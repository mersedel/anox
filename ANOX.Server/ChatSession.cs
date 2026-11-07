using ANOX.Core;

namespace ANOX.Server;

public class ChatSession
{
    private Client _clientA, _clientB;

    public Client ClientA => _clientA;

    public Client ClientB => _clientB;

    public ChatSession(Client clientA, Client clientB)
    {
        _clientA = clientA;
        _clientB = clientB;
    }

    public Client GetPeer(Client client)
    {
        return ReferenceEquals(client, _clientA) ? _clientB : _clientA;
    }

    /// <summary>
    /// Builds a new message instead of forwarding the received one: its sender/receiver fields
    /// come from the client and could be forged (e.g. "SERVER" or another user's name).
    /// </summary>
    public void Route(Client sender, string text)
    {
        if (!ReferenceEquals(sender, _clientA) && !ReferenceEquals(sender, _clientB)) return;

        Client peer = GetPeer(sender);
        peer.SendMessage(MessageFactory.CreateUserMessage(sender.Username, peer.Username, text));
    }
}