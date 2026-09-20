using UnityEngine;

namespace TaxiVR.Bootstrap
{
    /// <summary>Estado terminal del arranque XR en Android. Si OpenXR no llega a iniciarse no se juega en 3D:
    /// se muestra este aviso y se bloquea el gameplay, en vez de caer a los controles de escritorio dentro del
    /// visor, que con la cabeza puesta no hay forma de usar.</summary>
    public sealed class FatalXrSetupScreen : MonoBehaviour
    {
        public const string Message =
            "TAXI VR\n\nOpenXR no pudo iniciarse en este visor.\nEl juego no puede continuar.\n\n" +
            "Comprueba que OpenXR esta activo en el dispositivo y vuelve a abrir la aplicacion.";

        /// <summary>Android nunca cae a escritorio por su cuenta. Si el escritorio se pidio a proposito
        /// (ForceDesktop o -taxivr-desktop) se respeta, porque es una decision de quien arranca y no un fallo.</summary>
        public static bool MustHalt(RuntimePlatform platform, bool explicitDesktop, bool xrInitialized) =>
            platform == RuntimePlatform.Android && !explicitDesktop && !xrInitialized;

        public static FatalXrSetupScreen Show()
        {
            var screen = new GameObject("Fatal XR Setup Screen").AddComponent<FatalXrSetupScreen>();
            Debug.LogError(Message.Replace('\n', ' '));
            return screen;
        }

        void OnGUI()
        {
            float scale = Mathf.Clamp(Screen.width / 1280f, .8f, 3f);
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            var panel = new Rect(0, 0, Screen.width / scale, Screen.height / scale);
            var previous = GUI.color;
            GUI.color = new Color(.06f, .07f, .08f, 1f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = previous;
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 26,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                padding = new RectOffset(48, 48, 48, 48),
            };
            style.normal.textColor = new Color(.95f, .86f, .8f);
            GUI.Label(panel, Message, style);
            GUI.matrix = Matrix4x4.identity;
        }
    }
}
