using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Monolith.VehicleInput
{
    [Serializable]
    public sealed class VehicleInputState
    {
        [Header("Continuous Input")]

        [Range(-1f, 1f)]
        [SerializeField]
        private float steering;

        [Range(0f, 1f)]
        [SerializeField]
        private float throttle;

        [Range(0f, 1f)]
        [SerializeField]
        private float brakes;

        [Range(0f, 1f)]
        [SerializeField]
        private float clutch;

        [Range(0f, 1f)]
        [SerializeField]
        private float handbrake;

        [Header("Held Input")]

        [SerializeField]
        private bool horn;

        [SerializeField]
        private bool boost;

        [Header("One-shot Input")]

        [SerializeField]
        private bool shiftUp;

        [SerializeField]
        private bool shiftDown;

        [SerializeField]
        private bool engineStartStop;

        [SerializeField]
        private bool leftBlinker;

        [SerializeField]
        private bool rightBlinker;

        [SerializeField]
        private bool lowBeamLights;

        [SerializeField]
        private bool highBeamLights;

        [SerializeField]
        private bool hazardLights;

        [SerializeField]
        private bool extraLights;

        [SerializeField]
        private bool trailerAttachDetach;

        [SerializeField]
        private bool cruiseControl;

        [SerializeField]
        private bool flipOver;

        [SerializeField]
        private int shiftInto = NoDirectShift;

        public const int NoDirectShift = -999;

        public float Steering
        {
            get => steering;
            set => steering = Mathf.Clamp(value, -1f, 1f);
        }

        public float Throttle
        {
            get => throttle;
            set => throttle = Mathf.Clamp01(value);
        }

        public float Brakes
        {
            get => brakes;
            set => brakes = Mathf.Clamp01(value);
        }

        public float Clutch
        {
            get => clutch;
            set => clutch = Mathf.Clamp01(value);
        }

        public float Handbrake
        {
            get => handbrake;
            set => handbrake = Mathf.Clamp01(value);
        }

        public bool Horn
        {
            get => horn;
            set => horn = value;
        }

        public bool Boost
        {
            get => boost;
            set => boost = value;
        }

        public bool ShiftUp
        {
            get => shiftUp;
            set => shiftUp = value;
        }

        public bool ShiftDown
        {
            get => shiftDown;
            set => shiftDown = value;
        }

        public bool EngineStartStop
        {
            get => engineStartStop;
            set => engineStartStop = value;
        }

        public bool LeftBlinker
        {
            get => leftBlinker;
            set => leftBlinker = value;
        }

        public bool RightBlinker
        {
            get => rightBlinker;
            set => rightBlinker = value;
        }

        public bool LowBeamLights
        {
            get => lowBeamLights;
            set => lowBeamLights = value;
        }

        public bool HighBeamLights
        {
            get => highBeamLights;
            set => highBeamLights = value;
        }

        public bool HazardLights
        {
            get => hazardLights;
            set => hazardLights = value;
        }

        public bool ExtraLights
        {
            get => extraLights;
            set => extraLights = value;
        }

        public bool TrailerAttachDetach
        {
            get => trailerAttachDetach;
            set => trailerAttachDetach = value;
        }

        public bool CruiseControl
        {
            get => cruiseControl;
            set => cruiseControl = value;
        }

        public bool FlipOver
        {
            get => flipOver;
            set => flipOver = value;
        }

        public int ShiftInto
        {
            get => shiftInto;
            set => shiftInto = value;
        }

        public void ResetContinuous()
        {
            steering = 0f;
            throttle = 0f;
            brakes = 0f;
            clutch = 0f;
            handbrake = 0f;
            horn = false;
            boost = false;
        }

        public void ResetOneShot()
        {
            shiftUp = false;
            shiftDown = false;
            engineStartStop = false;

            leftBlinker = false;
            rightBlinker = false;
            lowBeamLights = false;
            highBeamLights = false;
            hazardLights = false;
            extraLights = false;

            trailerAttachDetach = false;
            cruiseControl = false;
            flipOver = false;

            shiftInto = NoDirectShift;
        }

        public void ResetAll()
        {
            ResetContinuous();
            ResetOneShot();
        }
    }
}