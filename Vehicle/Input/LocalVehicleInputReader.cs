using NWH.VehiclePhysics2.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Monolith.VehicleInput
{
    public sealed class LocalVehicleInputReader : MonoBehaviour
    {
        [SerializeField]
        private bool readInput = true;

        [SerializeField]
        private bool mouseInput;

        private VehicleInputActions inputActions;
        private VehicleInputState state;

        private const int HShifterGearCount = 10;
        private readonly bool[] shiftIntoHeld = new bool[HShifterGearCount];

        public VehicleInputState State => state;

        private void Awake()
        {
            state = new VehicleInputState();
            inputActions = new VehicleInputActions();

            SetupCallbacks();
        }

        private void OnEnable()
        {
            inputActions?.Enable();
        }

        private void OnDisable()
        {
            inputActions?.Disable();
            state?.ResetAll();
        }

        private void OnDestroy()
        {
            inputActions?.Dispose();
        }

        private void Update()
        {
            if (!readInput)
            {
                state.ResetAll();
                return;
            }

            ReadContinuousInput();
            ReadHeldInput();
            ReadOneShotInput();
        }

        /// <summary>
        /// 현재까지 수집된 단발 입력을 소비한 뒤 초기화한다.
        /// 네트워크 전송 또는 입력 적용 이후 호출한다.
        /// </summary>
        public void ConsumeOneShotInput()
        {
            state?.ResetOneShot();
        }


        private void ReadContinuousInput()
        {
            state.Throttle = mouseInput
                ? Mathf.Clamp(GetMouseVertical(), 0f, 1f)
                : inputActions.VehicleControls.Throttle.ReadValue<float>();

            state.Brakes = mouseInput
                ? -Mathf.Clamp(GetMouseVertical(), -1f, 0f)
                : inputActions.VehicleControls.Brakes.ReadValue<float>();

            state.Steering = mouseInput
                ? Mathf.Clamp(GetMouseHorizontal(), -1f, 1f)
                : inputActions.VehicleControls.Steering.ReadValue<float>();

            state.Clutch =
                inputActions.VehicleControls.Clutch.ReadValue<float>();

            state.Handbrake =
                inputActions.VehicleControls.Handbrake.ReadValue<float>();
        }

        private void ReadHeldInput()
        {
            state.Horn =
                inputActions.VehicleControls.Horn.IsPressed();

            state.Boost =
                inputActions.VehicleControls.Boost.IsPressed();
        }

        private void ReadOneShotInput()
        {
            state.EngineStartStop =
                inputActions.VehicleControls.EngineStartStop.triggered;

            state.ExtraLights =
                inputActions.VehicleControls.ExtraLights.triggered;

            state.HighBeamLights =
                inputActions.VehicleControls.HighBeamLights.triggered;

            state.HazardLights =
                inputActions.VehicleControls.HazardLights.triggered;

            state.LeftBlinker =
                inputActions.VehicleControls.LeftBlinker.triggered;

            state.LowBeamLights =
                inputActions.VehicleControls.LowBeamLights.triggered;

            state.RightBlinker =
                inputActions.VehicleControls.RightBlinker.triggered;

            state.ShiftDown =
                inputActions.VehicleControls.ShiftDown.triggered;

            state.ShiftUp =
                inputActions.VehicleControls.ShiftUp.triggered;

            state.TrailerAttachDetach =
                inputActions.VehicleControls.TrailerAttachDetach.triggered;

            state.FlipOver =
                inputActions.VehicleControls.FlipOver.triggered;

            state.CruiseControl =
                inputActions.VehicleControls.CruiseControl.triggered;

            state.ShiftInto = GetShiftIntoGear();
        }

        private void SetupCallbacks()
        {
            SetupGearShiftInput(
                inputActions.VehicleControls.ShiftIntoR1,
                0);

            SetupGearShiftInput(
                inputActions.VehicleControls.ShiftInto0,
                1);

            SetupGearShiftInput(
                inputActions.VehicleControls.ShiftInto1,
                2);

            SetupGearShiftInput(
                inputActions.VehicleControls.ShiftInto2,
                3);

            SetupGearShiftInput(
                inputActions.VehicleControls.ShiftInto3,
                4);

            SetupGearShiftInput(
                inputActions.VehicleControls.ShiftInto4,
                5);

            SetupGearShiftInput(
                inputActions.VehicleControls.ShiftInto5,
                6);

            SetupGearShiftInput(
                inputActions.VehicleControls.ShiftInto6,
                7);

            SetupGearShiftInput(
                inputActions.VehicleControls.ShiftInto7,
                8);

            SetupGearShiftInput(
                inputActions.VehicleControls.ShiftInto8,
                9);
        }

        private void SetupGearShiftInput(
            InputAction action,
            int index)
        {
            action.started += _ => shiftIntoHeld[index] = true;
            action.canceled += _ => shiftIntoHeld[index] = false;
        }

        private int GetShiftIntoGear()
        {
            for (int i = 0; i < shiftIntoHeld.Length; i++)
            {
                if (shiftIntoHeld[i])
                {
                    return i - 1;
                }
            }

            return VehicleInputState.NoDirectShift;
        }

        private static float GetMouseHorizontal()
        {
            if (Mouse.current == null)
            {
                return 0f;
            }

            Vector2 mousePosition = Mouse.current.position.ReadValue();
            float percent = Mathf.Clamp01(mousePosition.x / Screen.width);

            return percent < 0.5f
                ? -(0.5f - percent) * 2f
                : (percent - 0.5f) * 2f;
        }

        private static float GetMouseVertical()
        {
            if (Mouse.current == null)
            {
                return 0f;
            }

            Vector2 mousePosition = Mouse.current.position.ReadValue();
            float percent = Mathf.Clamp01(mousePosition.y / Screen.height);

            return percent < 0.5f
                ? -(0.5f - percent) * 2f
                : (percent - 0.5f) * 2f;
        }
    }
}