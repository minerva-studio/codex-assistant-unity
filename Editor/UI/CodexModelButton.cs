using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Minerva.Codex
{
    /// <summary>Collapsed model entry: optional Fast lightning, model name, colored effort, and chevron.</summary>
    internal sealed class CodexModelButton : Button
    {
        readonly CodexWorkspace m_Workspace;
        readonly VisualElement m_Fast;
        readonly Label m_Model;
        readonly Label m_Effort;
        readonly VisualElement m_Chevron;

        public CodexModelButton(CodexWorkspace workspace, Action openControl) : base(openControl)
        {
            m_Workspace = workspace;
            name = "codex-model";
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            style.height = 30;
            style.marginLeft = 4;
            style.paddingLeft = style.paddingRight = 6;
            style.backgroundColor = Color.clear;
            style.borderTopWidth = style.borderBottomWidth = 0;
            style.borderLeftWidth = style.borderRightWidth = 0;
            style.borderTopLeftRadius = style.borderTopRightRadius = 6;
            style.borderBottomLeftRadius = style.borderBottomRightRadius = 6;
            RegisterCallback<MouseEnterEvent>(_ => style.backgroundColor = CodexPalette.PillHover);
            RegisterCallback<MouseLeaveEvent>(_ => style.backgroundColor = Color.clear);
            m_Fast = new VisualElement { name = "codex-model-fast", pickingMode = PickingMode.Ignore };
            m_Fast.style.width = m_Fast.style.height = 24;
            m_Fast.style.scale = new Scale(new Vector3(.7f, .7f, 1));
            m_Fast.generateVisualContent += context =>
            {
                var painter = context.painter2D;
                painter.lineWidth = 1.2f;
                painter.fillColor = painter.strokeColor = CodexPalette.Text;
                CodexModelControl.DrawFastIcon(painter, true);
            };
            Add(m_Fast);
            m_Model = new Label { name = "codex-model-name", enableRichText = false, pickingMode = PickingMode.Ignore };
            m_Model.style.color = CodexPalette.Text;
            m_Model.style.fontSize = 11;
            m_Model.style.marginLeft = m_Model.style.marginRight = 0;
            Add(m_Model);
            m_Effort = new Label { name = "codex-model-effort", enableRichText = false, pickingMode = PickingMode.Ignore };
            m_Effort.style.fontSize = 11;
            m_Effort.style.marginLeft = 4;
            m_Effort.style.marginRight = 4;
            Add(m_Effort);
            m_Chevron = new VisualElement { pickingMode = PickingMode.Ignore };
            m_Chevron.style.width = 10;
            m_Chevron.style.height = 12;
            m_Chevron.generateVisualContent += context =>
            {
                var painter = context.painter2D;
                painter.lineWidth = 1.2f;
                painter.strokeColor = m_Chevron.resolvedStyle.color;
                painter.BeginPath();
                painter.MoveTo(new Vector2(2, 4));
                painter.LineTo(new Vector2(5, 7));
                painter.LineTo(new Vector2(8, 4));
                painter.Stroke();
            };
            Add(m_Chevron);
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                workspace.SettingsChanged += Refresh;
                workspace.Changed += Refresh;
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
            var model = m_Workspace.Models.FirstOrDefault(candidate => candidate.Matches(m_Workspace.SelectedModel));
            m_Model.text = model?.DisplayName ?? m_Workspace.SelectedModel ?? "Model";
            m_Effort.text = CodexModelControl.EffortName(m_Workspace.SelectedEffort);
            Color color = CodexModelControl.EffortColor(m_Workspace.SelectedEffort);
            m_Effort.style.color = m_Chevron.style.color = color;
            m_Chevron.MarkDirtyRepaint();
            m_Fast.style.display = m_Workspace.FastMode ? DisplayStyle.Flex : DisplayStyle.None;
            tooltip = "Model: " + m_Model.text + " · reasoning effort: " + m_Effort.text
                + " · Fast mode: " + (m_Workspace.FastMode ? "on" : "off");
            SetEnabled(m_Workspace.SettingsLoaded && !m_Workspace.IsRunning
                && (m_Workspace.Models.Count > 0 || !string.IsNullOrEmpty(m_Workspace.SelectedModel)));
        }
    }
}
