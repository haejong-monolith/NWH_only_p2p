using Unity.Collections;
using Unity.Netcode;

namespace Blindfly.Networking
{
    public struct PlayerConnectionData : INetworkSerializable
    {
        public int PlayerIndex;
        public int SlotNumber;
        public int VehicleTypeId;

        public FixedString64Bytes Nickname;

        public int TeamId;
        public int CostumeId;
        public int ColorId;

        public static PlayerConnectionData Unassigned =>
            new PlayerConnectionData
            {
                PlayerIndex = -1,
                SlotNumber = -1,
                VehicleTypeId = -1,
                Nickname = default,
                TeamId = 0,
                CostumeId = 0,
                ColorId = 0
            };

        public static PlayerConnectionData DefaultRequest =>
            new PlayerConnectionData
            {
                PlayerIndex = -1,
                SlotNumber = 0,
                VehicleTypeId = 0,
                Nickname = "Player",
                TeamId = 0,
                CostumeId = 0,
                ColorId = 0
            };

        public void NetworkSerialize<T>(
            BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref PlayerIndex);
            serializer.SerializeValue(ref SlotNumber);
            serializer.SerializeValue(ref VehicleTypeId);

            serializer.SerializeValue(ref Nickname);

            serializer.SerializeValue(ref TeamId);
            serializer.SerializeValue(ref CostumeId);
            serializer.SerializeValue(ref ColorId);
        }
    }
}
