using System;
using System.IO;
using System.Linq;
using Cake.Common;
using Cake.Common.IO;
using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Build;
using Cake.Common.Tools.DotNet.NuGet.Push;
using Cake.Common.Tools.DotNet.Pack;
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
    public const string LibrariesPath = "../../src/";
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

[TaskName("Package")]
[IsDependentOn(typeof(CleanArtifactsTask))]
public sealed class PackageTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        // if we use the SolutionPath, we get warnings for the samples, even though they're marked with IsPackable = false
        // so to avoid warnings polluting the output, we'll specify the projects to pack
        
        var projectsToPack = context.GetSubDirectories(LibrariesPath);
        
        foreach (var projectToPackPath in projectsToPack)
        {
            context.DotNetPack(
                projectToPackPath.FullPath,
                new DotNetPackSettings
                {
                    Configuration = context.MsBuildConfiguration,
                    OutputDirectory = ArtifactsPath,
                    NoRestore = true,
                    NoBuild = true,
                    IncludeSymbols = true,
                    SymbolPackageFormat = "snupkg"
                });
        }
    }
}

[TaskName("Push")]
public sealed class PushTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        var packages = context.GetFiles(Path.Combine(ArtifactsPath, "*.nupkg"));
        var packageSymbols = context.GetFiles(Path.Combine(ArtifactsPath, "*.snupkg"));
        var source = context.Argument<string>("source");
        var apiKey = context.EnvironmentVariable("API_KEY", context.Argument<string>("api-key", null));

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("API_KEY environment variable or --api-key argument is required");
        }

        if (packages.Count == 0)
        {
            throw new InvalidOperationException("No packages found to push");
        }

        foreach (var package in packages.Concat(packageSymbols))
        {
            context.DotNetNuGetPush(
                package,
                new DotNetNuGetPushSettings
                {
                    Source = source,
                    ApiKey = apiKey
                });
        }
    }
}

[TaskName("PackageAndPush")]
[IsDependentOn(typeof(PackageTask))]
[IsDependentOn(typeof(PushTask))]
public class PackageAndPushTask : FrostingTask;

[TaskName("Default")]
[IsDependentOn(typeof(BuildAndTestTask))]
public class DefaultTask : FrostingTask;