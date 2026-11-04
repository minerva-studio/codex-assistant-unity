using UnityEditor;

namespace Minerva.Codex.Editor.Dev
{
    /// <summary>Opens the assistant window for a normal (non-batch) editor launch.</summary>
    public static class CodexWindowLauncher
    {
        public static void Open()
        {
            CodexAssistantWindow.Open();
        }

        [MenuItem("Tools/Minerva/Codex/Open Assistant Window")]
        static void OpenFromMenu()
        {
            Open();
        }
    }
}
