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
    SessionConnectResponse = 7
}