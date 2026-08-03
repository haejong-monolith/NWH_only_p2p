using NWH.VehiclePhysics2;
using NWH.VehiclePhysics2.Sound.SoundComponents;
using UnityEngine;
using UnityEngine.Audio;

namespace Blindfly.Networking
{
    /// <summary>
    /// 순수 Client에서 NWH 물리를 다시 켜지 않고 Snapshot 사운드를 재생한다.
    /// NWH의 Clip/Mixer 설정은 재사용하되, VehicleController 비활성화와 함께
    /// 꺼지는 기존 AudioSource 대신 Client 전용 AudioSource를 사용한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RemoteVehicleSoundPlayback : MonoBehaviour
    {
        [SerializeField]
        private VehicleController vehicleController;

        private AudioSource engineSource;
        private AudioSource fanSource;
        private AudioSource transmissionSource;
        private AudioSource hornSource;
        private AudioSource engineStartStopSource;
        private AudioSource gearChangeSource;

        private bool sourcesInitialized;
        private bool hasPreviousState;
        private VehicleSoundSnapshot previousState;

        private void Awake()
        {
            if (vehicleController == null)
            {
                vehicleController =
                    GetComponentInChildren<VehicleController>(true);
            }
        }

        public void SetVehicleController(VehicleController controller)
        {
            if (controller != null && vehicleController != controller)
            {
                vehicleController = controller;
                sourcesInitialized = false;
            }
        }

        public void Apply(
            VehicleSoundSnapshot state,
            Vector3 worldVelocity)
        {
            if (!EnsureSources())
            {
                return;
            }

            UpdateContinuousSounds(state, worldVelocity);

            if (hasPreviousState)
            {
                PlayTransitionSounds(previousState, state);
            }
            else
            {
                Debug.Log(
                    $"[RemoteVehicleSound] Snapshot 사운드 재생 시작 | " +
                    $"Vehicle={name} | RPM={state.EngineRpmPercent:F2} | " +
                    $"Running={state.HasFlag(VehicleSoundFlags.EngineRunning)} | " +
                    $"EngineClip={(engineSource != null && engineSource.clip != null)} | " +
                    $"Listeners={FindObjectsOfType<AudioListener>().Length}",
                    this);
            }

            previousState = state;
            hasPreviousState = true;
        }

        public void StopAndReset()
        {
            Stop(engineSource);
            Stop(fanSource);
            Stop(transmissionSource);
            Stop(hornSource);
            Stop(engineStartStopSource);
            Stop(gearChangeSource);

            previousState = default;
            hasPreviousState = false;
        }

        private bool EnsureSources()
        {
            if (sourcesInitialized)
            {
                return true;
            }

            if (vehicleController == null ||
                vehicleController.soundManager == null)
            {
                return false;
            }

            var manager = vehicleController.soundManager;

            engineSource = CreateSource(
                "RemoteEngineRunning",
                manager.engineRunningComponent,
                true,
                manager.engineMixerGroup,
                10);

            fanSource = CreateSource(
                "RemoteEngineFan",
                manager.engineFanComponent,
                true,
                manager.engineMixerGroup,
                100);

            transmissionSource = CreateSource(
                "RemoteTransmissionWhine",
                manager.transmissionWhineComponent,
                true,
                manager.transmissionMixerGroup,
                90);

            hornSource = CreateSource(
                "RemoteHorn",
                manager.hornComponent,
                true,
                manager.otherMixerGroup,
                200);

            engineStartStopSource = CreateSource(
                "RemoteEngineStartStop",
                manager.engineStartStopComponent,
                false,
                manager.engineMixerGroup,
                50);

            gearChangeSource = CreateSource(
                "RemoteGearChange",
                manager.gearChangeComponent,
                false,
                manager.transmissionMixerGroup,
                160);

            sourcesInitialized = true;

            if (engineSource == null &&
                fanSource == null &&
                transmissionSource == null &&
                hornSource == null)
            {
                Debug.LogWarning(
                    "[RemoteVehicleSound] 재생할 NWH AudioClip이 없습니다.",
                    this);
            }

            return true;
        }

        private AudioSource CreateSource(
            string sourceName,
            SoundComponent component,
            bool loop,
            AudioMixerGroup mixerGroup,
            int priority)
        {
            if (component == null || component.Clip == null)
            {
                return null;
            }

            GameObject sourceObject = new GameObject(sourceName);
            // Pure clients disable the NWH VehicleController before its sound
            // components are initialized. Accessing component.ContainerGO or
            // component.AudioMixerGroup in that state dereferences the sound
            // component's unassigned internal VehicleController. Keep these
            // independent sources under the active network root and use the
            // SoundManager's public mixer references instead.
            sourceObject.transform.SetParent(transform, false);

            AudioSource source = sourceObject.AddComponent<AudioSource>();
            source.clip = component.Clip;
            source.outputAudioMixerGroup = mixerGroup;
            source.spatialBlend = vehicleController.soundManager.spatialBlend;
            source.dopplerLevel = vehicleController.soundManager.dopplerLevel;
            source.priority = priority;
            source.playOnAwake = false;
            source.loop = loop;
            return source;
        }

        private void UpdateContinuousSounds(
            VehicleSoundSnapshot state,
            Vector3 worldVelocity)
        {
            var manager = vehicleController.soundManager;
            bool engineRunning =
                state.HasFlag(VehicleSoundFlags.EngineRunning) ||
                state.HasFlag(VehicleSoundFlags.StarterActive) ||
                state.EngineRpmPercent > 0.01f;

            EngineRunningComponent engine =
                manager.engineRunningComponent;

            if (engineSource != null && engine != null)
            {
                engineSource.pitch = Mathf.Clamp(
                    state.EngineRpmPercent * engine.pitchRange +
                    engine.pitchOffset,
                    0f,
                    5f);

                engineSource.volume = Mathf.Clamp(
                    engine.baseVolume +
                    state.EngineLoad * engine.volumeRange,
                    engine.baseVolume,
                    2f) * manager.masterVolume;

                SetPlaying(engineSource, engineRunning);
            }

            EngineFanComponent fan = manager.engineFanComponent;

            if (fanSource != null && fan != null)
            {
                fanSource.pitch = Mathf.Clamp(
                    fan.basePitch +
                    fan.pitchRange * state.EngineRpmPercent,
                    0f,
                    5f);

                fanSource.volume =
                    state.EngineRpmPercent *
                    state.EngineRpmPercent *
                    fan.baseVolume *
                    manager.masterVolume;

                SetPlaying(fanSource, engineRunning);
            }

            UpdateTransmission(state, worldVelocity.magnitude);

            if (hornSource != null && manager.hornComponent != null)
            {
                hornSource.volume =
                    manager.hornComponent.baseVolume *
                    manager.masterVolume;

                SetPlaying(
                    hornSource,
                    state.HasFlag(VehicleSoundFlags.HornOn));
            }
        }

        private void UpdateTransmission(
            VehicleSoundSnapshot state,
            float speed)
        {
            TransmissionWhineComponent component =
                vehicleController.soundManager.transmissionWhineComponent;

            if (transmissionSource == null || component == null)
            {
                return;
            }

            float speedRatio = component.maxSpeed > 0.001f
                ? Mathf.Clamp01(speed / component.maxSpeed)
                : 0f;

            transmissionSource.pitch = Mathf.Clamp(
                component.basePitch +
                (state.Gear != 0 ? speedRatio * component.pitchRange : 0f),
                0f,
                5f);

            float speedCoefficient =
                Mathf.Clamp01(Mathf.Abs(speed) * 0.8f);

            float volume =
                (component.baseVolume +
                 state.EngineLoad * component.volumeRange) *
                speedCoefficient *
                vehicleController.soundManager.masterVolume;

            transmissionSource.volume = volume;
            SetPlaying(
                transmissionSource,
                volume > 0.0001f && state.Gear != 0);
        }

        private void PlayTransitionSounds(
            VehicleSoundSnapshot previous,
            VehicleSoundSnapshot current)
        {
            if (!previous.HasFlag(VehicleSoundFlags.StarterActive) &&
                current.HasFlag(VehicleSoundFlags.StarterActive))
            {
                PlayClip(
                    engineStartStopSource,
                    vehicleController.soundManager.engineStartStopComponent,
                    0);
            }

            if (previous.HasFlag(VehicleSoundFlags.IgnitionOn) &&
                !current.HasFlag(VehicleSoundFlags.IgnitionOn))
            {
                PlayClip(
                    engineStartStopSource,
                    vehicleController.soundManager.engineStartStopComponent,
                    1);
            }

            if (previous.Gear != current.Gear && current.Gear != 0)
            {
                GearChangeComponent component =
                    vehicleController.soundManager.gearChangeComponent;

                if (component != null && component.clips.Count > 0)
                {
                    int index = Random.Range(0, component.clips.Count);
                    PlayClip(gearChangeSource, component, index);
                }
            }
        }

        private void PlayClip(
            AudioSource source,
            SoundComponent component,
            int clipIndex)
        {
            if (source == null ||
                component == null ||
                component.clips == null ||
                clipIndex < 0 ||
                clipIndex >= component.clips.Count ||
                component.clips[clipIndex] == null)
            {
                return;
            }

            source.clip = component.clips[clipIndex];
            source.volume =
                component.baseVolume *
                vehicleController.soundManager.masterVolume;
            source.pitch = 1f;
            source.Play();
        }

        private static void SetPlaying(
            AudioSource source,
            bool shouldPlay)
        {
            if (source == null)
            {
                return;
            }

            if (shouldPlay)
            {
                if (!source.isPlaying)
                {
                    source.Play();
                }
            }
            else if (source.isPlaying)
            {
                source.Stop();
            }
        }

        private static void Stop(AudioSource source)
        {
            if (source != null && source.isPlaying)
            {
                source.Stop();
            }
        }
    }
}
