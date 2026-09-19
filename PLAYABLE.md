# El Taxista Imprudente — versión jugable completa

Escena de producción: **`Assets/Main.unity`** (única escena de la compilación).
Motor: Unity **6000.6.0f1**. Plataforma objetivo: Meta Quest 2/3/3S independiente, con modo escritorio para pruebas.

## Qué es

Juego de conducción arcade en VR sentada. Eres un taxista: recibes una hoja impresa con el perfil del pasajero,
lo recoges (o te largas y te persigue), conduces una ciudad procedural infinita siguiendo —o ignorando— el GPS,
atiendes sus peticiones y cobras según lo bien o mal que lo hayas tratado.

## Cómo arrancar

### En Unity, con el simulador de Meta

1. **TaxiVR > Playable > 4 - Activar simulador XR**.
2. Abre `Assets/Main.unity` y pulsa **Play**.

Menú **TaxiVR > Playable > 5 - Jugar con simulador XR** hace ambos pasos de una vez.

Si no hay visor se cae a teclado y ratón y lo avisa en consola. Para forzar escritorio: casilla **Force Desktop**
o el argumento `-taxivr-desktop`.

### Ejecutable

`TaxiVR.exe -taxivr-desktop` para teclado y ratón; `TaxiVR.exe` para OpenXR.

## Controles

| Acción | Escritorio | VR |
|---|---|---|
| Mirar | Ratón (clic para capturar, Esc libera) | Cabeza |
| Agarrar | Clic izquierdo sobre el objeto | Pinza índice-pulgar |
| Tocar un botón | Clic izquierdo | Punta del índice |
| Volante | A / D, o agarrar y arrastrar | Agarrar el aro con una o dos manos |
| Acelerar / frenar | W / S, Espacio o gatillos | Pie verde / pie rojo (FootTracker) |
| Hablar al pasajero | Mantener **V** | Voz por el micrófono del visor |
| Marcha F / O / R | Q / E | Agarrar la palanca y desplazarla |
| Centrar vista | H | Botón de menú del mando izquierdo (el botón Meta del derecho lo reserva el sistema), o H en el PC |
| Volver a la calle | R | R en el PC |

El FootTracker es opcional y se activa solo: mientras lleguen paquetes frescos al puerto UDP 5055, los pies
mandan sobre el acelerador y el freno, y el teclado, los gatillos y el crucero no los tocan; si el socket calla
más de un segundo, el mando vuelve a ellos. Arrancar sin tracker no bloquea el arranque ni avisa de nada. El
receptor escucha en `0.0.0.0`, así que en el Quest independiente el tracker puede correr en un PC de la LAN
(el puerto no está autenticado: cualquier equipo de la red puede inyectar pedales).

Las **manos** se rastrean en el Quest: la feature *Hand Tracking Subsystem* de OpenXR está encendida para
Android y Standalone, y el tooling la vuelve a encender antes de compilar, así que un APK sin manos no sale en
silencio. Para agarrar vale la pinza índice-pulgar **o** la mano cerrada en torno al objeto; los umbrales
(`PinchDistance`, `ClosedHandCurl`, `GripHysteresis` en `PlayerHands`) son editables en el Inspector con el
visor puesto, que es donde se ajustan de verdad.

## Bucle de partida

```
Boot → WakeUp → BriefPrinted → WaitingPassenger
                               ├── TripActive → Arrived → final normal
                               └── PassengerChasing → PoliceChase
                                                      ├── PoliceCaught
                                                      └── PoliceEscaped
```

- **Arranque (0–9 s)**: pantalla negra, apertura radial de ojos a los 5–6,5 s, impresora de la hoja a los 7–9 s.
  El arranque no espera al FootTracker: los pies solo deciden el acelerador y el freno cuando llegan datos.
- **Pasajero**: aparece 3 s después de imprimirse la hoja. Si el taxi va por debajo de 1 km/h sube por la puerta
  trasera derecha en 4 s. Si arrancas por encima de 10 km/h sin él, te persigue; puedes parar y recogerlo con −10
  de penalización, o huir y provocar la persecución policial.
- **Viaje**: el GPS se enciende al subir el pasajero y traza la ruta sobre el grafo. Detenerse 2 s en la zona
  amarilla cierra el viaje, calcula calidad, puntualidad y cobro.
- **Finales**: `GOOD_ON_TIME`, `GOOD_LATE`, `BAD_ON_TIME`, `BAD_LATE`, más `POLICE_CAUGHT` y `POLICE_ESCAPED`.
  A los 8 s de animación del pasajero la partida se reinicia **sin cambiar de escena**.

## Sistemas implementados

### Ciudad (`Assets/_Project/Scripts/TaxiVR/Playable/CityGrid.cs`)
Rejilla urbana determinista de 64 m. **Todas las calles son rectas y todos los cruces existen**: no hay calles
que aparezcan y desaparezcan, porque el mundo es una cuadrícula y la misma función decide la geometría y el
grafo, de modo que el GPS nunca puede mandar al taxista por una calle que no está dibujada.

- **Manzanas**: cuadradas de 3 × 3 parcelas (8 casas) o rectangulares de 3 × 6 (14 casas), en ambos casos con
  el **centro vacío**. La manzana rectangular ocupa dos sectores y se come la calle intermedia, que desaparece
  también del grafo.
- **Calzada** de 12 m (dos carriles de 3 m por sentido) más 4 m de acera: el bordillo a 15 cm es lo que separa
  visual y físicamente la calle de la manzana.
- **Superficies**: calzada, acera, césped del patio interior, eje amarillo y pasos de cebra, todo en mallas
  combinadas por material, con las coordenadas de textura en metros de mundo para que no haya costuras al
  cruzar de sector. Cada sector reparte su cuadrado en regiones y pinta cada una una sola vez: ni huecos por
  los que caerse ni superficies coplanares peleándose por el mismo píxel.
- **Decoración dura** (el taxi no la mueve): casas del kit, árboles del patio, farolas, bancos, bocas de riego,
  señales, vallas, pivotes, jardineras, semáforos. **Decoración blanda** (el taxi la tira y la arrastra):
  contenedores, cajas, bidones, conos, manholes sueltos y el género del puesto, con masas de 0,6 a 22 kg y
  reciclado automático si salen del mundo.
- **Streaming** (`EndlessCity.cs`): anillo de 7 × 7 sectores. El sector se monta en **dos fotogramas** —suelo
  primero, casas y decoración después— para que cruzar de manzana no cueste un tirón, y el detalle solo se
  monta a un sector de distancia. Cada sector lleva su propio colisionador de 64 × 64 m, así que **el suelo
  existe siempre**, incluso antes de que la manzana esté decorada; si aun así el taxi cayera, se le devuelve a
  la calzada más cercana.
- **Silueta lejana**: fuera del radio de detalle, cada manzana se sustituye por un volumen con la textura de
  fachadas lejanas, para que la ciudad siga teniendo perfil dentro de la niebla sin pagar el precio de las casas.

### Peatones (`CityTrafficSystem.cs`, `PedestrianLook.cs`)
Cuerpos distintos (hombre/mujer × campesino/guardabosques/superhéroe) con peinados y barbas injertados sobre el
mismo esqueleto, y **ciclo de caminata retargetado** desde la librería de animaciones del proyecto: el animador
ajusta su velocidad a la del peatón para que la zancada no patine. El recorrido es el anillo de acera **de la
manzana**, medido sobre la manzana y no sobre el sector, así que también funciona en las manzanas rectangulares;
si el esqueleto o el controlador no están, el peatón conserva la marcha procedural.

### Grafo vial (`Gameplay/CityGraph.cs`)
A\* y **Yen K = 5** sobre la rejilla, con alternativas viables si su ETA ≤ 1,35× la óptima y su solape de aristas
≤ 75 %. El sentido único se mantiene en un 15 % de las calles, que es lo que da vida a la penalización por ir en
sentido contrario; callejones y cierres quedan a cero porque en una ciudad de rejilla no aportan nada. La ciudad
conecta el grafo con `Blocked`, de modo que la calle que se come una manzana rectangular desaparece también para
el navegador. **Selección de destino**: exige ≥ 4 rutas viables y una ETA inicial de 225–255 s, corrigiendo la
distancia iterativamente porque sortear destinos al azar casi nunca cae dentro del margen. **Recalculado** solo
cuando el taxi se aleja más de 10 m de la ruta durante 1 s, no en cada frame.

### Pasajero
12 perfiles deterministas (nombre, destino, velocidad, comida, conversación, temperatura, urgencia). Emociones
`Fear`, `Anger`, `Trust`, `Comfort` con expresiones `Calm/Happy/Concerned/Scared/Angry/Crying`. Voz sintetizada.
La hoja impresa es un objeto físico agarrable y arrugable (compresión a dos manos por debajo de 12 cm durante 0,5 s
→ bola de papel).

### Puntuación
Empieza en 100 y no sale de [0, 100]. Tabla completa de penalizaciones (papelera −1, barrera −2, árbol/edificio −3,
vehículo civil −3, colisión grave −6, peatón −15, rojo −3, sentido contrario −1/3 s, recuperación −10, recogida
tardía −10). Conformidad de velocidad, temperatura y conversación, peticiones (correcta / ignorada / incorrecta),
plazo (ETA + 75 s, urgente + 20 s), calidad de servicio (corte en 60) y tarifa `S/ 8 + km × 6 + 5 urgente` con
multiplicadores 1,00 / 0,80 / 0,60 / 0,35.

### Peticiones
Exactamente tres, a los 55 s (comida), 115 s (radio) y 175 s (temperatura), cada una con 20 s de ventana. Se
responden con el objeto que entregas, la emisora que pones o la temperatura que ajustas.

### Tráfico, semáforos y peatones (`CityTrafficSystem.cs`)
18 coches activos de 32 en reserva, con física completa dentro de 35 m y seguimiento cinemático fuera; reciclado
pasados 120 m y fuera de la vista. Densidad 0–1 recalculada cada 10 s. Semáforos 20 s verde / 3 s ámbar / 1 s todo
rojo por eje (ciclo 48 s), fuente única de verdad para coches, lámparas y penalización por rojo. 24 peatones activos
de 48, reducidos a 12 por encima de 40 km/h.

### Policía (`PoliceSystem.cs`)
3 coches: 1 perseguidor y 2 interceptores. Aparecen a 100–150 m y siempre fuera de la vista. Captura: por debajo de
1 km/h durante 4 s con ≥ 2 coches a menos de 5 m. Escape: sin ningún coche a menos de 120 m y con más de 200 m de
distancia vial durante 45 s.

### Interior
Volante cinemático (una o dos manos; **el recorte real es ±240°, no los ±450° de la especificación**, pendiente de
la fase de interacción de cabina), palanca F/O/R con detente, radio con cinco canciones offline
sintetizadas, clima frío/neutro/cálido, seis alimentos agarrables, espejos ajustables y **solo el retrovisor interior
se desprende** (18 cm de su anclaje durante 250 ms).

### Seguridad y recuperación
Sin barra de vida: los golpes cuestan puntos y asustan al pasajero. Si el taxi queda volcado más de 70° y por debajo
de 2 km/h durante 5 s, se funde a negro, se recoloca y cuesta −10.

## Rendimiento

Objetivo 72 Hz (presupuesto 13,89 ms) en Quest 2. Configuración aplicada: Vulkan único, Multiview, 4× MSAA,
HDR y post-proceso desactivados, SRC Batcher e instancing activos, sombra direccional a 30 m con dos cascadas,
niebla lineal 105→175 m, FFR medio y `AndroidEnableSustainedPerformanceMode`.

El pipeline se fija **por plataforma**: los niveles de calidad Standalone usan `PC_RPAsset` y los de
Android/Meta Quest usan `Mobile_RPAsset` (antes el nivel móvil caía al pipeline global de PC). El presupuesto de
fotograma va a 72 Hz en `GameConstants.TargetFrameRate`.

**Sin verificar**: el presupuesto real de CPU/GPU, el conteo de batches y el rendimiento sostenido a 72 Hz
necesitan una sesión con hardware o con el simulador. La verificación de escritorio solo mide FPS del editor.

## Verificación reproducible

- **Tests de edición**: `TaxiVR.Tests.EditMode`, **153 casos (108 métodos)** más **2 pruebas PlayMode**. Cubren la rejilla urbana (una manzana por
  sector, 8 casas en el cuadrado y 14 en el rectángulo, la calle interior cerrada, simetría de los cierres,
  parcelas sin solape, conectividad del grafo y alternativas de destino), el grafo (conectividad, densidad,
  sentidos únicos, cierres, A\*, Yen, destinos), el reglamento completo (penalizaciones, conformidad, peticiones,
  plazos, tarifas, finales), los 12 pasajeros, la máquina de estados, el guion de arranque, los pedales y la
  composición de la escena de producción.
- **Contraste del arte**: `TaxiVR > City > Wire generated model slots` enlaza edificios, árboles, decoración dura
  y blanda, los seis cuerpos de peatón, los peinados, el controlador de caminata y las texturas de calzada y
  acera. El cableado corre **solo** dentro de `PlayableBuilder.Configure()` (el menú sigue disponible para
  reenlazar a mano) y `Verify()` falla en voz alta si algún slot queda vacío: ya no hay un paso manual silencioso.
- **Determinismo**: la partida sale de una semilla raíz fija que se registra al arrancar (`TaxiVR semilla N`).
  Se puede fijar otra con `-seed=N`, de modo que un reporte se puede reproducir.
- **Tests Python**: `python -m pytest` en `FootTracker/`.
- **Integración del ejecutable**:
  `TaxiVR.exe -taxivr-desktop -taxivr-verify -logFile verification-player.log` ejecuta conducción, registro y
  reciclado de manzanas, origen flotante, agarre, volumen, GPS, viaje completo con ETA, entrega, finales y la tabla
  de pedales por UDP real; genera `Verification/results.txt` y tres capturas.
  En el editor el mismo arnés se lanza con `runtime-check`. El arnés emite **33 comprobaciones** y firma el
  resultado con el commit (`INFO commit ...`, tomado de `TAXIVR_COMMIT` o de `Verification/commit.txt`).

## Código

```
Assets/_Project/Scripts/TaxiVR/
├── Bootstrap/        arranque XR, diagnóstico, escena
├── City/             CityCatalog y CitySectorTemplate (assets de ciudad)
├── Gameplay/         lógica pura y comprobable: CityGraph, ScoreSystem,
│                     TripSession, GameStateMachine, PassengerProfile
└── Playable/         comportamientos de escena: GameDirector, PassengerAgent,
                      PassengerSheet, PoliceSystem, CityTrafficSystem,
                      InteriorControls, VehicleAssist, TaxiDrive, TaxiGPS,
                      PlayerHands, FootReceiver, CockpitInteractable
```

`Assets/Editor/TaxiDevelopmentBridge.cs` procesa únicamente comandos locales predeterminados desde
`Logs/TaxiCommand.txt` (`inspect`, `play`, `stop`, `save`, `refresh`, `test`, `city`, `wire`, `configure`,
`build`, `verify`, `runtime-check`). No abre puertos ni ejecuta código arbitrario.

## Desviaciones respecto a la especificación

Documentadas por exigencia del propio documento (§1). Todas son deliberadas.

1. **Versiones** — la especificación pide Unity 6000.0.66f2, OpenXR 1.16.1 y Meta XR SDK v83. El proyecto ya
   funcionaba sobre Unity **6000.6.0f1**, OpenXR **1.18.0** y Meta XR SDK **205.0.0**. No se han degradado: hacerlo
   habría roto una cadena XR que funciona.
2. **Manzanas de 64 m** — la especificación pide 50 × 50 m. El kit de arte y el grafo comparten la rejilla de 64 m
   (`CityGraph.BlockSize`), y las vías del mundo se dibujan en los múltiplos de 64 para coincidir con los nudos del
   grafo, los carriles (±3 m) y el GPS. Cambiar la rejilla obligaría a rehacer el arte.
3. **Paquetes de Meta** — se han retirado `com.meta.xr.sdk.all` (paraguas redundante: todas sus dependencias ya
   estaban declaradas una a una) y `com.meta.xr.mrutilitykit`, que el proyecto no usa y que registraba errores en
   cada arranque. La lista de SDK que pide la especificación se mantiene intacta.
4. **Arte y voz de los 12 pasajeros** — los hitos 23 y 24 (arte final y audio final) no están hechos. Los pasajeros
   usan los modelos low poly existentes con cara y voz **sintetizadas por procedimiento**; la radio son cinco pistas
   sintetizadas, no música con licencia.
5. **Suelo físico** — el kit de arte no trae una losa continua de calzada, así que la calzada, la acera y el
   césped se generan por código y **cada sector aporta su propio colisionador de 64 × 64 m**. Es lo que hace
   imposible caerse aunque el detalle de la manzana todavía no se haya montado.
6. **GPS** — la especificación pide un mapa de 600 × 600 m. El mapa cubre 600 m en vertical y 800 m en horizontal,
   porque la textura es 256 × 192 y se mantiene la escala uniforme.
7. **Sentido único al 5 %** — la especificación pedía un 15 %. Medido sobre la rejilla nueva, con un 15 % el
   selector de destino solo encontraba cuatro rutas alternativas en 16 de cada 24 intentos; con un 5 % encuentra
   cuatro en 20, que es la fiabilidad que ya tenía el grafo anterior. La penalización por sentido contrario sigue
   teniendo calles donde activarse.
8. **Arranque sin esperar al FootTracker** — la especificación manda esperar al tracker de pies conectado y
   calibrado antes de despertar, con 20 s de gracia. Eso convertía un accesorio en un peaje de arranque, así que
   la fase `WaitForPedalTracker` se ha retirado del guion: los pies son una fuente de entrada más, con prioridad
   mientras llegan paquetes y con el teclado y los mandos de reserva. El receptor escucha además en `0.0.0.0`
   y no en loopback, para que un tracker en un PC pueda alcanzar al Quest independiente.
9. **Mandos Touch como respaldo** — la especificación pide entrada solo por manos y deja fuera de alcance jugar con
   mando. El proyecto mantiene las lecturas de los mandos (grip, gatillo y botón de menú) como red de seguridad
   para cuando el tracking de manos se pierde, y el recentrado de emergencia es ese botón de menú en vez del gesto
   de ambas manos abiertas 2 s que pide la especificación. Los perfiles Touch siguen activados en OpenXR y
   `PlayableBuilder.Verify()` los exige, así que el APK no sale solo con manos.
10. **El asiento del conductor se mueve con el jugador** — la especificación pide asientos rígidos, pero uno
    rígido se atraviesa en cuanto el jugador se echa atrás, porque la cabeza entra en el respaldo. `SeatCompanion`
    reclina y separa `PIVOT_ASIENTO_CONDUCTOR` lo justo para que siga quedando por detrás de la cabeza y lo
    devuelve a su sitio al incorporarse; la referencia es la postura de arranque, así que está quieto al conducir.
    Los cuatro números del movimiento son campos del Inspector porque se ajustan con el visor puesto. El asiento
    del copiloto lleva el mismo componente sin cabeza a la que seguir, a la espera de que el pasajero lo enlace.

## Cadena de suministro

- `com.coplaydev.unity-mcp` está **pineado a un SHA** (`30d22075093d1d35dfb0091c1c7550e9ad948577`) en
  `Packages/manifest.json`, no a la rama `main`. Ese paquete ejecuta código en el Editor; fijar el commit
  evita que entre una revisión no revisada.
- El API layer de OpenXR `XrApiLayer_METAX_operator.dll` ("agentic interface for OpenXR") está
  **desactivado** (la feature `API Layers` del perfil Standalone y la capa quedan a `0` en
  `Assets/XR/Settings/OpenXR Package Settings.asset`). El repositorio no trae su fuente, build step ni
  firma, así que no se carga hasta auditar su procedencia. Para reinstaurarlo: activar la feature
  `API Layers` y volver a marcar la capa en `Window > XR > OpenXR`.
- La configuración local del DevAgent de Meta XR (`Assets/Resources/DevAgentSettings.asset`) **no se
  versiona**: contenía un `accessToken` que se purgó de la historia de git con `git filter-repo`. El
  archivo vive solo en local (ignorado por `.gitignore`) y su token debe rotarse en
  `Meta XR > Immersive Debugger`.

## Pendiente

- Arte final y audio final (hitos 23 y 24).
- Medición de rendimiento con visor real y ajuste del presupuesto de 72 Hz (hitos 25 y 28).
- Compilación y despliegue del APK en Quest: la configuración Android (IL2CPP, ARM64, Vulkan único, minSdk 32,
  targetSdk 34, `com.unsa.eltaxistaimprudente`) está aplicada, pero la compilación Android no se ha ejecutado aquí.
