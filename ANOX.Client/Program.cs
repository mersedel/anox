namespace ANOX.Client;

class Program
{
    static void Main(string[] args)
    {
        Chat chat = new Chat("192.168.0.55", 5656);
        chat.Start();
    }
}