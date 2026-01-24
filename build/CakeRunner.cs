#:sdk Cake.Sdk

const string solutionPath = "./OutboxKit.sln";
const string librariesPath = "./src/";
const string artifactsPath = "./artifacts/";

var target = Argument("target", "BuildAndTest");
var configuration = Argument("configuration", "Release");

//////////////////////////////////////////////////////////////////////
// TASKS
//////////////////////////////////////////////////////////////////////

Task("CleanSolution")
    .Does(() => DotNetClean(solutionPath));

Task("CleanArtifacts")
    .Does(() => CleanDirectory(artifactsPath));

Task("Restore")
    .Does(() => DotNetRestore(solutionPath));

Task("Build")
    .Does(() => DotNetBuild(
        solutionPath,
        new DotNetBuildSettings
        {
            Configuration = configuration,
            NoRestore = true
        }));

Task("Test")
    .Does(() => DotNetTest(
        solutionPath,
        new DotNetTestSettings
        {
            Configuration = configuration,
            NoRestore = true,
            NoBuild = true
        }));

Task("BuildAndTest")
    .IsDependentOn("CleanSolution")
    .IsDependentOn("Restore")
    .IsDependentOn("Build")
    .IsDependentOn("Test");

Task("Package")
    .IsDependentOn("CleanArtifacts")
    .Does(() =>
    {
        // if we use the SolutionPath, we get warnings for the samples, even though they're marked with IsPackable = false
        // so to avoid warnings polluting the output, we'll specify the projects to pack

        var projectsToPack = GetSubDirectories(librariesPath);

        foreach (var projectToPackPath in projectsToPack)
        {
            DotNetPack(
                projectToPackPath.FullPath,
                new DotNetPackSettings
                {
                    Configuration = configuration,
                    OutputDirectory = artifactsPath,
                    NoRestore = true,
                    NoBuild = true,
                    IncludeSymbols = true,
                    SymbolPackageFormat = "snupkg"
                });
        }
    });

Task("Push")
    .Does(() =>
    {
        var packages = GetFiles(System.IO.Path.Combine(artifactsPath, "*.nupkg"));
        var source = Argument<string>("source");
        var apiKey = EnvironmentVariable("API_KEY", Argument<string?>("api-key", null));
        var allowNightly = Argument("allow-nightly", false);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("API_KEY environment variable or --api-key argument is required");
        }

        if (!allowNightly && packages.Any(p => p.FullPath.Contains("nightly", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Nightly packages are not allowed to be pushed to source \"{source}\"");
        }

        if (packages.Count == 0)
        {
            throw new InvalidOperationException("No packages found to push");
        }

        foreach (var package in packages)
        {
            DotNetNuGetPush(
                package,
                new DotNetNuGetPushSettings
                {
                    Source = source,
                    ApiKey = apiKey
                });
        }
    });

Task("PackageAndPush")
    .IsDependentOn("Package")
    .IsDependentOn("Push");

Task("Default")
    .IsDependentOn("BuildAndTest");

//////////////////////////////////////////////////////////////////////
// EXECUTION
//////////////////////////////////////////////////////////////////////

RunTarget(target);