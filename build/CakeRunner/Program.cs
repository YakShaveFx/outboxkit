using Cake.Common;
using Cake.Common.IO;
using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Build;
using Cake.Common.Tools.DotNet.Test;
using Cake.Core;
using Cake.Frosting;
using static BuildContext;

return new CakeHost()
    .UseContext<BuildContext>()
    .Run(args);

public class BuildContext(ICakeContext context) : FrostingContext(context)
{
    public const string SolutionPath = "../../OutboxKit.sln";
    public const string ArtifactsPath = "../../artifacts/";
    
    public string MsBuildConfiguration { get; } = context.Argument("configuration", "Release");
}

[TaskName("CleanSolution")]
public sealed class CleanSolutionTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context) => context.DotNetClean(SolutionPath);
}

[TaskName("CleanArtifacts")]
public sealed class CleanArtifactsTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context) => context.CleanDirectory(ArtifactsPath);
}

[TaskName("Restore")]
public sealed class RestoreTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context) => context.DotNetRestore(SolutionPath);
}

[TaskName("Build")]
public sealed class BuildTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
        => context.DotNetBuild(
            SolutionPath,
            new DotNetBuildSettings
            {
                Configuration = context.MsBuildConfiguration,
                NoRestore = true
            });
}

[TaskName("Test")]
public sealed class TestTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
        => context.DotNetTest(
            SolutionPath,
            new DotNetTestSettings
            {
                Configuration = context.MsBuildConfiguration,
                NoRestore = true,
                NoBuild = true
            });
}

[TaskName("BuildAndTest")]
[IsDependentOn(typeof(CleanSolutionTask))]
[IsDependentOn(typeof(RestoreTask))]
[IsDependentOn(typeof(BuildTask))]
[IsDependentOn(typeof(TestTask))]
public class BuildAndTestTask : FrostingTask;

[TaskName("Default")]
[IsDependentOn(typeof(BuildAndTestTask))]
public class DefaultTask : FrostingTask;