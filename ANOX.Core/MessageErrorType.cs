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
    InvalidUsername = 7
}