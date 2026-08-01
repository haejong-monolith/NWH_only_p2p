using UnityEngine;

namespace Monolith.VehicleInput
{
    public sealed class SinglePlayerVehicleInputBinder : MonoBehaviour
    {
        [SerializeField]
        private LocalVehicleInputReader inputReader;

        [SerializeField]
        private NwhVehicleInputConsumer initialVehicle;

        private NwhVehicleInputConsumer currentVehicle;

        private void Awake()
        {
            if (inputReader == null)
            {
                inputReader =
                    GetComponent<LocalVehicleInputReader>();
            }
        }

        private void Start()
        {
            if (initialVehicle != null)
            {
                BindVehicle(initialVehicle);
            }
        }

        private void OnDisable()
        {
            UnbindCurrentVehicle();
        }

        public void BindVehicle(
            NwhVehicleInputConsumer vehicle)
        {
            if (vehicle == currentVehicle)
            {
                return;
            }

            UnbindCurrentVehicle();

            currentVehicle = vehicle;

            if (currentVehicle != null &&
                inputReader != null)
            {
                currentVehicle.Bind(inputReader.State);
            }
        }

        public void UnbindCurrentVehicle()
        {
            if (currentVehicle != null)
            {
                currentVehicle.Unbind();
                currentVehicle = null;
            }
        }
    }
}