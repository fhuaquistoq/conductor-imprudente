# Taxi VR — versión jugable

Escena: `Assets/_Project/Scenes/TaxiVR_Playable.unity`. Conserva `Main.unity` como escena histórica del bootstrap. Motor instalado: Unity **6000.6.0f1**; no se ha cambiado la versión del Editor.

## Iniciar

### En Unity, con el simulador de Meta

Atajo: **TaxiVR > Playable > 5 - Jugar con simulador XR** activa el simulador y entra en Play de una vez. Paso a paso:

1. Instala el **Meta XR Simulator** (aplicación independiente).
2. **TaxiVR > Playable > 4 - Activar simulador XR** (equivale a `Window > Meta > Meta XR Simulator > Activate`). Deja el runtime OpenXR del Editor apuntando al simulador.
3. Abre la escena jugable y pulsa **Play**. El simulador se abre solo al entrar en Play.

La aplicación intenta siempre XR primero y espera hasta 120 frames a que el visor enganche; si no aparece ninguno, cae a teclado y ratón y lo avisa en la consola. Solo se fuerza escritorio con la casilla **Force Desktop** o con el argumento `-taxivr-desktop`.

**Si el juego arranca en escritorio**, es casi siempre esto: la activación es una variable de entorno del proceso del Editor (`XR_SELECTED_RUNTIME_JSON`), no un ajuste del sistema. Si el simulador no está activo, el runtime vuelve al de Quest Link (`oculus_openxr_64.json`); sin visor conectado OpenXR falla con `ErrorFormFactorUnavailable` y la aplicación cae a escritorio. Vuelve a ejecutar **TaxiVR > Playable > 4**; el estado aparece en la consola y en `Logs/TaxiConfigurationChecks.txt`.

Para probar con **manos** en lugar de mandos, abre el panel **Inputs** del simulador y elige el modo de manos para cada lado. La aplicación usa las articulaciones reales cuando el runtime las ofrece (XR Hands); si solo hay mandos, dibuja manos virtuales dirigidas por ellos, sin cambiar nada del juego.

> Ruido conocido: el paquete **MR Utility Kit** de Meta se inicializa solo y se queja contra el simulador (`ErrorFunctionUnsupported`, un `NullReferenceException` en `MRUK.Shared.cs`). El juego no usa ese paquete; se puede recortar la lista de paquetes de Meta más adelante.

### Ejecutable

En `Builds/Playable`, abrir **Jugar - Escritorio.bat** (teclado y ratón) o **Jugar - VR.bat** (OpenXR: visor o simulador). También sirve `TaxiVR.exe -taxivr-desktop` y `TaxiVR.exe`. Conservar la carpeta del ejecutable completa.

## Interfaz

**El juego no muestra UI 2D**: ni textos de ayuda, ni carteles, ni menús superpuestos. Todo lo que el jugador lee está dentro del mundo (velocímetro, pantalla de radio, GPS, papeles). El diagnóstico interno existe, pero **solo** se activa con el argumento `-taxivr-debug` o marcando **Show Debug In Editor** en `Taxi VR - playable composition`; en ese caso la tecla F1 lo muestra u oculta.

## Controles

| Acción | Escritorio | VR |
|---|---|---|
| Mirar | Mover el ratón (clic para capturar el cursor, Esc lo libera) | Mover cabeza |
| Agarrar | Mantener clic izquierdo sobre el objeto | Pinza índice-pulgar o grip del mando |
| Tocar un botón | Clic izquierdo | Acercar la punta del índice |
| Girar volante | A/D, o agarrar y arrastrar | Agarrar el aro con una o dos manos y girar |
| Acelerar / frenar | W / S o Espacio | Gatillo derecho / izquierdo |
| Acelerar / frenar con los pies | `FootTracker.exe` o `emit_mock` | Marcadores verde (acelera) y rojo (frena) |
| Conducción sin mandos | C activa crucero suave | Botones físicos CRUCERO y FRENAR |
| Avanzar / reversa | E / Q; agarrar la palanca y desplazarla | Agarrar y desplazar la palanca D/R |
| Ajustar radio o espejo | Agarrar y arrastrar; rueda del ratón | Agarrar y girar muñeca |
| Acercar comida o papel | Agarrar y usar rueda del ratón | Mover la mano |
| Centrar vista | H | H en el PC, mirando hacia delante |
| Recuperar taxi en la calle | R | R en el PC |
| Pausa / liberar cursor | Esc | Esc en el PC |

El GPS dibuja las calles, la ruta azul y el destino amarillo. Estacionar **dos segundos** en la zona azul completa una entrega y genera otro destino. RUTA elige otro destino. El mundo sigue disponible para conducción libre.

## Conducción

El taxi usa **física real con `WheelColliders`**: cuatro ruedas con suspensión, transferencia de peso, motor en el eje trasero, frenada por rueda, barra antivuelco, carga aerodinámica y dirección sensible a la velocidad (34° parado, 5° a velocidad máxima). Ya no se fija la velocidad a mano ni se congelan los ejes: el coche se apoya en sus ruedas y se puede volcar si se conduce mal.

El mando de los pedales es digital (0 o 1) y se convierte en una rampa interna de ~0,2 s para que el coche no dé tirones. Con los dos pedales a la vez se activa el derrape: humo, chirrido y pérdida temporal de agarre.

El cambio D/R es una palanca con recorrido real y detente: se agarra, se desplaza y solo cambia de marcha al superar el umbral. Si el taxi va demasiado rápido (más de 2,5 m/s) el cambio no se engrana y la palanca vuelve sola a la marcha actual, para que el estado visual nunca mienta.

## Ciudad

La ciudad es **procedural**: cada manzana se genera a partir de un hash de su coordenada, así que es reproducible pero **no es contenido guardado en la escena**. La escena jugable se mantiene ligera a propósito.

Para **ver y revisar la ciudad en el editor** sin entrar en Play usa el menú **TaxiVR > Playable > 0 - Vista previa de la ciudad**. Genera el distrito en una escena nueva y sin guardar, con luz, niebla y cámara libre. Es solo para inspección: no la añadas a la compilación.

> Hornear el distrito dentro de la escena jugable está descartado: se probó y el ejecutable Windows arrancaba con `level0` corrupto (con y sin instancias de prefab, con y sin LODGroups, en build limpio). Con la escena ligera el ejecutable pasa todas las verificaciones.

Si quieres que la ciudad sea realmente editable, el camino correcto es dirigir el diseño **por datos** (un ScriptableObject con el reparto de manzanas, alturas y usos) en lugar de reglas por hash; el generador leería ese asset y los cambios se verían al instante en la vista previa.

## Contenido de esta versión

- Grilla vial sin borde práctico, 49 sectores residentes, reciclaje fuera de la distancia visible, niebla y cambio de origen a los 1.024 metros.
- Edificios de DowntownCity, casas sencillas, patios, árboles, veredas y cruces peatonales. Peatones estilizados en las manzanas cercanas. Ocho vehículos circulan por carriles y frenan ante semáforos y obstáculos.
- Interior del modelo `Taxi_Full.fbx`, cámara sentada, volante con agarre a dos manos, palanca D/R, radio con tres pistas sintetizadas, GPS ajustable, retrovisores con cámaras traseras, ventana izquierda, café, hamburguesa y papel agarrables.
- **XR Hands como entrada principal**: si el runtime ofrece hand tracking se usan las articulaciones reales de los dedos; si no, se representan manos virtuales dirigidas por los mandos Touch. El agarre se libera al perder el seguimiento.
- **Tracking de pies** por webcam: `FootTracker/` clasifica dos marcadores y envía el estado por UDP local a 30 Hz. Los pedales arrancan **desarmados** con ambos pies apoyados y se arman al levantar un pie por primera vez; un marcador perdido es `unknown`, nunca `down`, y una pérdida prolongada va a neutro.

## Pedales por visión artificial

El ejecutable no abre la cámara: `FootTracker/` es un proceso Python independiente que clasifica los marcadores y envía por UDP. Para probarlo sin cámara:

```powershell
cd FootTracker
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install -e ".[dev]"
.\.venv\Scripts\python.exe -m tools.emit_mock --script truth-table
```

Con cámara real: `.\.venv\Scripts\python.exe -m foottracker --camera 0 --preview`, dejando los pies apoyados ~0,75 s para calibrar. La clasificación real con webcam es una comprobación manual de hardware; los tests usan imágenes sintéticas y el contrato de cable se verifica con un receptor UDP real.

## Verificación reproducible

- Tests de edición: `TaxiVR.Tests.EditMode`, incluidos sectores negativos, semáforos excluyentes, salto angular del volante, hash estable, ruta A* conectada y la máquina de pedales (tabla completa, armado, `unknown`, debounce, pérdida y recuperación).
- Tests Python: `python -m pytest` en `FootTracker/`, con clasificación sobre imágenes sintéticas y contrato de cable contra un receptor UDP real.
- Verificaciones de configuración: menú **TaxiVR > Playable > 2 - Verify configuration**, resultado en `Logs/TaxiConfigurationChecks.txt`.
- Integración del ejecutable: `TaxiVR.exe -taxivr-desktop -taxivr-verify -logFile verification-player.log`. Ejecuta conducción, registro de manzanas, reciclaje, cambio de origen, agarre, volumen, GPS, entrega y la **tabla de pedales por UDP real**; genera `Verification/results.txt` y tres capturas junto a la build, y sale con código 0 o 3.
- La prueba de escritorio no verifica la imagen estereoscópica, ergonomía, tracking real, compatibilidad de Link ni rendimiento a 72/90 Hz con visor. Esas comprobaciones requieren una sesión de hardware o del simulador.

## Código

`Assets/_Project/Scripts/TaxiVR/Playable/` contiene los sistemas independientes de ciudad, vehículo, manos, interacciones, GPS, pedales y composición. `PlayableBuilder` fija referencias reales a los assets, hornea el distrito y genera la escena. El puente de desarrollo en `Assets/Editor/TaxiDevelopmentBridge.cs` procesa únicamente comandos locales predeterminados desde `Logs/TaxiCommand.txt`; no abre puertos ni ejecuta código arbitrario.
