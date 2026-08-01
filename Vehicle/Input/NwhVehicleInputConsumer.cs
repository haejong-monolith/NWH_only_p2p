using NWH.VehiclePhysics2;
using UnityEngine;

namespace Monolith.VehicleInput
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VehicleController))]
    public sealed class NwhVehicleInputConsumer : MonoBehaviour
    {
        [SerializeField]
        private VehicleController vehicleController;

        private VehicleInputState inputState;

        public bool IsBound => inputState != null;

        private void Reset()
        {
            vehicleController = GetComponent<VehicleController>();
        }

        private void Awake()
        {
            if (vehicleController == null)
            {
                vehicleController = GetComponent<VehicleController>();
            }

            if (vehicleController == null)
            {
                Debug.LogError(
                    $"{nameof(NwhVehicleInputConsumer)} requires " +
                    $"{nameof(VehicleController)}.",
                    this);

                enabled = false;
                return;
            }

            // NWH의 씬 전역 InputProvider 자동 수집을 끈다.
            vehicleController.input.autoSetInput = false;
        }

        private void OnDisable()
        {
            ApplyNeutralInput();
        }

        public void Bind(VehicleInputState state)
        {
            inputState = state;

            if (inputState == null)
            {
                ApplyNeutralInput();
            }
        }

        public void Unbind()
        {
            inputState = null;
            ApplyNeutralInput();
        }

        private void Update()
        {
            if (inputState == null)
            {
                ApplyNeutralInput();
                return;
            }

            ApplyInput(inputState);
        }

        private void ApplyInput(VehicleInputState state)
        {
            var input = vehicleController.input;

            input.Steering = state.Steering;
            input.Throttle = state.Throttle;
            input.Brakes = state.Brakes;
            input.Clutch = state.Clutch;
            input.Handbrake = state.Handbrake;

            input.Horn = state.Horn;
            input.Boost = state.Boost;

            input.ShiftUp = state.ShiftUp;
            input.ShiftDown = state.ShiftDown;
            input.ShiftInto = state.ShiftInto;

            input.EngineStartStop = state.EngineStartStop;

            input.LeftBlinker = state.LeftBlinker;
            input.RightBlinker = state.RightBlinker;
            input.LowBeamLights = state.LowBeamLights;
            input.HighBeamLights = state.HighBeamLights;
            input.HazardLights = state.HazardLights;
            input.ExtraLights = state.ExtraLights;

            input.TrailerAttachDetach = state.TrailerAttachDetach;
            input.CruiseControl = state.CruiseControl;
            input.FlipOver = state.FlipOver;

            // Raw 값도 명시적으로 유지한다.
            input.states.steeringRaw = state.Steering;
            input.states.throttleRaw = state.Throttle;
            input.states.brakesRaw = state.Brakes;
            input.states.clutchRaw = state.Clutch;
            input.states.handbrakeRaw = state.Handbrake;
        }

        private void ApplyNeutralInput()
        {
            if (vehicleController == null)
            {
                return;
            }

            var input = vehicleController.input;

            input.Steering = 0f;
            input.Throttle = 0f;
            input.Brakes = 0f;
            input.Clutch = 0f;
            input.Handbrake = 0f;

            input.Horn = false;
            input.Boost = false;

            input.ShiftUp = false;
            input.ShiftDown = false;
            input.ShiftInto = VehicleInputState.NoDirectShift;

            input.EngineStartStop = false;

            input.LeftBlinker = false;
            input.RightBlinker = false;
            input.LowBeamLights = false;
            input.HighBeamLights = false;
            input.HazardLights = false;
            input.ExtraLights = false;

            input.TrailerAttachDetach = false;
            input.CruiseControl = false;
            input.FlipOver = false;

            input.states.steeringRaw = 0f;
            input.states.throttleRaw = 0f;
            input.states.brakesRaw = 0f;
            input.states.clutchRaw = 0f;
            input.states.handbrakeRaw = 0f;
        }
    }
}