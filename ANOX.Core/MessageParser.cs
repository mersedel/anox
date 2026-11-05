using System;
using System.Text;

namespace ANOX.Core;

public static class MessageParser
{
    public static string SerializeMessage(Message message)
    {
        StringBuilder stringBuilder = new StringBuilder();
        
        string parsedMessageText = Convert.ToBase64String(Encoding.UTF8.GetBytes(message.GetMessageText()));

        stringBuilder.AppendJoin('|',
            message.GetMessageType().ToString(),
            message.GetSenderName(),
            message.GetReceiverName(),
            parsedMessageText);
        
        return stringBuilder.ToString();
    }

    /// <summary>Splits a ServerError message into its error code and human-readable text.</summary>
    public static (MessageErrorType Type, string Text) ParseError(Message message)
    {
        string[] parts = message.GetMessageText().Split('|', 2);

        if (parts.Length == 2 && int.TryParse(parts[0], out int code) && Enum.IsDefined(typeof(MessageErrorType), code))
        {
            return ((MessageErrorType)code, parts[1]);
        }

        return (MessageErrorType.Unknown, message.GetMessageText());
    }

    public static Message DeserializeClientMessage(string serializedMessage)
    {
        string[] parts = serializedMessage.Split('|', 4);

        return new Message(
            Enum.Parse<MessageType>(parts[0]),
            parts[1],
            parts[2],
            Encoding.UTF8.GetString(Convert.FromBase64String(parts[3]))
        );
    }
}