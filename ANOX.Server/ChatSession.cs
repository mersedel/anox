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

    public void Route(Message message, string senderUsername)
    {
        if (senderUsername == _clientA.Username)
        {
            _clientB.SendMessage(message);
        }

        if (senderUsername == _clientB.Username)
        {
            _clientA.SendMessage(message);
        }
    }
}