using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PeakCinema;

[BepInAutoPlugin]
public partial class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; } = null!;

    private void Awake()
    {
        Log = Logger;

        Log.LogInfo($"Plugin {Name} is loaded!");

        Harmony.CreateAndPatchAll(typeof(Plugin));
    }

    [HarmonyPatch(typeof(CinemaCamera), "Update")]
    [HarmonyPrefix]
    static bool CinemaCameraFix(CinemaCamera __instance)
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            // Let the player move again
            InputSystem.actions.Enable();

            if (__instance.on)
            {
                __instance.on = false;

                __instance.cam.gameObject.SetActive(false);

                if (__instance.fog != null)
                {
                    __instance.fog.gameObject.SetActive(true);
                }

                if (__instance.oldCam != null)
                {
                    __instance.oldCam.gameObject.SetActive(true);
                }
            }

            //// Skip rest of Update method
            return false;
        }

        if (Input.GetKey(KeyCode.C) && Input.GetKeyDown(KeyCode.M))
        {
            __instance.on = true;
        }

        if (__instance.on)
        {
            // Keep player from moving around
            InputSystem.actions.Disable();

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
            __instance.cam.gameObject.SetActive(value: true);
            __instance.vel = Vector3.Lerp(__instance.vel, Vector3.zero, 1f * Time.deltaTime);
            __instance.rot = Vector3.Lerp(__instance.rot, Vector3.zero, 2.5f * Time.deltaTime);
            float num = 0.05f;
            __instance.rot.y += Input.GetAxis("Mouse X") * num * 0.05f;
            __instance.rot.x += Input.GetAxis("Mouse Y") * num * 0.05f;
            if (Input.GetKey(KeyCode.D))
            {
                __instance.vel.x += num * Time.deltaTime;
            }
            if (Input.GetKey(KeyCode.A))
            {
                __instance.vel.x -= num * Time.deltaTime;
            }
            if (Input.GetKey(KeyCode.W))
            {
                __instance.vel.z += num * Time.deltaTime;
            }
            if (Input.GetKey(KeyCode.S))
            {
                __instance.vel.z -= num * Time.deltaTime;
            }
            if (Input.GetKey(KeyCode.Space))
            {
                __instance.vel.y += num * Time.deltaTime;
            }
            if (Input.GetKey(KeyCode.LeftControl))
            {
                __instance.vel.y -= num * Time.deltaTime;
            }
            if (Input.GetKey(KeyCode.CapsLock))
            {
                __instance.vel = Vector3.Lerp(__instance.vel, Vector3.zero, 5f * Time.deltaTime);
            }
            __instance.cam.transform.Rotate(Vector3.up * __instance.rot.y, Space.World);
            __instance.cam.transform.Rotate(__instance.transform.right * (0f - __instance.rot.x));
            __instance.cam.transform.Translate(Vector3.right * __instance.vel.x, Space.Self);
            __instance.cam.transform.Translate(Vector3.forward * __instance.vel.z, Space.Self);
            __instance.cam.transform.Translate(Vector3.up * __instance.vel.y, Space.World);
            __instance.t = true;
        }

        return false;
    }
}
