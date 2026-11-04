using UnityEditor;
using UnityEngine;

namespace Minerva.Codex
{
    internal static class CodexPalette
    {
        public static bool Dark => EditorGUIUtility.isProSkin;

        public static Color Window => Dark ? new Color(0.21f, 0.21f, 0.21f) : new Color(0.78f, 0.78f, 0.78f);
        public static Color Sidebar => Dark ? new Color(0.185f, 0.185f, 0.185f) : new Color(0.74f, 0.74f, 0.74f);
        public static Color Selected => Dark ? new Color(0.25f, 0.35f, 0.47f) : new Color(0.70f, 0.80f, 0.92f);
        public static Color UserBubble => Dark ? new Color(0.23f, 0.30f, 0.40f) : new Color(0.84f, 0.90f, 0.98f);
        public static Color AgentBubble => Dark ? new Color(0.235f, 0.235f, 0.235f) : new Color(0.92f, 0.92f, 0.92f);
        public static Color ToolBubble => Dark ? new Color(0.20f, 0.20f, 0.20f) : new Color(0.86f, 0.86f, 0.86f);
        public static Color CodeBackground => Dark ? new Color(0.14f, 0.14f, 0.14f) : new Color(0.95f, 0.95f, 0.95f);
        public static Color Text => Dark ? new Color(0.87f, 0.87f, 0.87f) : new Color(0.12f, 0.12f, 0.12f);
        public static Color Muted => Dark ? new Color(0.62f, 0.62f, 0.62f) : new Color(0.38f, 0.38f, 0.38f);
        public static Color ComposerCard => Dark ? new Color(0.22f, 0.22f, 0.22f) : new Color(0.98f, 0.98f, 0.98f);
        public static Color Popover => Dark ? new Color(0.17f, 0.17f, 0.17f) : new Color(0.99f, 0.99f, 0.99f);
        public static Color PopoverHover => Dark ? new Color(0.24f, 0.24f, 0.24f) : new Color(0.90f, 0.90f, 0.90f);
        public static Color Pill => Dark ? new Color(0.26f, 0.26f, 0.26f) : new Color(0.88f, 0.88f, 0.88f);
        public static Color PillHover => Dark ? new Color(0.31f, 0.31f, 0.31f) : new Color(0.82f, 0.82f, 0.82f);
        public static Color PillBorder => Dark ? new Color(0.34f, 0.34f, 0.34f) : new Color(0.72f, 0.72f, 0.72f);
        public static Color SendAction => Dark ? new Color(0.33f, 0.52f, 0.75f) : new Color(0.23f, 0.47f, 0.75f);
        public static Color StopAction => Dark ? new Color(0.62f, 0.25f, 0.25f) : new Color(0.72f, 0.20f, 0.20f);
        public static Color DisabledAction => Dark ? new Color(0.30f, 0.30f, 0.30f) : new Color(0.78f, 0.78f, 0.78f);
        public static Color Error => Dark ? new Color(0.95f, 0.45f, 0.45f) : new Color(0.65f, 0.10f, 0.10f);
        public static Color Success => Dark ? new Color(0.45f, 0.80f, 0.50f) : new Color(0.10f, 0.45f, 0.15f);
        public static Color Border => Dark ? new Color(0.115f, 0.115f, 0.115f) : new Color(0.55f, 0.55f, 0.55f);
    }
}
