using System;
using UnityEngine;

namespace TaxiVR.Gameplay
{
    public enum SpeedPreference { Slow, Moderate, Fast }
    public enum FoodPreference { Carnivore, Vegetarian, Vegan }
    public enum ConversationPreference { Talkative, Silent }
    public enum TemperaturePreference { Cold, Warm }
    public enum PassengerUrgency { Normal, Urgent }
    public enum DestinationKind { Airport, Hospital, Hotel, Mall, Restaurant, Office, TrainStation, ResidentialBuilding }
    public enum FoodItem { MeatSandwich, Jerky, EggSandwich, CheeseSandwich, VegetableSalad, Apple }
    public enum RequestKind { Food, Radio, Temperature }

    /// <summary>Perfil completo de un pasajero. Todo lo que la hoja impresa necesita saber sale de aqui, de
    /// modo que la hoja y el sistema de puntuacion nunca discrepan.</summary>
    [Serializable]
    public sealed class PassengerProfile
    {
        public int Index;
        public string FullName;
        public DestinationKind Destination;
        public string DestinationName;
        public SpeedPreference Speed;
        public FoodPreference Food;
        public ConversationPreference Conversation;
        public TemperaturePreference Temperature;
        public PassengerUrgency Urgency;
        public int Look;      // variante visual 0..2 (modelo, pelo, color)
        public int Voice;     // variante de voz 0..2

        public float MinimumSpeedKmh => Speed switch
        {
            SpeedPreference.Slow => 20f,
            SpeedPreference.Moderate => 35f,
            _ => 50f
        };

        public float MaximumSpeedKmh => Speed switch
        {
            SpeedPreference.Slow => 35f,
            SpeedPreference.Moderate => 50f,
            _ => 65f
        };

        public bool Urgent => Urgency == PassengerUrgency.Urgent;

        /// <summary>Segundos de habla por minuto que el pasajero considera comodos.</summary>
        public float PreferredSpeechSecondsPerMinute => Conversation == ConversationPreference.Talkative ? 12f : 5f;

        public static readonly FoodItem[] Menu =
        {
            FoodItem.MeatSandwich, FoodItem.Jerky, FoodItem.EggSandwich,
            FoodItem.CheeseSandwich, FoodItem.VegetableSalad, FoodItem.Apple
        };

        /// <summary>Los dos platos validos para la preferencia. Pedir cualquier otro cuenta como incorrecto.</summary>
        public static FoodItem[] Accepted(FoodPreference preference) => preference switch
        {
            FoodPreference.Carnivore => new[] { FoodItem.MeatSandwich, FoodItem.Jerky },
            FoodPreference.Vegetarian => new[] { FoodItem.EggSandwich, FoodItem.CheeseSandwich },
            _ => new[] { FoodItem.VegetableSalad, FoodItem.Apple }
        };

        public bool Accepts(FoodItem item) => Array.IndexOf(Accepted(Food), item) >= 0;

        public static string NameOf(DestinationKind kind) => kind switch
        {
            DestinationKind.Airport => "Aeropuerto Jorge Chavez",
            DestinationKind.Hospital => "Hospital Nacional",
            DestinationKind.Hotel => "Hotel Bolivar",
            DestinationKind.Mall => "Centro Comercial",
            DestinationKind.Restaurant => "Restaurante Campestre",
            DestinationKind.Office => "Edificio de Oficinas",
            DestinationKind.TrainStation => "Estacion Central",
            _ => "Urbanizacion Los Alamos"
        };

        public static string NameOf(FoodItem item) => item switch
        {
            FoodItem.MeatSandwich => "Sandwich de carne",
            FoodItem.Jerky => "Cecina",
            FoodItem.EggSandwich => "Sandwich de huevo",
            FoodItem.CheeseSandwich => "Sandwich de queso",
            FoodItem.VegetableSalad => "Ensalada de verduras",
            _ => "Manzana"
        };

        public static string NameOf(SpeedPreference value) => value switch
        {
            SpeedPreference.Slow => "Despacio (20-35 km/h)",
            SpeedPreference.Moderate => "Normal (35-50 km/h)",
            _ => "Rapido (50-65 km/h)"
        };

        public static string NameOf(FoodPreference value) => value switch
        {
            FoodPreference.Carnivore => "Carnivoro",
            FoodPreference.Vegetarian => "Vegetariano",
            _ => "Vegano"
        };

        public static string NameOf(ConversationPreference value) => value == ConversationPreference.Talkative ? "Conversador" : "Silencioso";

        public static string NameOf(TemperaturePreference value) => value == TemperaturePreference.Cold ? "Frio" : "Calido";

        public static string NameOf(PassengerUrgency value) => value == PassengerUrgency.Urgent ? "Urgente" : "Sin prisa";

        /// <summary>Nombre de la ruta de la hoja impresa. Ancho fijo para que quepa en el papel.</summary>
        public string SheetText() =>
            $"{FullName}\n\nDESTINO\n{DestinationName}\n\nVELOCIDAD\n{NameOf(Speed)}\n\nCOMIDA\n{NameOf(Food)}\n\nCONVERSACION\n{NameOf(Conversation)}\n\nTEMPERATURA\n{NameOf(Temperature)}\n\nURGENCIA\n{NameOf(Urgency)}";
    }

    /// <summary>Los doce pasajeros finales. Se derivan del indice y de la semilla de la partida, asi que la
    /// misma semilla siempre pinta la misma plantilla y los tests pueden fijarlos.</summary>
    public static class PassengerCatalog
    {
        public const int Count = 12;

        static readonly string[] Given =
        {
            "Rosa", "Julio", "Micaela", "Hernan", "Yolanda", "Rafael",
            "Cecilia", "Teofilo", "Nadia", "Alonso", "Pilar", "Eusebio"
        };

        static readonly string[] Family =
        {
            "Quispe", "Zarate", "Mendoza", "Alvarado", "Rojas", "Paredes",
            "Vilchez", "Ccahuana", "Salazar", "Ordonez", "Ttito", "Barrientos"
        };

        static readonly SpeedPreference[] Speeds =
        {
            SpeedPreference.Moderate, SpeedPreference.Slow, SpeedPreference.Fast, SpeedPreference.Moderate,
            SpeedPreference.Slow, SpeedPreference.Fast, SpeedPreference.Moderate, SpeedPreference.Slow,
            SpeedPreference.Fast, SpeedPreference.Moderate, SpeedPreference.Slow, SpeedPreference.Fast
        };

        static readonly FoodPreference[] Foods =
        {
            FoodPreference.Carnivore, FoodPreference.Vegetarian, FoodPreference.Vegan, FoodPreference.Carnivore,
            FoodPreference.Vegan, FoodPreference.Vegetarian, FoodPreference.Carnivore, FoodPreference.Vegetarian,
            FoodPreference.Vegan, FoodPreference.Carnivore, FoodPreference.Vegan, FoodPreference.Vegetarian
        };

        static readonly ConversationPreference[] Conversations =
        {
            ConversationPreference.Talkative, ConversationPreference.Silent, ConversationPreference.Talkative,
            ConversationPreference.Silent, ConversationPreference.Talkative, ConversationPreference.Silent,
            ConversationPreference.Talkative, ConversationPreference.Silent, ConversationPreference.Talkative,
            ConversationPreference.Silent, ConversationPreference.Talkative, ConversationPreference.Silent
        };

        static readonly TemperaturePreference[] Temperatures =
        {
            TemperaturePreference.Warm, TemperaturePreference.Cold, TemperaturePreference.Cold,
            TemperaturePreference.Warm, TemperaturePreference.Warm, TemperaturePreference.Cold,
            TemperaturePreference.Cold, TemperaturePreference.Warm, TemperaturePreference.Warm,
            TemperaturePreference.Cold, TemperaturePreference.Warm, TemperaturePreference.Cold
        };

        static readonly PassengerUrgency[] Urgencies =
        {
            PassengerUrgency.Normal, PassengerUrgency.Normal, PassengerUrgency.Urgent, PassengerUrgency.Normal,
            PassengerUrgency.Normal, PassengerUrgency.Urgent, PassengerUrgency.Normal, PassengerUrgency.Normal,
            PassengerUrgency.Urgent, PassengerUrgency.Normal, PassengerUrgency.Normal, PassengerUrgency.Normal
        };

        public static PassengerProfile Create(int index, int seed)
        {
            int i = ((index % Count) + Count) % Count;
            var destination = (DestinationKind)((i + seed) % 8);
            return new PassengerProfile
            {
                Index = i,
                FullName = Given[i] + " " + Family[(i * 5 + seed) % Family.Length],
                Destination = destination,
                DestinationName = PassengerProfile.NameOf(destination),
                Speed = Speeds[i],
                Food = Foods[i],
                Conversation = Conversations[i],
                Temperature = Temperatures[i],
                Urgency = Urgencies[i],
                Look = (i + seed) % 3,
                Voice = i % 3
            };
        }

        public static PassengerProfile[] CreateAll(int seed)
        {
            var all = new PassengerProfile[Count];
            for (int i = 0; i < Count; i++) all[i] = Create(i, seed);
            return all;
        }
    }
}
