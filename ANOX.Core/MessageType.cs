namespace ANOX.Core;

public enum MessageType
{ 
    UserMessage = 0,
    Register = 1,
    SystemInfo = 2,
    CommandResponse = 3,
    Broadcast = 4,
    ServerError = 5,
    SessionConnectRequest = 6,
    SessionConnectResponse = 7,
    CommandRequest = 8,
    SessionEnded = 9,
    SessionLeave = 10,
    Ping = 11,
    Pong = 12
}