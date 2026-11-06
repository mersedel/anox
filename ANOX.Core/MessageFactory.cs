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
            { MessageErrorType.InvalidLength,          "Message too long, it was not delivered." },
            { MessageErrorType.Unauthorized,           "Access denied." },
            { MessageErrorType.ServerInternalError,    "Server internal error." },
            { MessageErrorType.ReceiverNotFound,       "No user online with name: {0}" },
            { MessageErrorType.NameAlreadyTaken,       "Name already taken." },
            { MessageErrorType.InvalidUsername,        "Invalid username. Use 1-20 letters, digits, '_', '-' or '.'." },
            { MessageErrorType.AlreadyInSession,       "User already in session." },
            { MessageErrorType.CannotConnectToSelf,    "You can't connect to yourself." },
            { MessageErrorType.InviteNotFound,         "No pending chat request from: {0}" },
            { MessageErrorType.TooManyRequests,        "Too many chat requests. Try again in {0}s." },
            { MessageErrorType.ProtocolMismatch,       "Unsupported client version. The server uses protocol {0}, please update the client." }
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
        if (!_errorDictionary.TryGetValue(errorType, out string? template))
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

        // "<code>|<text>": the client reacts to the code, not to the wording. Read with MessageParser.ParseError.
        return CreateMessage(
            MessageType.ServerError,
            "SERVER",
            receiver,
            $"{(int)errorType}|{text}"
        );
    }


    public static Message CreateUserMessage(string sender, string receiver, string text)
    {
        return CreateMessage(MessageType.UserMessage, sender, receiver, text);
    }
    
    /// <summary>The username goes in the sender field, the client's protocol version in the text.</summary>
    public static Message CreateRegisterMessage(string username)
    {
        return CreateMessage(MessageType.Register, username, "SERVER", ProtocolInfo.Version.ToString());
    }

    public static Message CreateSystemMessage(string receiver, string text)
    {
        return CreateMessage(MessageType.SystemInfo, "SERVER", receiver, text);
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

    public const string InviteAccept = "accept";
    public const string InviteDecline = "decline";

    /// <summary>Client -> server: answer to a chat request from <paramref name="requester"/>.</summary>
    public static Message CreateSessionInviteResponse(string sender, string requester, bool accept)
    {
        return CreateMessage(MessageType.SessionInviteResponse, sender, requester, accept ? InviteAccept : InviteDecline);
    }

    /// <summary>
    /// Server -> client: a pending chat request between <paramref name="otherUser"/> and the receiver is gone.
    /// The sender field is the other user, the text is an <see cref="InviteCancelReason"/>.
    /// </summary>
    public static Message CreateSessionInviteCancelled(string otherUser, string receiver, InviteCancelReason reason)
    {
        return CreateMessage(MessageType.SessionInviteCancelled, otherUser, receiver, reason.ToString());
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
