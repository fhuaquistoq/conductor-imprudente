using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace TaxiVR.Playable
{
    // Opt-in integration checks in the real player, with evidence written beside the build.
    public sealed class PlayableVerification : MonoBehaviour
    {
        readonly List<string> checks = new();
        readonly List<string> errors = new();
        string folder;
        void OnEnable() { Application.logMessageReceived += Log; }
        void OnDisable() { Application.logMessageReceived -= Log; }
        void Log(string message, string stack, LogType type) { if (type == LogType.Exception || type == LogType.Error) errors.Add(message); }
        void Check(bool condition, string message) { checks.Add((condition ? "PASS " : "FAIL ") + message); }
        IEnumerator Start()
        {
            folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../Verification")); Directory.CreateDirectory(folder);
            yield return new WaitForSeconds(3);
            var root = PlayableRoot.Instance; var drive = root.Drive; var city = root.City;
            Check(city.LoadedSectors == 49, "49 sectors loaded with fixed capacity");
            Check(city.GetComponentsInChildren<CitySector>(true).Length == city.LoadedSectors, "City sectors registered without duplicates");
            Check(root.Player.View != null, "First person camera exists");
            Check(CockpitInteractable.All.Count >= 15, "Cockpit interactions connected");
            Capture(root.Player.View, "01-cockpit.png");
            var start = drive.Body.position; drive.TestThrottle = 1;
            yield return new WaitForSeconds(3);
            Check(drive.Body.position.z > start.z + 5, "Vehicle accelerates forward");
            drive.TestThrottle = 0; drive.Body.linearVelocity = Vector3.zero;
            drive.Body.position = new Vector3(3, 0, 200); city.Refresh();
            yield return null;
            Check(city.LoadedSectors == 49, "Sector recycling stays bounded after travel");
            var absolute = city.AbsolutePosition;
            drive.Body.position = new Vector3(1091, 0, 22);
            yield return null; yield return null;
            Check(city.OriginSector.x != 0 && Mathf.Abs(drive.Body.position.x) < 64, "Floating origin keeps precision after long travel");
            Check(Mathf.Abs(city.AbsolutePosition.x - 1091) < 1, "Floating origin preserves logical world position");
            drive.ResetToRoad();
            var wheel = drive.Wheel; Vector3 a = wheel.transform.TransformPoint(new Vector3(wheel.Radius, 0, 0));
            Vector3 b = wheel.transform.TransformPoint(new Vector3(0, wheel.Radius, 0));
            wheel.Grab(98, a, Quaternion.identity); wheel.Move(98, b, Quaternion.identity);
            wheel.Grab(99, -a + 2 * wheel.transform.position, Quaternion.identity);
            Check(wheel.HolderCount == 2, "Steering accepts two hands");
            yield return new WaitForEndOfFrame();
            Check(Mathf.Abs(wheel.Value) > 50, "Physical hand arc rotates steering");
            wheel.Release(98); wheel.Release(99);
            var knob = CockpitInteractable.All.First(x => x.Kind == CockpitKind.Knob);
            knob.Grab(98, knob.transform.position, Quaternion.identity); float before = knob.Value;
            knob.Move(98, knob.transform.position, Quaternion.Euler(0, 0, -40)); knob.Release(98);
            Check(knob.Value > before, "Wrist rotation changes radio volume");
            var food = CockpitInteractable.All.First(x => x.name == "Hamburguesa");
            Vector3 home = food.transform.position;
            food.Grab(98, home, Quaternion.identity); food.Move(98, home + Vector3.up * .2f, Quaternion.identity);
            Check(food.transform.position.y > home.y + .15f, "Food follows grabbed hand");
            food.Release(98); Check(!food.GetComponent<Rigidbody>().isKinematic, "Released food restores physics"); food.ReturnHome();
            var gpsButton = CockpitInteractable.All.First(x => x.name == "GPS on"); gpsButton.Press(); Check(!root.GPS.Powered, "GPS button toggles power");
            root.GPS.Powered = true;
            drive.Body.position = city.LocalPosition(root.GPS.Destination) + new Vector3(3, 0, 14);
            for (float elapsed = 0; elapsed < 6f && root.GPS.Deliveries == 0; elapsed += Time.deltaTime)
            {
                drive.Body.linearVelocity = Vector3.zero;
                drive.Body.angularVelocity = Vector3.zero;
                yield return null;
            }
            Check(root.GPS.Deliveries == 1, "Stopping at GPS destination completes a delivery");
            drive.ResetToRoad();
            yield return new WaitForSeconds(.4f);
            Capture(root.Player.View, "02-after-drive.png");
            var overview = new GameObject("Verification city camera").AddComponent<Camera>(); overview.CopyFrom(root.Player.View);
            overview.transform.position = drive.transform.position + new Vector3(40, 65, -55); overview.transform.LookAt(drive.transform.position + new Vector3(20, 0, 30));
            overview.cullingMask = ~(1 << 8); Capture(overview, "03-city.png"); Destroy(overview.gameObject);
            yield return FeetChecks();
            Check(errors.Count == 0, "No runtime errors or exceptions: " + errors.Count);
            float frameTime = 0;
            for (int frame = 0; frame < 120; frame++) { yield return null; frameTime += Time.unscaledDeltaTime; }
            checks.Add($"INFO average FPS over 120 frames: {120 / Mathf.Max(.001f, frameTime):F1}; hardware XR not tested by this desktop run");
            File.WriteAllLines(Path.Combine(folder, "results.txt"), checks.Concat(errors));
            bool passed = errors.Count == 0 && !checks.Any(c => c.StartsWith("FAIL"));
            Debug.Log("TAXIVR_VERIFICATION " + (passed ? "PASS" : "FAIL") + " " + folder);
            if (!Application.isEditor) Application.Quit(passed ? 0 : 3);
        }
        UdpClient footSender;
        int footSequence;

        void SendFeet(FootState red, FootState green, bool redValid, bool greenValid)
        {
            footSender ??= new UdpClient();
            var bytes = Encoding.UTF8.GetBytes(FootProtocol.Serialize(new FootPacket(footSequence++, red, green, redValid, greenValid, FootProtocol.Version)));
            footSender.Send(bytes, bytes.Length, "127.0.0.1", FootProtocol.Port);
        }

        IEnumerator HoldFeet(FootState red, FootState green, float seconds, bool redValid = true, bool greenValid = true)
        {
            for (float elapsed = 0; elapsed < seconds; elapsed += Time.deltaTime)
            {
                SendFeet(red, green, redValid, greenValid);
                yield return null;
            }
        }

        IEnumerator FeetChecks()
        {
            var world = PlayableRoot.Instance; var feet = world.Feet; var drive = world.Drive;
            drive.TestThrottle = -1;
            Check(feet != null && feet.SocketBound, "Foot UDP receiver is bound to port " + FootProtocol.Port);
            yield return HoldFeet(FootState.Down, FootState.Down, .35f);
            Check(!feet.Machine.Armed && drive.Throttle < .5f && drive.Brake < .5f, "Both feet down start unarmed and neutral");
            yield return HoldFeet(FootState.Down, FootState.Up, .35f);
            Check(feet.Machine.Armed && drive.Brake > .5f && drive.Throttle < .5f, "First raised foot arms and brakes");
            yield return HoldFeet(FootState.Up, FootState.Down, .35f);
            Check(drive.Throttle > .5f && drive.Brake < .5f, "Green marker down accelerates");
            yield return HoldFeet(FootState.Down, FootState.Down, .5f);
            Check(drive.Throttle > .5f && drive.Brake > .5f && drive.Skidding, "Both markers down skid");
            yield return HoldFeet(FootState.Unknown, FootState.Unknown, .9f, false, false);
            Check(feet.Machine.Warning && !feet.Machine.TrackingValid && drive.Throttle < .5f && drive.Brake < .5f, "Lost markers fail safe to neutral and warn");
            yield return HoldFeet(FootState.Down, FootState.Up, .35f);
            Check(feet.Machine.TrackingValid && drive.Brake > .5f, "Markers recover after a loss");
            drive.Body.linearVelocity = Vector3.zero;
            footSender?.Close();
        }

        void Capture(Camera camera, string file)
        {
            var render = new RenderTexture(1440, 900, 24); var previous = camera.targetTexture; var active = RenderTexture.active;
            camera.targetTexture = render; camera.Render(); RenderTexture.active = render;
            var image = new Texture2D(1440, 900, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0,0,1440,900),0,0); image.Apply();
            File.WriteAllBytes(Path.Combine(folder, file), image.EncodeToPNG()); camera.targetTexture = previous; RenderTexture.active = active;
            render.Release(); Destroy(render); Destroy(image);
        }
    }
}
