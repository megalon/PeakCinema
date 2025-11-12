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
    internal static Camera? CinemaCamComponent { get; private set; }
    internal static float InitialFOV = 60f;
    internal static bool CameraWasSpawned { get; private set; }
    internal static bool Smoothing { get; private set; } = true;
    internal static float HoldTimer { get; private set; }
    internal static float InitHoldTimer { get; private set; } = 1.5f;
    internal static List<VoiceObscuranceFilter> VoiceFilters = new List<VoiceObscuranceFilter>();
    internal static Vector3 DeathLocation { get; private set; }
    internal static bool PlayerVisibilityToggled { get; private set; } = false;


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

        CinemaCamComponent = __instance.cam.GetComponent<Camera>();

        if (CinemaCamComponent != null)
        {
            InitialFOV = ModConfig.defaultFOV.Value;
            CinemaCamComponent.fieldOfView = ModConfig.defaultFOV.Value;
        }

        CameraWasSpawned = false;
        HoldTimer = InitHoldTimer;
        DeathLocation = Vector3.zero;

        if (HUD == null)
        {
            HUD = GameObject.Find("Canvas_HUD");
        }
    }

    [HarmonyPatch(typeof(CinemaCamera), "Update")]
    [HarmonyPrefix]
    static bool CinemaCameraFix(CinemaCamera __instance)
    {
        HandlePlayerVisibilityInput();

        if (Input.GetKeyDown(ModConfig.exitCinemaCamKey.Value))
        {
            CinemaCamActive = false;
            __instance.on = false;

            HUD?.SetActive(true);
            __instance.cam.gameObject.SetActive(false);

            if (__instance.fog != null)
                __instance.fog.gameObject.SetActive(true);

            if (__instance.oldCam != null)
                __instance.oldCam.gameObject.SetActive(true);

            // Reset voice filter distance check start position back to the main camera
            foreach (var v in VoiceFilters)
            {
                if (v == null) continue;
                v.head = MainCamera.instance.transform;
            }

            ApplyPlayerVisibility(false);
        }
        else if (Input.GetKey(ModConfig.toggleCinemaCamControlKey.Value))
        {
            HoldTimer -= Time.deltaTime;
            if (HoldTimer <= 0)
            {
                MoveCameraToPlayerPosition(__instance);
                if (!__instance.on) __instance.on = true;
                HoldTimer = -1;
            }
        }
        else if (Input.GetKeyUp(ModConfig.toggleCinemaCamControlKey.Value))
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
            // Disable player controls
            InputSystem.actions.Disable();
            HUD?.SetActive(false);

            ApplyPlayerVisibility(true);

            __instance.ambience.parent = __instance.transform;
            if ((bool)__instance.fog)
                __instance.fog.gameObject.SetActive(false);

            if ((bool)__instance.oldCam)
                __instance.oldCam.gameObject.SetActive(false);

            __instance.transform.parent = null;
            __instance.cam.parent = null;

            if (!CameraWasSpawned)
                MoveCameraToPlayerPosition(__instance);

            __instance.cam.gameObject.SetActive(true);

            // FOV
            if (CinemaCamComponent != null)
            {
                float fovChange = Input.GetAxis("Mouse ScrollWheel");
                if (fovChange != 0)
                {
                    CinemaCamComponent.fieldOfView -= fovChange * ModConfig.fovSensitivity.Value;
                    CinemaCamComponent.fieldOfView = Mathf.Clamp(CinemaCamComponent.fieldOfView, 5f, 120f);
                }

                if (Input.GetKeyDown(ModConfig.keyFOVReset.Value))
                    CinemaCamComponent.fieldOfView = ModConfig.defaultFOV.Value;
            }

            if (Input.GetKeyDown(ModConfig.keySmoothToggle.Value))
                Smoothing = !Smoothing;

            float speed = 0;
            float normalMoveSpeed = ModConfig.normalSpeed.Value;
            float fastMoveSpeed = ModConfig.fastSpeed.Value;
            float rotSensitivity = ModConfig.rotationSensitivity.Value;

            // Rotation sensitivity scaling with FOV
            float fov = CinemaCamComponent != null ? CinemaCamComponent.fieldOfView : 60f;
            float fovScale = Mathf.Pow(60f / Mathf.Clamp(fov, 5f, 180f), 0.1f);
            float adjustedRotSensitivity = rotSensitivity * fovScale;

            if (Smoothing)
            {
                __instance.vel = Vector3.Lerp(__instance.vel, Vector3.zero, ModConfig.velLerpValue.Value * Time.deltaTime);
                __instance.rot = Vector3.Lerp(__instance.rot, Vector3.zero, ModConfig.rotLerpValue.Value * Time.deltaTime);

                speed = Input.GetKey(ModConfig.keyMoveFaster.Value)
                    ? (fastMoveSpeed / 10f)
                    : (normalMoveSpeed / 10f);

                __instance.rot.y += Input.GetAxis("Mouse X") * speed * adjustedRotSensitivity * 0.05f;
                __instance.rot.x += Input.GetAxis("Mouse Y") * speed * adjustedRotSensitivity * 0.05f;
            }
            else
            {
                __instance.vel = Vector3.zero;
                __instance.rot = Vector3.zero;

                speed = Input.GetKey(ModConfig.keyMoveFaster.Value)
                    ? fastMoveSpeed
                    : normalMoveSpeed;

                __instance.rot.y += Input.GetAxis("Mouse X") * adjustedRotSensitivity * 0.1f;
                __instance.rot.x += Input.GetAxis("Mouse Y") * adjustedRotSensitivity * 0.1f;
            }

            float adjustedSpeed = speed * Time.deltaTime;
            if (Input.GetKey(ModConfig.keyMoveRight.Value))
                __instance.vel.x = Smoothing ? __instance.vel.x + adjustedSpeed : adjustedSpeed;

            if (Input.GetKey(ModConfig.keyMoveLeft.Value))
                __instance.vel.x = Smoothing ? __instance.vel.x - adjustedSpeed : -adjustedSpeed;

            if (Input.GetKey(ModConfig.keyMoveForward.Value))
                __instance.vel.z = Smoothing ? __instance.vel.z + adjustedSpeed : adjustedSpeed;

            if (Input.GetKey(ModConfig.keyMoveBackward.Value))
                __instance.vel.z = Smoothing ? __instance.vel.z - adjustedSpeed : -adjustedSpeed;

            if (Input.GetKey(ModConfig.keyMoveUp.Value))
                __instance.vel.y = Smoothing ? __instance.vel.y + adjustedSpeed : adjustedSpeed;

            if (Input.GetKey(ModConfig.keyMoveDown.Value))
                __instance.vel.y = Smoothing ? __instance.vel.y - adjustedSpeed : -adjustedSpeed;

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
            InputSystem.actions.Enable();
            ApplyPlayerVisibility(false);
        }

        return false;
    }

    private static void HandlePlayerVisibilityInput()
    {
        if (Input.GetKeyDown(ModConfig.keyTogglePlayer.Value))
        {
            PlayerVisibilityToggled = !PlayerVisibilityToggled;
            Log.LogInfo($"Player visibility set to {PlayerVisibilityToggled}");
        }
    }

    /// <summary>
    /// Player visibility renderer
    /// </summary>
    private static void ApplyPlayerVisibility(bool cameraActive)
    {
        Character localCharacter = Character.AllCharacters.FirstOrDefault(c => c.IsLocal);
        CharacterCustomization customization = localCharacter?.refs?.customization;

        if (customization == null) return;

        bool shouldBeHidden = cameraActive && PlayerVisibilityToggled;

        if (shouldBeHidden)
        {
            customization.HideAllRenderers();
        }
        else
        {
            customization.ShowAllRenderers();
        }
    }

    private static void MoveCameraToPlayerPosition(CinemaCamera __instance)
    {
        Character localCharacter = Character.AllCharacters.FirstOrDefault(c => c.IsLocal);
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
        public readonly ConfigEntry<KeyCode> keyTogglePlayer;

        public readonly ConfigEntry<KeyCode> keyMoveForward;
        public readonly ConfigEntry<KeyCode> keyMoveBackward;
        public readonly ConfigEntry<KeyCode> keyMoveLeft;
        public readonly ConfigEntry<KeyCode> keyMoveRight;

        public readonly ConfigEntry<KeyCode> keyMoveUp;
        public readonly ConfigEntry<KeyCode> keyMoveDown;
        public readonly ConfigEntry<KeyCode> keyMoveFaster;
        public readonly ConfigEntry<KeyCode> keySmoothToggle;
        public readonly ConfigEntry<float> velLerpValue;
        public readonly ConfigEntry<float> rotLerpValue;

        public readonly ConfigEntry<float> normalSpeed;
        public readonly ConfigEntry<float> fastSpeed;
        public readonly ConfigEntry<float> rotationSensitivity;

        public readonly ConfigEntry<KeyCode> keyFOVReset;
        public readonly ConfigEntry<float> defaultFOV;
        public readonly ConfigEntry<float> fovSensitivity;

        public PluginModConfig(ConfigFile config)
        {
            // General
            toggleCinemaCamControlKey = config.Bind<KeyCode>("General", "Toggle Cinema Cam Control", KeyCode.F3, "Hold F3 to reset camera position to player, press F3 to toggle on/off.");
            exitCinemaCamKey = config.Bind<KeyCode>("General", "Exit Cinema Cam", KeyCode.Escape, "Exits the cinema camera and re-enables player input.");
            keyTogglePlayer = config.Bind<KeyCode>("General", "Toggle Player Visibility", KeyCode.F4, "Toggles your character model (body/head/cosmetics) on/off.");

            // Movement
            keyMoveFaster = config.Bind<KeyCode>("Movement", "Move Faster", KeyCode.LeftShift, "Hold to move at the fast speed setting.");
            keyMoveUp = config.Bind<KeyCode>("Movement", "Move Up", KeyCode.Space, "Camera movement up key.");
            keyMoveDown = config.Bind<KeyCode>("Movement", "Move Down", KeyCode.LeftControl, "Camera movement down key.");

            keyMoveForward = config.Bind<KeyCode>("Movement", "Move Forward", KeyCode.W, "Camera movement forward key.");
            keyMoveLeft = config.Bind<KeyCode>("Movement", "Move Left", KeyCode.A, "Camera movement left key.");
            keyMoveBackward = config.Bind<KeyCode>("Movement", "Move Backward", KeyCode.S, "Camera movement backward key.");
            keyMoveRight = config.Bind<KeyCode>("Movement", "Move Right", KeyCode.D, "Camera movement right key.");

            // Camera
            keySmoothToggle = config.Bind<KeyCode>("Camera", "Toggle Camera Smoothing", KeyCode.CapsLock, "Toggles velocity damping and movement smoothing.");
            normalSpeed = config.Bind<float>("Camera", "Normal Speed", 2f, "The default movement speed of the camera.");
            fastSpeed = config.Bind<float>("Camera", "Fast Speed", 6f, "The movement speed when holding the 'Move Faster' key.");
            rotationSensitivity = config.Bind<float>("Camera", "Rotation Sensitivity", 1.5f, "Mouse sensitivity for looking around.");
            velLerpValue = config.Bind<float>("Camera", "Smoothing Velocity", 2f, "Control how quickly the camera stops moving.");
            rotLerpValue = config.Bind<float>("Camera", "Smoothing Rotation", 3f, "Control how quickly the camera stops rotating.");

            // FOV
            keyFOVReset = config.Bind<KeyCode>("FOV", "Reset FOV", KeyCode.Mouse2, "Key to reset the Field of View to the default value.");
            defaultFOV = config.Bind<float>("FOV", "Default FOV", 60f, "The default Field of View value (used for reset).");
            fovSensitivity = config.Bind<float>("FOV", "FOV Scroll Sensitivity", 15f, "Multiplier for how fast the FOV changes when scrolling the mouse wheel.");
        }
    }
}