using System;

namespace MathMLToCSharpLib.Entities
{
    [Serializable]
    class Mstyle : WithBuildableContents
    {
        public Mstyle() { }
        public Mstyle(IBuildable content) : base(new[] { content }) { }
        public Mstyle(IBuildable[] contents) : base(contents) { }
    }
}
