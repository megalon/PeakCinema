using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PeakCinema;

[BepInAutoPlugin]
public partial class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; } = null!;

    internal static PluginModConfig ModConfig { get; private set; } = null!;

    internal static GameObject HUD = null!;

    internal static bool CameraWasSpawned { get; private set; }
    internal static bool Smoothing { get; private set; } = true;
    internal static float HoldTimer { get; private set; }
    internal static float InitHoldTimer { get; private set; } = 3f;

    private void Awake()
    {
        Log = Logger;

        Log.LogInfo($"Plugin {Name} is loaded!");

        Harmony.CreateAndPatchAll(typeof(Plugin));

        ModConfig = new PluginModConfig(Config);
        HoldTimer = InitHoldTimer;
    }

    [HarmonyPatch(typeof(CinemaCamera), "Start")]
    [HarmonyPostfix]
    static void CinemaCamera_Start()
    {
        CameraWasSpawned = false;
        HoldTimer = InitHoldTimer;
    }

    [HarmonyPatch(typeof(CinemaCamera), "Update")]
    [HarmonyPrefix]
    static bool CinemaCameraFix(CinemaCamera __instance)
    {
        if (HUD == null)
        {
            HUD = GameObject.Find("Canvas_HUD");
        }

        if (Input.GetKeyDown(ModConfig.exitCinemaCamKey.Value))
        {
            __instance.on = false;

            HUD?.SetActive(true);

            __instance.cam.gameObject.SetActive(false);

            if (__instance.fog != null)
            {
                __instance.fog.gameObject.SetActive(true);
            }

            if (__instance.oldCam != null)
            {
                __instance.oldCam.gameObject.SetActive(true);
            }
        } else if (Input.GetKey(ModConfig.toggleCinemaCamControlKey.Value))
        {
            HoldTimer -= Time.deltaTime;

            if (HoldTimer <= 0)
            {
                MoveCameraToPlayerPosition(__instance);

                // Switch to the camera immediately if we were not in cam mode
                if (!__instance.on) __instance.on = true;

                HoldTimer = -1;
            }
        } else if (Input.GetKeyUp(ModConfig.toggleCinemaCamControlKey.Value))
        {
            // Don't change state if we just reset cam position
            if (HoldTimer > -1)
            {
                __instance.on = !__instance.on;
            }

            HoldTimer = InitHoldTimer;
        }

        if (__instance.on)
        {
            // Keep player from moving around
            InputSystem.actions.Disable();

            HUD?.SetActive(false);

            __instance.ambience.parent = __instance.transform;
            if ((bool)__instance.fog)
            {
                __instance.fog.gameObject.SetActive(value: false);
            }
            if ((bool)__instance.oldCam)
            {
                __instance.oldCam.gameObject.SetActive(value: false);
            }
            __instance.transform.parent = null;
            __instance.cam.parent = null;

            if (!CameraWasSpawned)
            {
                MoveCameraToPlayerPosition(__instance);
            }

            __instance.cam.gameObject.SetActive(value: true);

            if (Input.GetKeyDown(ModConfig.keySmoothToggle.Value))
            {
                Smoothing = !Smoothing;
            }

            float speed = 0;

            if (Smoothing)
            {
                __instance.vel = Vector3.Lerp(__instance.vel, Vector3.zero, 1f * Time.deltaTime);
                __instance.rot = Vector3.Lerp(__instance.rot, Vector3.zero, 2.5f * Time.deltaTime);

                speed = Input.GetKey(ModConfig.keyMoveFaster.Value) ? 0.2f : 0.05f;

                __instance.rot.y += Input.GetAxis("Mouse X") * speed * 0.05f;
                __instance.rot.x += Input.GetAxis("Mouse Y") * speed * 0.05f;
            }
            else
            {
                __instance.vel = Vector3.zero;
                __instance.rot = Vector3.zero;

                speed = Input.GetKey(ModConfig.keyMoveFaster.Value) ? 10f : 5f;

                __instance.rot.y += Input.GetAxis("Mouse X") * speed * 0.1f;
                __instance.rot.x += Input.GetAxis("Mouse Y") * speed * 0.1f;
            }

            float adjustedSpeed = speed * Time.deltaTime;

            if (Input.GetKey(ModConfig.keyMoveRight.Value))
            {
                __instance.vel.x = Smoothing
                    ? __instance.vel.x + adjustedSpeed
                    : adjustedSpeed;
            }
            if (Input.GetKey(ModConfig.keyMoveLeft.Value))
            {
                __instance.vel.x = Smoothing
                    ? __instance.vel.x - adjustedSpeed
                    : -adjustedSpeed;
            }
            if (Input.GetKey(ModConfig.keyMoveForward.Value))
            {
                __instance.vel.z = Smoothing
                    ? __instance.vel.z + adjustedSpeed
                    : adjustedSpeed;
            }
            if (Input.GetKey(ModConfig.keyMoveBackward.Value))
            {
                __instance.vel.z = Smoothing
                    ? __instance.vel.z - adjustedSpeed
                    : -adjustedSpeed;
            }
            if (Input.GetKey(ModConfig.keyMoveUp.Value))
            {
                __instance.vel.y = Smoothing
                    ? __instance.vel.y + adjustedSpeed
                    : adjustedSpeed;
            }
            if (Input.GetKey(ModConfig.keyMoveDown.Value))
            {
                __instance.vel.y = Smoothing
                    ? __instance.vel.y - adjustedSpeed
                    : -adjustedSpeed;
            }

            __instance.cam.transform.Rotate(Vector3.up * __instance.rot.y, Space.World);
            __instance.cam.transform.Rotate(__instance.transform.right * (0f - __instance.rot.x));
            __instance.cam.transform.Translate(Vector3.right * __instance.vel.x, Space.Self);
            __instance.cam.transform.Translate(Vector3.forward * __instance.vel.z, Space.Self);
            __instance.cam.transform.Translate(Vector3.up * __instance.vel.y, Space.World);
            __instance.t = true;

            CameraWasSpawned = true;
        }
        else
        {
            // Let the player move again
            InputSystem.actions.Enable();
        }

        return false;
    }

    private static void MoveCameraToPlayerPosition(CinemaCamera __instance)
    {
        Character localCharacter = Character.AllCharacters.First(c => c.IsLocal);

        if (localCharacter != null)
        {
            __instance.cam.transform.position = localCharacter.refs.animationPositionTransform.position + new Vector3(0, 0, 1);
        }
    }

    public class PluginModConfig
    {
        public readonly ConfigEntry<KeyCode> toggleCinemaCamControlKey;
        public readonly ConfigEntry<KeyCode> exitCinemaCamKey;

        public readonly ConfigEntry<KeyCode> keyMoveForward;
        public readonly ConfigEntry<KeyCode> keyMoveBackward;
        public readonly ConfigEntry<KeyCode> keyMoveLeft;
        public readonly ConfigEntry<KeyCode> keyMoveRight;
        public readonly ConfigEntry<KeyCode> keyMoveUp;
        public readonly ConfigEntry<KeyCode> keyMoveDown;
        public readonly ConfigEntry<KeyCode> keyMoveFaster;
        public readonly ConfigEntry<KeyCode> keySmoothToggle;

        public PluginModConfig(ConfigFile config)
        {
            toggleCinemaCamControlKey = config.Bind<KeyCode>("Keybinds", "Toggle Cinema Cam Control", KeyCode.F3, "");
            exitCinemaCamKey = config.Bind<KeyCode>("Keybinds", "Exit Cinema Cam", KeyCode.Escape, "");
            keyMoveForward = config.Bind<KeyCode>("Keybinds", "Move Forward", KeyCode.W, "");
            keyMoveBackward = config.Bind<KeyCode>("Keybinds", "Move Backward", KeyCode.S, "");
            keyMoveLeft = config.Bind<KeyCode>("Keybinds", "Move Left", KeyCode.A, "");
            keyMoveRight = config.Bind<KeyCode>("Keybinds", "Move Right", KeyCode.D, "");
            keyMoveUp = config.Bind<KeyCode>("Keybinds", "Move Up", KeyCode.Space, "");
            keyMoveDown = config.Bind<KeyCode>("Keybinds", "Move Down", KeyCode.LeftControl, "");
            keyMoveFaster = config.Bind<KeyCode>("Keybinds", "Move Faster", KeyCode.LeftShift, "");
            keySmoothToggle = config.Bind<KeyCode>("Keybinds", "Toggle Camera Smoothing", KeyCode.CapsLock, "");
        }
    }
}
