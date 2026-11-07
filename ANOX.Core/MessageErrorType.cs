namespace ANOX.Core;

public enum MessageErrorType
{
    Unknown = 0, 
    InvalidFormat = 1,
    InvalidLength = 2,
    Unauthorized = 3,
    ServerInternalError = 4,
    ReceiverNotFound = 5,
    NameAlreadyTaken = 6,
    InvalidUsername = 7,
    AlreadyInSession = 8,
    CannotConnectToSelf = 9,
    InviteNotFound = 10,
    TooManyRequests = 11,
    ProtocolMismatch = 12,
    RateLimited = 13,
    ServerFull = 14,
    TooManyConnections = 15
}