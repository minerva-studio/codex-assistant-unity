namespace Minerva.Codex
{
    /// <summary>
    /// One enabled skill reported by the app-server for the current project.
    /// <see cref="Path"/> is the SKILL.md directory used by skill references.
    /// </summary>
    internal sealed class CodexSkillInfo
    {
        public string Name;
        public string Path;
        public string Description;
        public string ShortDescription;
    }
}
