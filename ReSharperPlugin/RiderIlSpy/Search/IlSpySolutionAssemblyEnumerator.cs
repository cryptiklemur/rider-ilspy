using System.Collections.Generic;
using JetBrains.ProjectModel;
using JetBrains.ProjectModel.Model2.Assemblies.Interfaces;
using JetBrains.ReSharper.Psi.Modules;
using JetBrains.Util;

namespace RiderIlSpy.Search;

public sealed class IlSpySolutionAssemblyEnumerator
{
    private readonly ISolution mySolution;

    public IlSpySolutionAssemblyEnumerator(ISolution solution) => mySolution = solution;

    public IEnumerable<string> EnumerateReferencedAssemblyPaths()
    {
        HashSet<string> seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        IPsiModules psiModules = mySolution.GetComponent<IPsiModules>();

        foreach (IAssemblyPsiModule mod in psiModules.GetAssemblyModules())
        {
            if (mod.ContainingProjectModule is not IAssembly assembly) continue;
            foreach (IAssemblyFile candidate in assembly.GetFiles())
            {
                FileSystemPath? candidatePath = candidate.Location.AssemblyPhysicalPath?.ToNativeFileSystemPath();
                if (candidatePath == null || candidatePath.IsEmpty || !candidatePath.ExistsFile) continue;
                string fullPath = candidatePath.FullPath;
                if (seen.Add(fullPath))
                    yield return fullPath;
                break;
            }
        }
    }
}
