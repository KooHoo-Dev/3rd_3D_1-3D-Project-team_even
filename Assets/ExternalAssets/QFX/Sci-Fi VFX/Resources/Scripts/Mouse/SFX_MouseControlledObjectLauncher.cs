using UnityEngine;

// ReSharper disable once CheckNamespace
namespace QFX.SFX
{
    public class SFX_MouseControlledObjectLauncher : MonoBehaviour
    {
        public SFX_ControlledObject[] ControlledObjects;

        // 0 = Left, 1 = Right, 2 = Middle
        public int MouseButtonCode;
        public bool CallStop = true;

        private void Update()
        {
            if (SFX_InputUtil.GetMouseButtonDown(MouseButtonCode))
            {
                foreach (var controlledObject in ControlledObjects)
                {
                    controlledObject.Setup();
                    controlledObject.Run();
                }
            }
            else if (CallStop && SFX_InputUtil.GetMouseButtonUp(MouseButtonCode))
            {
                foreach (var controlledObject in ControlledObjects)
                    controlledObject.Stop();
            }
        }
    }
}
