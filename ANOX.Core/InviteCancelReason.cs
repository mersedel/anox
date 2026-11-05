namespace ANOX.Core;

/// <summary>Why a pending chat request disappeared without starting a session.</summary>
public enum InviteCancelReason
{
    Declined = 0,
    Expired = 1,
    Cancelled = 2,
    Offline = 3,
    Busy = 4
}
