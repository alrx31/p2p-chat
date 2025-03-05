namespace p2p_chat;

enum MessageTypes : byte
{
    Message = 1,
    Name = 2,
    UserEntered = 3,
    UserLeft = 4,
    PeerList = 5
}