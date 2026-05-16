using System;
using System.Collections.Generic;

namespace ANOX.Core;

public static class MessageFactory
{
    private static readonly Dictionary<MessageErrorType, string> _errorDictionary =
        new Dictionary<MessageErrorType, string>
        {
            { MessageErrorType.Unknown,                "Unknown error." },
            { MessageErrorType.InvalidFormat,          "Invalid message format." },
            { MessageErrorType.InvalidLength,          "Message too long." },
            { MessageErrorType.Unauthorized,           "Access denied." },
            { MessageErrorType.ServerInternalError,    "Server internal error." },
            { MessageErrorType.ReceiverNotFound,       "No user online with name: {0}" },
            { MessageErrorType.NameAlreadyTaken,       "Name already taken." },
            { MessageErrorType.InvalidUsername,        "Invalid username." }
        };

    
    private static Message CreateMessage(
        MessageType type,
        string sender,
        string receiver,
        string text)
    {
        return new Message(
            type,
            sender,
            receiver,
            text
        );
    }


    private static string ResolveErrorText(MessageErrorType errorType, string? argument)
    {
        if (!_errorDictionary.TryGetValue(errorType, out string template))
            template = _errorDictionary[MessageErrorType.Unknown];

        if (argument == null)
            return template;

        return string.Format(template, argument);
    }


    public static Message CreateErrorMessage(
        MessageErrorType errorType,
        string receiver,
        string? argument = null)
    {
        string text = ResolveErrorText(errorType, argument);

        return CreateMessage(
            MessageType.ServerError,
            "SERVER",
            receiver,
            text
        );
    }


    public static Message CreateUserMessage(string sender, string receiver, string text)
    {
        return CreateMessage(MessageType.UserMessage, sender, receiver, text);
    }

    public static Message CreateSystemMessage(string text)
    {
        return CreateMessage(MessageType.SystemInfo, "SERVER", "ALL", text);
    }

    public static Message CreateBroadcast(string sender, string text)
    {
        return CreateMessage(MessageType.Broadcast, sender, "ALL", text);
    }

    public static Message CreateCommandResponse(string receiver, string text)
    {
        return CreateMessage(MessageType.CommandResponse, "SERVER", receiver, text);
    }
}
