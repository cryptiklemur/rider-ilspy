using System;
using System.Threading;
using JetBrains.Application.Parts;
using JetBrains.Application.Settings;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;
using JetBrains.Rd.Tasks;
using JetBrains.ReSharper.Feature.Services.Protocol;
using JetBrains.Util;
using JetBrains.Util.Logging;
using RiderIlSpy.Model;

namespace RiderIlSpy;

/// <summary>
/// [SolutionComponent] that subscribes to <see cref="RiderIlSpyModel.SaveAsProject"/>
/// and delegates to <see cref="IlSpyDecompiler.DecompileAssemblyToProject"/>. Lives
/// in its own component (rather than the navigation provider) because the
/// "Save Decompiled Assembly as Project..." flow is its own subsystem — it has
/// its own rd contract (<see cref="SaveAsProjectRequest"/> /
/// <see cref="SaveAsProjectResponse"/>), its own error model, and consumes a
/// different IlSpyDecompiler API than the navigation surface. Keeping it
/// alongside <see cref="IlSpyExternalSourcesProvider"/> previously meant the
/// provider's ctor wired one orthogonal rd handler, which muddied the
/// provider's responsibility.
/// </summary>
/// <remarks>
/// The handler runs via <c>SetSync</c> on the protocol thread. The kotlin
/// frontend wraps each call in a backgroundable progress task so the IDE
/// stays responsive while ILSpy churns. Exceptions are caught locally and
/// returned as <c>success=false</c> responses — never thrown to the rd layer.
/// </remarks>
[SolutionComponent(Instantiation.DemandAnyThreadSafe)]
public sealed class SaveAsProjectProtocolHandler
{
    private static readonly ILogger ourLogger = Logger.GetLogger<SaveAsProjectProtocolHandler>();

    private readonly IlSpyDecompiler myDecompiler;
    private readonly IlSpyRequestSettingsBuilder mySettingsBuilder;

    public SaveAsProjectProtocolHandler(
        Lifetime lifetime,
        ISolution solution,
        ISettingsStore settingsStore,
        IlSpyDecompiler decompiler)
    {
        myDecompiler = decompiler;
        IContextBoundSettingsStoreLive boundSettings = settingsStore.BindToContextLive(lifetime, ContextRange.ApplicationWide);
        mySettingsBuilder = new IlSpyRequestSettingsBuilder(boundSettings, ourLogger);
        RiderIlSpyModel model = solution.GetProtocolSolution().GetRiderIlSpyModel();
        model.SaveAsProject.SetSync(OnRequest);
    }

    private SaveAsProjectResponse OnRequest(Lifetime _, SaveAsProjectRequest request)
    {
        try
        {
            // SaveAsProject doesn't honor the rd-live mode toggle — it always
            // emits a full C# project. So we pass IlSpyOutputMode.CSharp as the
            // snapshot's Mode; only DecompilerSettings + ExtraSearchDirs are
            // consumed by DecompileAssemblyToProject.
            IlSpyRequestSettings snapshot = mySettingsBuilder.Snapshot(IlSpyOutputMode.CSharp);
            DecompileAssemblyToProjectResult result = myDecompiler.DecompileAssemblyToProject(
                request.AssemblyPath,
                request.TargetDirectory,
                snapshot.DecompilerSettings,
                snapshot.ExtraSearchDirs,
                CancellationToken.None);
            return new SaveAsProjectResponse(
                success: true,
                projectFilePath: result.ProjectFilePath ?? "",
                csharpFileCount: result.CSharpFileCount,
                errorMessage: "");
        }
        catch (Exception ex)
        {
            ourLogger.Error(ex, "RiderIlSpy.SaveAsProject failed for " + request.AssemblyPath);
            return new SaveAsProjectResponse(
                success: false,
                projectFilePath: "",
                csharpFileCount: 0,
                errorMessage: ex.Message);
        }
    }
}
