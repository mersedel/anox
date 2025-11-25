using System;

namespace ANOX.Core;

public class Message
{
    private readonly MessageType _type;
    
    private readonly string _sender;
    private readonly string _receiver;
    private readonly string _text;
    private readonly string _timestamp;

    public Message(MessageType messageType, string sender, string receiver, string text, string timestamp)
    {
        _type = messageType;
        
        _sender = sender;
        _receiver = receiver;
        _text = text;
        _timestamp = timestamp;
    }

    public MessageType GetMessageType() => _type;
    
    public string GetSenderName() => _sender;
    public string GetReceiverName() => _receiver;
    public string GetMessageText() => _text;
    public string GetMessageTime() => _timestamp;
    
    public override string ToString()
    {
        return $"[{_timestamp}] [{_type}] {_sender} -> {_receiver}: {_text}";
    }

}
