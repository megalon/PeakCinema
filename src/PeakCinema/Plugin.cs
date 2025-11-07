using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PeakCinema;

[BepInAutoPlugin]
public partial class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; } = null!;

    internal static PluginModConfig ModConfig { get; private set; } = null!;

    internal static GameObject HUD = null!;

    internal static bool CinemaCamActive { get; private set; }
    internal static Transform? CamTransform { get; private set; }
    internal static bool CameraWasSpawned { get; private set; }
    internal static bool Smoothing { get; private set; } = true;
    internal static float HoldTimer { get; private set; }
    internal static float InitHoldTimer { get; private set; } = 3f;
    internal static List<VoiceObscuranceFilter> VoiceFilters = new List<VoiceObscuranceFilter>();
    internal static Vector3 DeathLocation { get; private set; }

    private void Awake()
    {
        Log = Logger;

        Log.LogInfo($"Plugin {Name} is loaded!");

        Harmony.CreateAndPatchAll(typeof(Plugin));

        ModConfig = new PluginModConfig(Config);

        HoldTimer = InitHoldTimer;
    }

    [HarmonyPatch(typeof(VoiceObscuranceFilter), "Start")]
    [HarmonyPostfix]
    static void VoiceObscuranceFilter_Start(VoiceObscuranceFilter __instance)
    {
        // Fix case where new player joins lobby while camera is active
        if (CinemaCamActive)
        {
            SetVoiceFilterToCinemaCam(__instance);
        }

        VoiceFilters.Add(__instance);
    }

    [HarmonyPatch(typeof(CinemaCamera), "Start")]
    [HarmonyPostfix]
    static void CinemaCamera_Start(CinemaCamera __instance)
    {
        CamTransform = __instance.cam;
        CameraWasSpawned = false;
        HoldTimer = InitHoldTimer;
        DeathLocation = Vector3.zero;
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
            CinemaCamActive = false;

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
            
            // Reset voice filter distance check start position back to the main camera
            foreach (var v in VoiceFilters)
            {
                if (v == null) continue;

                v.head = MainCamera.instance.transform;
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
            if (!CinemaCamActive)
            {
                CinemaCamActive = true;

                HandleVoiceFilters();
            }

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
                __instance.fog.gameObject.SetActive(false);
            }
            if ((bool)__instance.oldCam)
            {
                __instance.oldCam.gameObject.SetActive(false);
            }
            __instance.transform.parent = null;
            __instance.cam.parent = null;

            if (!CameraWasSpawned)
            {
                MoveCameraToPlayerPosition(__instance);
            }

            __instance.cam.gameObject.SetActive(true);

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
            if (localCharacter.data.dead)
            {
                __instance.cam.transform.position = DeathLocation;
            }
            else
            {
                __instance.cam.transform.position = localCharacter.refs.animationPositionTransform.position;
            }

            __instance.cam.transform.position += new Vector3(0, 1.5f, -2);
        }
    }

    private static void HandleVoiceFilters()
    {
        // Remove old filters that no longer exist, for example when a player has left
        for (int i = VoiceFilters.Count - 1; i >= 0; --i)
        {
            VoiceObscuranceFilter filter = VoiceFilters[i];

            if (filter == null)
            {
                VoiceFilters.RemoveAt(i);
            }
        }

        foreach (var v in VoiceFilters)
        {
            if (v == null) continue;

            SetVoiceFilterToCinemaCam(v);
        }

        Log.LogInfo($"Adjusted {VoiceFilters.Count} voice filters!");
    }

    /// <summary>
    /// This changes the voice filters so they use the cinema cam as the
    /// start point for distance checking
    /// </summary>
    private static void SetVoiceFilterToCinemaCam(VoiceObscuranceFilter filter)
    {
        if (CamTransform == null) return;
        if (filter == null) return;

        // "head" is the voice filter linecast start position
        filter.head = CamTransform;
    }

    // We are patching this line
    //   CharacterData component = base.transform.root.GetComponent<CharacterData>();
    // And replacing it with this
    //   CharacterData component = Character.localCharacter.data;
    [HarmonyPatch(typeof(AmbienceAudio), "Update")]
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var codes = new List<CodeInstruction>(instructions);
        bool patched = false;

        var targetFieldCharacter = AccessTools.Field(typeof(AmbienceAudio), "character");
        var targetFieldData = AccessTools.Field(typeof(Character), "data");
        var replacementFieldLocalCharacter = AccessTools.Field(typeof(Character), "localCharacter");
        var replacementFieldData = AccessTools.Field(typeof(Character), "data");

        for (int i = 0; i < codes.Count - 2; i++)
        {
            if (
                codes[i].opcode == OpCodes.Ldarg_0 &&
                codes[i + 1].opcode == OpCodes.Ldfld && codes[i + 1].operand as FieldInfo == targetFieldCharacter &&
                codes[i + 2].opcode == OpCodes.Ldfld && codes[i + 2].operand as FieldInfo == targetFieldData
            )
            {
                Log.LogInfo("Patching AmbienceAudio.Update...");
                codes.RemoveRange(i, 3);
                codes.InsertRange(i,
                [
                    new CodeInstruction(OpCodes.Ldsfld, replacementFieldLocalCharacter),
                    new CodeInstruction(OpCodes.Ldfld, replacementFieldData),
                ]);

                Log.LogInfo("Successfully patched AmbienceAudio.Update!");
                patched = true;
                break;
            }
        }

        if (!patched)
        {
            Log.LogFatal("Failed to patch AmbienceAudio.Update.");
        }

        return codes;
    }

    [HarmonyPatch(typeof(Character), "RPCA_Die")]
    [HarmonyPrefix]
    static bool Character_RPCA_Die(Character __instance)
    {
        if (!__instance.IsLocal) return true;

        DeathLocation = __instance.refs.animationPositionTransform.position;

        return true;
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
