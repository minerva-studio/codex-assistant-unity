using UnityEditor;
using UnityEngine;

namespace Minerva.Codex
{
    /// <summary>Colors and radii shared by the Codex window.</summary>
    internal static class CodexPalette
    {
        public static bool Dark => EditorGUIUtility.isProSkin;

        public static Color Window => Dark ? Hex(0x15171b) : Hex(0xf4f5f8);
        public static Color Sidebar => Dark ? Hex(0x101216) : Hex(0xebeef4);
        public static Color Panel => Dark ? Hex(0x191c21) : Hex(0xffffff);
        public static Color Surface => Dark ? Hex(0x1d2127) : Hex(0xffffff);
        public static Color SurfaceRaised => Dark ? Hex(0x252a32) : Hex(0xf7f8fb);
        public static Color Hover => Dark ? Hex(0x20252d) : Hex(0xeef1f6);
        public static Color Selected => Dark ? Hex(0x293446) : Hex(0xe1eafd);

        public static Color UserBubble => Dark ? Hex(0x223247) : Hex(0xe7f0ff);
        public static Color AgentBubble => Dark ? Hex(0x1d2127) : Hex(0xffffff);
        public static Color ToolBubble => Dark ? Hex(0x1a1e24) : Hex(0xf4f6f9);
        public static Color CodeBackground => Dark ? Hex(0x0e1013) : Hex(0xf0f2f6);

        public static Color Text => Dark ? Hex(0xe7eaf0) : Hex(0x202531);
        public static Color Muted => Dark ? Hex(0x8b93a1) : Hex(0x697181);
        public static Color TextDim => Dark ? Hex(0x6f7785) : Hex(0x8a92a2);

        public static Color ComposerCard => Dark ? Hex(0x1c2026) : Hex(0xffffff);
        public static Color Popover => Dark ? Hex(0x1c2026) : Hex(0xffffff);
        public static Color PopoverHover => Dark ? Hex(0x262c35) : Hex(0xf0f3f8);
        public static Color Pill => Dark ? Hex(0x252b34) : Hex(0xeef1f6);
        public static Color PillHover => Dark ? Hex(0x313947) : Hex(0xe0e5ee);
        public static Color PillBorder => Dark ? Hex(0x343c48) : Hex(0xd6dce6);

        public static Color Accent => Dark ? Hex(0x6c9bff) : Hex(0x2f67e8);
        public static Color AccentSoft => Dark ? Hex(0x26344d) : Hex(0xe3ebfd);
        public static Color SendAction => Dark ? Hex(0x4f7df3) : Hex(0x2f67e8);
        public static Color StopAction => Dark ? Hex(0xc25454) : Hex(0xc63e3e);
        public static Color DisabledAction => Dark ? Hex(0x2a2f37) : Hex(0xdde2ea);

        public static Color Warning => Dark ? Hex(0xe0b15c) : Hex(0x9a6700);
        public static Color Success => Dark ? Hex(0x5ecf8b) : Hex(0x1a7f4b);
        public static Color Error => Dark ? Hex(0xef767a) : Hex(0xc0393f);

        public static Color Border => Dark ? Hex(0x2a3039) : Hex(0xdce1e9);
        public static Color BorderStrong => Dark ? Hex(0x3a4350) : Hex(0xc6cede);

        static Color Hex(int rgb)
        {
            return new Color(
                ((rgb >> 16) & 0xff) / 255f,
                ((rgb >> 8) & 0xff) / 255f,
                (rgb & 0xff) / 255f,
                1f);
        }
    }
}
