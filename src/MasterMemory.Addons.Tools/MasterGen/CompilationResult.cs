using System.Collections.Generic;
using System.Reflection;

namespace MasterMemory.Addons.Tools.MasterGen;

/// <summary>生成ソースをコンパイルした結果。失敗したら <see cref="Assembly"/> は null で、理由が <see cref="Errors"/> に入る。</summary>
internal sealed record CompilationResult(Assembly? Assembly, IReadOnlyList<string> Errors);
