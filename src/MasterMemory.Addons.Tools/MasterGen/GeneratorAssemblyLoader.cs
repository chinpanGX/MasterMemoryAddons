using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;

namespace MasterMemory.Addons.Tools.MasterGen;

/// <summary>Source Generator の DLL を既定のコンテキストに読み込む(Roslyn は Tools と同じものを使う)。</summary>
internal sealed class GeneratorAssemblyLoader : IAnalyzerAssemblyLoader
{
    public void AddDependencyLocation(string fullPath)
    {
    }

    public Assembly LoadFromPath(string fullPath)
    {
        return AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
    }
}
