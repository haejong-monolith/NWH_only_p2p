using Unity.Netcode;

namespace Blindfly.Networking
{
    /// <summary>
    /// 클라이언트에서 서버로 전송하는 차량 입력 패킷.
    /// </summary>
    public struct VehicleInputNetworkState : INetworkSerializable
    {
        public uint Tick;

        // Continuous
        public float Steering;
        public float Throttle;
        public float Brakes;
        public float Clutch;
        public float Handbrake;

        // Held
        public bool Horn;
        public bool Boost;

        // One-shot
        public bool EngineStartStop;
        public bool ExtraLights;
        public bool HighBeamLights;
        public bool HazardLights;
        public bool LeftBlinker;
        public bool LowBeamLights;
        public bool RightBlinker;

        public bool ShiftDown;
        public bool ShiftUp;

        public bool TrailerAttachDetach;
        public bool FlipOver;
        public bool CruiseControl;

        public int ShiftInto;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref Tick);

            serializer.SerializeValue(ref Steering);
            serializer.SerializeValue(ref Throttle);
            serializer.SerializeValue(ref Brakes);
            serializer.SerializeValue(ref Clutch);
            serializer.SerializeValue(ref Handbrake);

            serializer.SerializeValue(ref Horn);
            serializer.SerializeValue(ref Boost);

            serializer.SerializeValue(ref EngineStartStop);
            serializer.SerializeValue(ref ExtraLights);
            serializer.SerializeValue(ref HighBeamLights);
            serializer.SerializeValue(ref HazardLights);
            serializer.SerializeValue(ref LeftBlinker);
            serializer.SerializeValue(ref LowBeamLights);
            serializer.SerializeValue(ref RightBlinker);

            serializer.SerializeValue(ref ShiftDown);
            serializer.SerializeValue(ref ShiftUp);

            serializer.SerializeValue(ref TrailerAttachDetach);
            serializer.SerializeValue(ref FlipOver);
            serializer.SerializeValue(ref CruiseControl);

            serializer.SerializeValue(ref ShiftInto);
        }
    }
}