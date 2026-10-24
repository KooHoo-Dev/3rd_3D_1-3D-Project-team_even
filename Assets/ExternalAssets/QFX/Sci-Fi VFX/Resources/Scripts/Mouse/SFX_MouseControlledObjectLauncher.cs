using Study.Utilities;
using UnityEngine;


// ReSharper disable once CheckNamespace
namespace QFX.SFX
{
    public class SFX_MouseControlledObjectLauncher : MonoBehaviour
    {
        public SFX_ControlledObject[] ControlledObjects;
        public int MouseButtonCode;
        public bool CallStop = true;

        private void Update()
        {
            if (SimpleInput.GetMouseButtonDown(SimpleInput.MouseButton.Left))
            {
                foreach (var controlledObject in ControlledObjects)
                {
                    controlledObject.Setup();
                    controlledObject.Run();
                }
            }
            else if (CallStop && (SimpleInput.GetMouseButtonUp(SimpleInput.MouseButton.Left)))
            {
                foreach (var controlledObject in ControlledObjects)
                    controlledObject.Stop();
            }
        }
    }
}