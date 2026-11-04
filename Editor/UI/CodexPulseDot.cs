using UnityEngine;
using UnityEngine.UIElements;

namespace Minerva.Codex
{
    /// <summary>A small pulsing status dot for running work.</summary>
    internal sealed class CodexPulseDot : VisualElement
    {
        readonly IVisualElementScheduledItem m_Animation;
        bool m_On;

        public CodexPulseDot(Color color, float size = 7f, bool animate = true)
        {
            style.width = size;
            style.height = size;
            style.flexShrink = 0;
            style.borderTopLeftRadius = size * 0.5f;
            style.borderTopRightRadius = size * 0.5f;
            style.borderBottomLeftRadius = size * 0.5f;
            style.borderBottomRightRadius = size * 0.5f;
            style.backgroundColor = color;

            if (!animate)
            {
                return;
            }

            m_Animation = schedule.Execute(Tick).Every(550);
            RegisterCallback<AttachToPanelEvent>(_ => m_Animation.Resume());
            RegisterCallback<DetachFromPanelEvent>(_ => m_Animation.Pause());
        }

        void Tick()
        {
            m_On = !m_On;
            style.opacity = m_On ? 1f : 0.3f;
        }
    }
}
