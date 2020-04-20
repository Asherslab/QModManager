﻿namespace QModManager.Utility
{
    using System.Reflection;
    using System.Reflection.Emit;
    using System.Collections;
    using System.Collections.Generic;

    using Harmony;
    using UnityEngine;
    using UnityEngine.SceneManagement;

    /// <summary>TODO</summary>
    public static class MainMenuMessages
    {
        private const int defaultSize = 25;
        private const string defaultColor = "red";

        private static List<string> messageQueue;
        private static List<ErrorMessage._Message> messages;

        /// <summary>TODO</summary>
        public static void Add(string msg, int size = defaultSize, string color = defaultColor, bool autoformat = true)
        {
            if (Patches.hInstance == null) // just in case
            {
                Logger.Error($"MainMenuMessages: trying to add message before Harmony initialized ({msg})");
                return;
            }

            Init();

            Logger.Debug($"MainMenuMessages: message added {msg}");

            if (autoformat)
            {
                Assembly callingAssembly = ReflectionHelper.CallingAssemblyByStackTrace(true);

                string prefix;
                if (callingAssembly != Assembly.GetExecutingAssembly())
                    prefix = callingAssembly.GetName().Name;
                else
                    prefix = "QModManager"; // or maybe just remove prefix if it's QMM ?

                msg = $"<size={size}><color={color}><b>[{prefix}]:</b> {msg}</color></size>";
            }

            if (ErrorMessage.main != null)
                AddInternal(msg);
            else
                messageQueue.Add(msg);
        }

        private static void Init()
        {
            if (messageQueue != null)
                return;

            messageQueue = new List<string>();
            messages = new List<ErrorMessage._Message>();
            Patches.Patch();

            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void AddInternal(string msg)
        {
            ErrorMessage.AddDebug(msg);

            var message = ErrorMessage.main.GetExistingMessage(msg);
            messages.Add(message);
            message.timeEnd += 1e6f;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode _)
        {
            if (scene.name != "Main")
                return;

            SceneManager.sceneLoaded -= OnSceneLoaded;
            ErrorMessage.main.StartCoroutine(_waitForLoad());

            static IEnumerator _waitForLoad()
            {
                yield return new WaitForSeconds(1f);

                while (SaveLoadManager.main.isLoading)
                    yield return null;

                foreach (var msg in messages)
                    msg.timeEnd = Time.time + 1f;

                messages.Clear();
                Patches.Unpatch();
            }
        }

        private static class Patches
        {
            public static HarmonyInstance hInstance { get; private set; }

            public static void Patch()
            {
                Logger.Debug("MainMenuMessages: patching");

                // patching it only if we need to (transpilers take time)
                hInstance.Patch(AccessTools.Method(typeof(ErrorMessage), nameof(ErrorMessage.OnUpdate)),
                    transpiler: new HarmonyMethod(AccessTools.Method(typeof(Patches), nameof(UpdateMessages))));
            }

            public static void Unpatch()
            {
                Logger.Debug("MainMenuMessages: unpatching");

                hInstance.Unpatch(AccessTools.Method(typeof(ErrorMessage), nameof(ErrorMessage.Awake)),
                    AccessTools.Method(typeof(AddMessages), nameof(AddMessages.Postfix)));

                hInstance.Unpatch(AccessTools.Method(typeof(ErrorMessage), nameof(ErrorMessage.OnUpdate)),
                    AccessTools.Method(typeof(Patches), nameof(UpdateMessages)));
            }

            [HarmonyPatch(typeof(ErrorMessage), nameof(ErrorMessage.Awake))]
            private static class AddMessages
            {
                // workaround to get harmony instance
                private static MethodBase TargetMethod(HarmonyInstance instance)
                {
                    hInstance = instance;
                    return null; // using target method from attribute
                }

                public static void Postfix()
                {
                    Logger.Debug($"MainMenuMessages: ErrorMessage created");

                    messageQueue.ForEach(msg => AddInternal(msg));
                    messageQueue.Clear();
                }
            }

            private static float _getVal(float val, ErrorMessage._Message message) => messages.Contains(message)? 1f: val;

            // we changing result for 'float value = Mathf.Clamp01(MathExtensions.EvaluateLine(...' to 1.0f
            // so text don't stay in the center of the screen (because of changed 'timeEnd')
            private static IEnumerable<CodeInstruction> UpdateMessages(IEnumerable<CodeInstruction> cins)
            {
                var list = new List<CodeInstruction>(cins);
                int index = list.FindIndex(cin => cin.opcode == OpCodes.Stloc_S && (cin.operand as LocalBuilder)?.LocalIndex == 11) + 1;

                list.InsertRange(index, new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldloc_S, 11),
                    new CodeInstruction(OpCodes.Ldloc_S, 6),
                    new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Patches), nameof(_getVal))),
                    new CodeInstruction(OpCodes.Stloc_S, 11)
                });

                return list;
            }
        }
    }
}
