using System;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Minerva.Codex.Editor.Dev
{
    /// <summary>Checks speed selection and the actual switch without starting an inference turn.</summary>
    internal static class CodexFastModeSmokeTest
    {
        const BindingFlags Methods = BindingFlags.Instance | BindingFlags.NonPublic;

        [MenuItem("Tools/Minerva/Codex/Check Fast Mode")]
        public static string Run()
        {
            var models = new JArray(Model("fast-model", true), Model("plain-model", false), Model("catalog-default", true));
            var levels = new JArray();
            foreach (string level in new[] { "low", "medium", "high", "xhigh", "max", "ultra" })
                levels.Add(new JObject { ["reasoningEffort"] = level });
            models[0]["supportedReasoningEfforts"] = levels;
            models[2]["defaultServiceTier"] = "priority";
            var workspace = new CodexWorkspace();
            Load(workspace, models, "fast-model", null);
            Require(!workspace.FastMode && workspace.FastTier.Id == "priority", "advertised tier id determines Fast capability");
            workspace.SetFastMode(true);
            Require(workspace.FastMode && workspace.SelectedServiceTier == "priority" && workspace.SelectedEffort == "high",
                "Fast changes the speed tier without changing reasoning effort");
            workspace.SetEffort("ultra");
            Require(workspace.FastMode && workspace.SelectedEffort == "ultra", "reasoning effort remains independent");

            var host = ScriptableObject.CreateInstance<EditorWindow>();
            try
            {
                bool modelPickerOpened = false;
                var card = new CodexModelControl(workspace, () => modelPickerOpened = true);
                host.rootVisualElement.Add(card);
                host.ShowUtility();
                var toggle = card.Q<Button>("codex-fast-switch");
                var slider = card.Q<SliderInt>("codex-reasoning-slider");
                Require(slider.highValue == 5 && slider.value == 5 && card.Q("codex-reasoning-ticks").childCount == 6,
                    "reasoning slider uses the model's six advertised levels");
                Submit(toggle);
                Require(!workspace.FastMode && workspace.SelectedServiceTier == "default"
                    && !toggle.ClassListContains("codex-fast-enabled") && workspace.SelectedEffort == "ultra",
                    "Fast icon explicitly selects standard without changing reasoning");
                slider.value = 3;
                Require(workspace.SelectedEffort == "xhigh" && !workspace.FastMode, "reasoning slider changes effort without changing speed");
                Submit(card.Q<Button>("codex-reasoning-reset"));
                Require(workspace.SelectedEffort == "high" && slider.value == 2 && !workspace.FastMode,
                    "reset restores the model's default reasoning level without changing speed");
                Submit(card.Q<Button>("codex-control-model"));
                Require(modelPickerOpened, "model button opens the model selector");
                Submit(toggle);
                Require(workspace.FastMode && toggle.ClassListContains("codex-fast-enabled"), "Fast icon enables advertised speed");
                workspace.SetEffort("ultra");
                workspace.SetModel("plain-model");
                Require(!workspace.FastMode && workspace.FastTier == null && !toggle.enabledSelf,
                    "changing to an unsupported model selects standard and disables the switch");
                Require(workspace.SelectedEffort == "high" && slider.highValue == 1 && slider.value == 0,
                    "shrinking the model's reasoning range does not overwrite its default with a clamped old index");
                workspace.SetFastMode(true);
                Require(!workspace.FastMode, "unsupported models cannot enable Fast");
                workspace.SetModel("fast-model");
                typeof(CodexWorkspace).GetProperty("SelectedThreadId").SetValue(workspace, "thread-one");
                Invoke(workspace, "ApplyThreadSettings", "thread-one", new JObject
                {
                    ["model"] = "fast-model", ["serviceTier"] = "priority", ["reasoningEffort"] = "high",
                });
                Require(workspace.FastMode && toggle.ClassListContains("codex-fast-enabled"), "thread resume restores speed and refreshes the icon");
                Invoke(workspace, "OnThreadSettingsUpdated", new JObject
                {
                    ["threadId"] = "thread-one", ["threadSettings"] = new JObject
                    {
                        ["model"] = "fast-model", ["serviceTier"] = "default", ["effort"] = JValue.CreateNull(),
                    },
                });
                Require(!workspace.FastMode, "speed-only settings notifications apply even with null effort");
                Invoke(workspace, "OnThreadSettingsUpdated", new JObject
                {
                    ["threadId"] = "other-thread", ["threadSettings"] = new JObject { ["serviceTier"] = "priority" },
                });
                Require(!workspace.FastMode, "another thread cannot overwrite this selection");
                workspace.SetFastMode(true);
                Invoke(workspace, "ApplyThreadSettings", "thread-one", new JObject
                {
                    ["model"] = "fast-model", ["serviceTier"] = JValue.CreateNull(),
                });
                Require(!workspace.FastMode, "a resumed null tier clears the previous thread's Fast setting");
            }
            finally { host.Close(); }

            var inherited = new CodexWorkspace();
            Load(inherited, models, "catalog-default", null);
            Require(inherited.FastMode, "catalog default participates when config has no override");
            inherited.SetFastMode(false);
            Require(inherited.SelectedServiceTier == "default" && !inherited.FastMode, "Off overrides a Fast catalog default");
            var configured = new CodexWorkspace();
            Load(configured, models, "fast-model", "fast");
            Require(configured.FastMode, "config fast alias is reflected without changing its tier value");
            typeof(CodexWorkspace).GetProperty("IsRunning").SetValue(configured, true);
            configured.SetFastMode(false);
            Require(configured.FastMode, "speed cannot change during an active turn");
            return "PASS: model control card, discrete reasoning slider/reset, independent Fast icon, model-range changes, config/defaults, resume/notification sync and active-turn guard";
        }

        static JObject Model(string id, bool fast) => new JObject
        {
            ["id"] = id, ["model"] = id, ["displayName"] = id, ["defaultReasoningEffort"] = "high",
            ["supportedReasoningEfforts"] = new JArray(new JObject { ["reasoningEffort"] = "high" }, new JObject { ["reasoningEffort"] = "ultra" }),
            ["serviceTiers"] = fast ? new JArray(new JObject { ["id"] = "priority", ["name"] = "Fast", ["description"] = "Faster responses, increased usage" }) : new JArray(),
        };

        static void Load(CodexWorkspace workspace, JArray models, string model, string tier)
        {
            Invoke(workspace, "OnModelsLoaded", models);
            Invoke(workspace, "OnConfigLoaded", new JObject
            {
                ["model"] = model, ["model_reasoning_effort"] = "high", ["service_tier"] = tier,
            });
        }

        static void Invoke(CodexWorkspace workspace, string method, params object[] args) =>
            typeof(CodexWorkspace).GetMethod(method, Methods).Invoke(workspace, args);

        static void Submit(Button button)
        {
            using var evt = NavigationSubmitEvent.GetPooled();
            evt.target = button;
            button.SendEvent(evt);
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Fast mode smoke: " + message);
        }
    }
}
