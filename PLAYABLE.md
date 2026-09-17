# Taxi VR — primera versión jugable

Escena: `Assets/_Project/Scenes/TaxiVR_Playable.unity`. Conserva `Main.unity` como escena histórica del bootstrap. Motor instalado: Unity **6000.6.0f1**; no se ha cambiado la versión del Editor.

## Iniciar

En `Builds/Playable`, abrir **Jugar - Escritorio.bat** para teclado y ratón, o **Jugar - VR.bat** para OpenXR. También se puede ejecutar `TaxiVR.exe -taxivr-desktop` o `TaxiVR.exe` respectivamente. Conservar la carpeta del ejecutable completa.

En Unity, abrir la escena jugable y pulsar Play. Por defecto el Editor usa escritorio; para VR desmarcar **Desktop In Editor** en `Taxi VR - playable composition`. El menú **TaxiVR > Playable** permite regenerar la escena, validar la configuración y compilar Windows.

## Controles

| Acción | Escritorio | VR |
|---|---|---|
| Mirar | Mantener botón derecho y mover ratón | Mover cabeza |
| Agarrar | Mantener clic izquierdo sobre el objeto | Grip del mando o pinza índice-pulgar |
| Tocar un botón | Clic izquierdo | Acercar la punta del índice |
| Girar volante | A/D, o agarrar y arrastrar | Agarrar el aro con una o dos manos y girar |
| Acelerar / frenar | W / S o Espacio | Gatillo derecho / izquierdo |
| Conducción sin mandos | C activa crucero suave | Botones físicos CRUCERO y FRENAR |
| Avanzar / reversa | E / Q, con el taxi detenido | Agarrar y desplazar la palanca D/R |
| Ajustar radio o espejo | Agarrar y arrastrar; rueda del ratón | Agarrar y girar muñeca |
| Acercar comida o papel | Agarrar y usar rueda del ratón | Mover la mano |
| Centrar vista | H | H en el PC, mirando hacia delante |
| Recuperar taxi en la calle | R | R en el PC |
| Ayuda / pausa | F1 / Esc | F1 / Esc en el PC |

El GPS dibuja las calles, la ruta azul y el destino amarillo. Estacionar **dos segundos** en la zona azul completa una entrega y genera otro destino. RUTA elige otro destino. El mundo sigue disponible para conducción libre.

## Contenido de esta versión

- Grilla vial sin borde práctico, con 49 sectores residentes, reciclaje fuera de la distancia visible, niebla y cambio de origen a los 1.024 metros para conservar precisión.
- Edificios de DowntownCity, casas sencillas, patios, árboles, veredas y cruces peatonales. Los peatones estilizados caminan alrededor de las manzanas cercanas. Ocho vehículos circulan por carriles y frenan ante semáforos y obstáculos.
- Interior del modelo existente `Taxi_Full.fbx`, cámara sentada, volante con agarre a dos manos, palanca D/R, radio con tres pistas sintetizadas, volumen, GPS ajustable, retrovisores con cámaras traseras, ventana izquierda, café, hamburguesa y papel agarrables.
- OpenXR, mandos Touch y XR Hands. Los dedos de las manos reales siguen las articulaciones; con mandos se representan manos virtuales. Se libera el agarre cuando se pierde tracking. La pérdida de tracking de cabeza después de una sesión activa frena el taxi.
- Controles provisionales de aceleración y frenado; los pedales y la webcam están fuera de esta entrega.

## Alcance y hardware

Esta entrega es un prototipo de conducción e interacción. Los peatones usan modelos sencillos articulados; el tráfico recorre carriles rectos; los retrovisores muestran cámaras traseras ajustables. No incluye aún pasajeros narrativos, lesiones, averías, peticiones ni evaluación final de la especificación extensa.

Meta documenta el tracking de manos de Quest por Link en **Unity Editor** como ayuda de desarrollo, sin garantizarlo en el ejecutable Windows. La ruta PCVR del ejecutable admite manos virtuales controladas por Touch. Si el runtime expone `XR_EXT_hand_tracking`, la aplicación usa sus articulaciones; de lo contrario muestra el estado de entrada disponible. No se presenta la simulación como prueba de manos reales.

Referencia: https://developers.meta.com/horizon/documentation/unity/unity-handtracking-overview/

## Verificación reproducible

- Tests de edición: `TaxiVR.Tests.EditMode`, incluidos sectores negativos, semáforos excluyentes, salto angular del volante, hash estable y ruta A* conectada.
- Verificaciones de configuración: menú **TaxiVR > Playable > 2 - Verify configuration**, resultado en `Logs/TaxiConfigurationChecks.txt`.
- Integración del ejecutable: `TaxiVR.exe -taxivr-desktop -taxivr-verify -logFile verification-player.log`. Ejecuta conducción, reciclaje, cambio de origen, agarre, volumen, GPS y entrega; genera `Verification/results.txt` y tres capturas junto a la build, y sale con código 0 o 3.
- La prueba de escritorio no verifica la imagen estereoscópica, ergonomía, tracking real, compatibilidad de Link ni rendimiento a 72/90 Hz con visor. Esas comprobaciones requieren una sesión de hardware.

## Código

`Assets/_Project/Scripts/TaxiVR/Playable/` contiene los sistemas independientes de ciudad, vehículo, manos, interacciones, GPS y composición. `PlayableBuilder` fija referencias reales a los assets y genera la escena. El puente de desarrollo en `Assets/Editor/TaxiDevelopmentBridge.cs` procesa únicamente comandos locales predeterminados desde `Logs/TaxiCommand.txt`; no abre puertos ni ejecuta código arbitrario.
