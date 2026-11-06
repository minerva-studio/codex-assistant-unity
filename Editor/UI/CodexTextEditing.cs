using System;
using System.Reflection;
using UnityEngine.UIElements;

namespace Minerva.Codex
{
    /// <summary>Synchronizes visible and editing indices after programmatic field updates.</summary>
    internal static class CodexTextEditing
    {
        /// <summary>Moves selection and Unity text-editing indices to the end of the field.</summary>
        public static void CaretAtEnd(TextField field)
        {
            if (field == null)
            {
                return;
            }

            int end = field.value?.Length ?? 0;

            // Public path: updates the visible selection and the text editing
            // state together.
            field.SelectRange(end, end);

            try
            {
                object utilities = FindEditingUtilities(field.Q<TextElement>());
                if (utilities == null)
                {
                    return;
                }

                // The indices the keyboard handlers and the IME position read.
                // Set them without glyph-index validation, which clamps to 0 while
                // the element has not been laid out again.
                SetNonPublicProperty(utilities, "cursorIndexNoValidation", end);
                SetNonPublicProperty(utilities, "selectIndexNoValidation", end);
            }
            catch (Exception)
            {
                // Retain the public selection if Unity changes its private editing implementation.
            }
        }

        static object FindEditingUtilities(TextElement element)
        {
            if (element == null)
            {
                return null;
            }

            object manipulator = null;
            for (Type type = element.GetType(); type != null && manipulator == null; type = type.BaseType)
            {
                foreach (FieldInfo field in type.GetFields(
                             BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                             BindingFlags.DeclaredOnly))
                {
                    if (field.FieldType.Name == "TextEditingManipulator")
                    {
                        manipulator = field.GetValue(element);
                        break;
                    }
                }
            }

            return manipulator?
                .GetType()
                .GetField("editingUtilities", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(manipulator);
        }

        static void SetNonPublicProperty(object target, string name, object value)
        {
            target.GetType()
                .GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(target, value);
        }

    }
}
