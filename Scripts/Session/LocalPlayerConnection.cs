namespace Blindfly.Networking
{
    public static class LocalPlayerConnection
    {
        public static PlayerConnectionData Data =
            PlayerConnectionData.DefaultRequest;

        public static void Reset()
        {
            Data =
                PlayerConnectionData.DefaultRequest;
        }
    }
}
