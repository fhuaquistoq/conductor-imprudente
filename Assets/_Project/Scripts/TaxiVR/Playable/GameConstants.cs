using UnityEngine;

namespace TaxiVR.Playable
{
    /// <summary>Constantes compartidas del juego, para no dejar numeros sueltos por el codigo.</summary>
    public static class GameConstants
    {
        /// <summary>Objetivo de fotogramas por segundo (Quest 2). Coincide con el presupuesto de PLAYABLE.md.</summary>
        public const int TargetFrameRate = 72;
    }

    /// <summary>Capas del proyecto. Se referencian por nombre (TagManager) en vez de por enteros sueltos.</summary>
    public static class Layers
    {
        /// <summary>Triggers de interaccion de la cabina (botones, volante, GPS, espejos).</summary>
        public const int Interaction = 8;
        /// <summary>Carroceria del taxi y del trafico.</summary>
        public const int Vehicle = 9;
    }

    /// <summary>Anclajes de la cabina: nombres de las piezas del FBX y posiciones de los interactuables. Tenerlos
    /// en un solo sitio deja renombrar una pieza o mover un boton sin tocar el compositor.</summary>
    public static class CockpitAnchors
    {
        public const string Wheel = "PIVOT_VOLANTE";
        public const string Gear = "PIVOT_CAMBIOS_DR";
        public const string VolumeKnob = "PIVOT_RADIO_-0.067";
        public const string TuneKnob = "PIVOT_RADIO_0.137";
        public const string InteriorMirror = "PIVOT_ESPEJO_INTERIOR";
        public const string LeftMirror = "PIVOT_RETROVISOR_ORIGINAL_L";
        public const string RightMirror = "PIVOT_RETROVISOR_ORIGINAL_R";
        public const string FrontGlass = "Taxi_L_FRONT_GLASS";
        public const string DriverSeat = "PIVOT_ASIENTO_CONDUCTOR";

        public static readonly Vector3 RadioPower = new(-.043f, .6f, .445f);
        public static readonly Vector3 Station = new(.035f, .6f, .445f);
        public static readonly Vector3 Cruise = new(-.185f, .737f, .487f);
        public static readonly Vector3 Brake = new(-.10f, .737f, .487f);
        public static readonly Vector3 Window = new(-.659f, .598f, .39f);
        public static readonly Vector3 Speedometer = new(-.405f, .759f, .574f);
        public static readonly Vector3 RadioDisplay = new(.035f, .661f, .432f);
        public static readonly Vector3 RadioText = new(.035f, .661f, .423f);
        public static readonly Vector3 GpsBase = new(.035f, .803f, .448f);
        public static readonly Vector3 GpsOn = new(-.066f, .704f, .415f);
        public static readonly Vector3 GpsRoute = new(.03f, .704f, .415f);
        public static readonly Vector3 InteriorMirrorQuad = new(0, 1.015f, .343f);
        public static readonly Vector3 LeftMirrorQuad = new(-.81f, .76f, .79f);
        public static readonly Vector3 RightMirrorQuad = new(.81f, .76f, .79f);
        public static readonly Vector3 Tray = new(.28f, .37f, .06f);
    }
}
