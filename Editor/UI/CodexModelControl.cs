using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Minerva.Codex
{
    /// <summary>Model control card: Fast icon, current effort, model picker, and discrete reasoning slider.</summary>
    internal sealed class CodexModelControl : VisualElement
    {
        readonly CodexWorkspace m_Workspace;
        readonly Label m_Effort;
        readonly Button m_Fast;
        readonly Button m_Reset;
        readonly Button m_Model;
        readonly SliderInt m_Slider;
        readonly VisualElement m_Fill;
        readonly VisualElement m_Ticks;

        /// <param name="workspace">Owns model, effort, speed, and advertised capabilities.</param>
        /// <param name="selectModel">Opens the owning window's searchable model list.</param>
        public CodexModelControl(CodexWorkspace workspace, Action selectModel)
        {
            m_Workspace = workspace;
            name = "codex-model-control";
            style.height = 126;
            style.flexShrink = 0;
            style.paddingLeft = style.paddingRight = 16;
            style.paddingTop = 12;
            style.paddingBottom = 16;
            style.backgroundColor = CodexPalette.Dark ? new Color(.145f, .145f, .145f) : CodexPalette.Panel;
            style.borderTopLeftRadius = style.borderTopRightRadius = 20;
            style.borderBottomLeftRadius = style.borderBottomRightRadius = 20;

            var heading = new VisualElement();
            heading.style.flexDirection = FlexDirection.Row;
            heading.style.alignItems = Align.Center;
            heading.style.height = 24;
            Add(heading);
            m_Fast = IconButton("codex-fast-switch", () => workspace.SetFastMode(!workspace.FastMode), painter =>
            {
                painter.BeginPath();
                painter.MoveTo(new Vector2(13, 3));
                painter.LineTo(new Vector2(6, 13));
                painter.LineTo(new Vector2(11, 13));
                painter.LineTo(new Vector2(10, 21));
                painter.LineTo(new Vector2(19, 10));
                painter.LineTo(new Vector2(14, 10));
                painter.ClosePath();
                if (workspace.FastMode) painter.Fill();
                painter.Stroke();
            });
            heading.Add(m_Fast);
            m_Effort = new Label { name = "codex-reasoning-label", enableRichText = false };
            m_Effort.style.flexGrow = 1;
            m_Effort.style.unityTextAlign = TextAnchor.MiddleCenter;
            m_Effort.style.fontSize = 13;
            m_Effort.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.Add(m_Effort);
            m_Reset = IconButton("codex-reasoning-reset", () =>
            {
                var model = workspace.Models.FirstOrDefault(candidate => candidate.Matches(workspace.SelectedModel));
                if (!string.IsNullOrEmpty(model?.DefaultEffort)) workspace.SetEffort(model.DefaultEffort);
            }, painter =>
            {
                painter.BeginPath();
                for (int index = 0; index <= 12; index++)
                {
                    float angle = (index * 25 + 20) * Mathf.Deg2Rad;
                    var point = new Vector2(12 + Mathf.Cos(angle) * 8, 12 + Mathf.Sin(angle) * 8);
                    if (index == 0) painter.MoveTo(point); else painter.LineTo(point);
                }
                painter.Stroke();
                painter.BeginPath();
                painter.MoveTo(new Vector2(15, 4));
                painter.LineTo(new Vector2(20, 4));
                painter.LineTo(new Vector2(20, 9));
                painter.Stroke();
            });
            m_Reset.tooltip = "Restore the model's default reasoning effort";
            heading.Add(m_Reset);

            m_Model = new Button(selectModel) { name = "codex-control-model" };
            m_Model.style.height = 24;
            m_Model.style.marginBottom = 12;
            m_Model.style.backgroundColor = Color.clear;
            m_Model.style.color = CodexPalette.Muted;
            m_Model.style.borderTopWidth = m_Model.style.borderBottomWidth = 0;
            m_Model.style.borderLeftWidth = m_Model.style.borderRightWidth = 0;
            m_Model.style.whiteSpace = WhiteSpace.NoWrap;
            m_Model.style.overflow = Overflow.Hidden;
            m_Model.style.textOverflow = TextOverflow.Ellipsis;
            Add(m_Model);

            m_Slider = new SliderInt(0, 1) { name = "codex-reasoning-slider" };
            m_Slider.style.height = 32;
            m_Slider.style.flexShrink = 0;
            m_Slider.style.marginLeft = m_Slider.style.marginRight = 0;
            m_Slider.style.backgroundColor = CodexPalette.Dark ? new Color(.26f, .26f, .26f) : CodexPalette.PillBorder;
            m_Slider.style.borderTopLeftRadius = m_Slider.style.borderTopRightRadius = 16;
            m_Slider.style.borderBottomLeftRadius = m_Slider.style.borderBottomRightRadius = 16;
            m_Fill = new VisualElement { pickingMode = PickingMode.Ignore };
            m_Fill.style.position = Position.Absolute;
            m_Fill.style.left = m_Fill.style.top = m_Fill.style.bottom = 0;
            m_Fill.style.borderTopLeftRadius = m_Fill.style.borderTopRightRadius = 16;
            m_Fill.style.borderBottomLeftRadius = m_Fill.style.borderBottomRightRadius = 16;
            m_Slider.Insert(0, m_Fill);
            m_Ticks = new VisualElement { name = "codex-reasoning-ticks", pickingMode = PickingMode.Ignore };
            m_Ticks.style.position = Position.Absolute;
            m_Ticks.style.left = m_Ticks.style.right = 16;
            m_Ticks.style.top = m_Ticks.style.bottom = 0;
            m_Ticks.style.flexDirection = FlexDirection.Row;
            m_Ticks.style.justifyContent = Justify.SpaceBetween;
            m_Ticks.style.alignItems = Align.Center;
            m_Slider.Insert(1, m_Ticks);
            m_Slider.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue >= 0 && evt.newValue < workspace.Efforts.Count)
                    workspace.SetEffort(workspace.Efforts[evt.newValue]);
            });
            m_Slider.RegisterCallback<GeometryChangedEvent>(_ => Refresh());
            Add(m_Slider);
            StyleSlider();
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                workspace.SettingsChanged += Refresh;
                workspace.Changed += Refresh;
                StyleSlider();
                Refresh();
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                workspace.SettingsChanged -= Refresh;
                workspace.Changed -= Refresh;
            });
            Refresh();
        }

        void Refresh()
        {
            bool available = m_Workspace.SettingsLoaded && !m_Workspace.IsRunning;
            string effort = m_Workspace.SelectedEffort;
            Color accent = effort == "ultra" || effort == "max" ? new Color(.58f, .31f, 1f) : new Color(0, .46f, .9f);
            m_Effort.text = string.IsNullOrEmpty(effort) ? "Default" : char.ToUpperInvariant(effort[0]) + effort.Substring(1);
            m_Effort.style.color = accent;
            var model = m_Workspace.Models.FirstOrDefault(candidate => candidate.Matches(m_Workspace.SelectedModel));
            m_Model.text = (model?.DisplayName ?? m_Workspace.SelectedModel ?? "Model") + " ›";
            m_Model.SetEnabled(available && m_Workspace.Models.Count > 0);
            m_Fast.SetEnabled(available && (m_Workspace.FastTier != null || m_Workspace.FastMode));
            m_Fast.style.color = m_Workspace.FastMode ? accent : CodexPalette.Muted;
            m_Fast.tooltip = m_Workspace.FastTier == null && !m_Workspace.FastMode ? "Fast mode unavailable for this model"
                : "Fast mode: " + (m_Workspace.FastMode ? "on" : "off") + " · " + m_Workspace.FastTier?.Description;
            m_Fast.EnableInClassList("codex-fast-enabled", m_Workspace.FastMode);
            m_Fast.MarkDirtyRepaint();
            m_Reset.SetEnabled(available && !string.IsNullOrEmpty(model?.DefaultEffort) && effort != model.DefaultEffort);
            int count = m_Workspace.Efforts.Count;
            int selected = 0;
            for (int index = 0; index < count; index++) if (m_Workspace.Efforts[index] == effort) selected = index;
            // Reset silently before shrinking the range, so binding does not emit a user effort change.
            m_Slider.SetValueWithoutNotify(0);
            m_Slider.highValue = Math.Max(0, count - 1);
            m_Slider.SetValueWithoutNotify(selected);
            m_Slider.SetEnabled(available && count > 1);
            m_Slider.tooltip = "Reasoning effort: " + m_Effort.text;
            m_Fill.style.backgroundColor = accent;
            float fraction = count > 1 ? (float)selected / (count - 1) : 0;
            m_Fill.style.width = 16 + Math.Max(0, m_Slider.contentRect.width - 32) * fraction;
            if (m_Ticks.childCount != count)
            {
                m_Ticks.Clear();
                for (int index = 0; index < count; index++)
                {
                    var tick = new VisualElement { pickingMode = PickingMode.Ignore };
                    tick.style.width = tick.style.height = 5;
                    tick.style.borderTopLeftRadius = tick.style.borderTopRightRadius = 3;
                    tick.style.borderBottomLeftRadius = tick.style.borderBottomRightRadius = 3;
                    tick.style.backgroundColor = new Color(1, 1, 1, .28f);
                    m_Ticks.Add(tick);
                }
            }
        }

        void StyleSlider()
        {
            var input = m_Slider.Q(className: "unity-base-slider__input");
            if (input != null) { input.style.height = 32; input.style.marginLeft = input.style.marginRight = 0; }
            var tracker = m_Slider.Q(className: "unity-base-slider__tracker");
            if (tracker != null) tracker.style.backgroundColor = Color.clear;
            var dragger = m_Slider.Q(className: "unity-base-slider__dragger");
            if (dragger != null)
            {
                dragger.style.position = Position.Absolute;
                dragger.style.width = dragger.style.height = 32;
                dragger.style.flexShrink = 0;
                dragger.style.top = 0;
                dragger.style.marginTop = dragger.style.marginBottom = 0;
                dragger.style.backgroundColor = Color.white;
                dragger.style.borderTopLeftRadius = dragger.style.borderTopRightRadius = 16;
                dragger.style.borderBottomLeftRadius = dragger.style.borderBottomRightRadius = 16;
            }
            var border = m_Slider.Q(className: "unity-base-slider__dragger-border");
            if (border != null) border.style.display = DisplayStyle.None;
        }

        static Button IconButton(string name, Action action, Action<Painter2D> draw)
        {
            var button = new Button(action) { name = name };
            button.style.width = button.style.height = 24;
            button.style.flexShrink = 0;
            button.style.paddingLeft = button.style.paddingRight = 0;
            button.style.marginLeft = button.style.marginRight = 0;
            button.style.backgroundColor = Color.clear;
            button.style.color = CodexPalette.Muted;
            button.style.borderTopWidth = button.style.borderBottomWidth = 0;
            button.style.borderLeftWidth = button.style.borderRightWidth = 0;
            button.generateVisualContent += context =>
            {
                var painter = context.painter2D;
                painter.lineWidth = 1.4f;
                painter.strokeColor = painter.fillColor = button.resolvedStyle.color;
                draw(painter);
            };
            return button;
        }
    }
}
