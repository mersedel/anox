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
            { MessageErrorType.InvalidUsername,        "Invalid username. Use 1-20 letters, digits, '_', '-' or '.'." },
            { MessageErrorType.AlreadyInSession,       "User already in session." },
            { MessageErrorType.CannotConnectToSelf,    "You can't connect to yourself." }
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

        // Templates without a placeholder ignore the argument; a missing argument never leaks a raw "{0}".
        return string.Format(template, argument ?? "unknown");
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
    
    public static Message CreateRegisterMessage(string username)
    {
        return CreateMessage(MessageType.Register, username, "SERVER", username);
    }

    public static Message CreateSystemMessage(string text)
    {
        return CreateMessage(MessageType.SystemInfo, "SERVER", "ALL", text);
    }
    
    public static Message CreateSystemMessage(string receiver, string text)
    {
        return CreateMessage(MessageType.SystemInfo, "SERVER", receiver, text);
    }

    public static Message CreateBroadcast(string sender, string text)
    {
        return CreateMessage(MessageType.Broadcast, sender, "ALL", text);
    }

    public static Message CreateCommandResponse(string receiver, string text)
    {
        return CreateMessage(MessageType.CommandResponse, "SERVER", receiver, text);
    }

    /// <param name="commandText">Command name without the leading '/', followed by its arguments, e.g. "getServerTime".</param>
    public static Message CreateCommandRequest(string sender, string commandText)
    {
        return CreateMessage(MessageType.CommandRequest, sender, "SERVER", commandText);
    }

    public static Message CreateSessionConnectRequest(string sender, string receiver)
    {
        return CreateMessage(MessageType.SessionConnectRequest, sender, receiver, string.Empty);
    }
    
    /// <param name="peer">The user who left the session; sent as the message text.</param>
    public static Message CreateSessionEnded(string receiver, string peer)
    {
        return CreateMessage(MessageType.SessionEnded, "SERVER", receiver, peer);
    }

    public static Message CreateSessionLeave(string sender)
    {
        return CreateMessage(MessageType.SessionLeave, sender, "SERVER", string.Empty);
    }

    /// <param name="payload">Opaque data the server echoes back in the Pong (e.g. a send timestamp).</param>
    public static Message CreatePing(string sender, string payload)
    {
        return CreateMessage(MessageType.Ping, sender, "SERVER", payload);
    }

    public static Message CreatePong(string receiver, string payload)
    {
        return CreateMessage(MessageType.Pong, "SERVER", receiver, payload);
    }

    public static Message CreateSessionConnectResponse(string sender, string receiver, string response)
    {
        return CreateMessage(MessageType.SessionConnectResponse, sender, receiver, response);
    }
}
