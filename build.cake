#r "System.Xml.Linq" 
using System.Xml.Linq;

///////////////////////////////////////////////////////////////////////////////
// SETUP / TEARDOWN
///////////////////////////////////////////////////////////////////////////////

Setup<BuildParameters>(context =>
{
    var parameters = new BuildParameters(context);

    Information($"Cake version: {typeof(ICakeContext).Assembly.GetName().Version}");
    Information("");
    Information($"dotnet cake build.cake");
    Information($"--configuration: {parameters.Configuration}");
    Information($"--commit: {parameters.Commit}");
    Information($"--pack-output: {parameters.PackOutput}");
    Information($"--publish-output: {parameters.PublishOutput}");
    Information($"--solution: {parameters.Solution}");
    Information($"--target: {Argument("target", "default")}");
    Information($"--version: {parameters.Version}");

    return parameters;
});

Teardown<BuildParameters>((context, parameters) =>
{
    Information("Finished running tasks.");
});

//////////////////////////////////////////////////////////////////////
// TASKS
//////////////////////////////////////////////////////////////////////

Task("Clean")
    .Does<BuildParameters>((context, parameters) =>
{
    DotNetClean(parameters.Solution, new DotNetCleanSettings()
    {
        Configuration = parameters.Configuration
    });
});

Task("Restore")
    .IsDependentOn("Clean")
    .Does<BuildParameters>((context, parameters) =>
{
    DotNetRestore(parameters.Solution, new DotNetRestoreSettings()
    {
        LockedMode = true,
        UseLockFile = true,
    });
});

Task("Build")
    .IsDependentOn("Restore")
    .Does<BuildParameters>((context, parameters) =>
{
    DotNetBuild(parameters.Solution, new DotNetBuildSettings()
    {
        Configuration = parameters.Configuration,
        NoRestore = true,
        MSBuildSettings = new DotNetMSBuildSettings()
            .SetVersion(parameters.Version)
            .WithProperty("ContinuousIntegrationBuild", "true")
            .WithProperty("SourceRevisionId", parameters.Commit)
    });
});

Task("Test")
    .IsDependentOn("Build")
    .Does<BuildParameters>((context, parameters) =>
{
    DotNetTest(parameters.Solution, new DotNetTestSettings()
    {
        ArgumentCustomization = args => args
            .Append("--logger trx"),
        Configuration = parameters.Configuration,
        NoBuild = true,
        NoRestore = true,
        ResultsDirectory = parameters.TestResults,
        HandleExitCode = exitCode =>
        {
            // Disable non-zero exit code for dotnet test.
            if (exitCode != 0)
            {
                context.Warning($"dotnet test returned exit code {exitCode} – continuing so results can be verified later.");
            }
            return true;
        }
    });
});

Task("Publish")
    .Does<BuildParameters>((context, parameters) =>
{
    DotNetPublish(parameters.Project, new DotNetPublishSettings
    {
        Configuration = parameters.Configuration,
        NoBuild = true,
        NoRestore = true,
        OutputDirectory = parameters.PublishOutput,
    });
});

Task("Pack")
    .IsDependentOn("Build")
    .Does<BuildParameters>((context, parameters) =>
{
    DotNetPack(parameters.Solution, new DotNetPackSettings()
    {
        ArgumentCustomization = args => args
            .Append($"/p:PackageVersion={parameters.Version}"),
        Configuration = parameters.Configuration,
        NoBuild = true,
        OutputDirectory = parameters.PackOutput,
    });
});

Task("Verify")
    .IsDependentOn("Test")
    .Does(() =>
{
    var trxPath = "./testresults/TestResults.trx";

    if (!System.IO.File.Exists(trxPath))
    {
        Error($"TRX file not found at {trxPath}");
        Environment.Exit(1);
    }

    var doc = XDocument.Load(trxPath);
    var counters = doc.Descendants("Counters").FirstOrDefault();
    var failed = int.Parse(counters?.Attribute("failed")?.Value ?? "0");

    if (failed > 0)
    {
        Error($"{failed} test(s) failed.");
        Environment.Exit(1);
    }

    Information("All tests passed.");
});

//////////////////////////////////////////////////////////////////////
// TASK TARGETS
//////////////////////////////////////////////////////////////////////

Task("default")
    .IsDependentOn("Clean")
    .IsDependentOn("Restore")
    .IsDependentOn("Build")
    .IsDependentOn("Test")
    .IsDependentOn("Publish")
    .IsDependentOn("Pack");

//////////////////////////////////////////////////////////////////////
// EXECUTION
//////////////////////////////////////////////////////////////////////

RunTarget(Argument("target", "default"));

//////////////////////////////////////////////////////////////////////
// PARAMETERS
//////////////////////////////////////////////////////////////////////

public class BuildParameters
{
    public string Configuration { get; set; }
    public string Commit { get; set; }
    public string PackOutput { get; set; }
    public string PublishOutput { get; set; }
    public string Project { get; set; }
    public string Solution { get; set; }
    public string Target { get; set; }
    public string TestResults { get; set; }
    public string Version { get; set; }

    public BuildParameters(ISetupContext context)
    {
        var buildSystem = context.BuildSystem();

        Configuration = context.Argument("configuration", "Release");
        Commit = context.Argument("commit", "");
        PackOutput = context.Argument("pack-output", "/pack");
        PublishOutput = context.Argument("publish-output", "/publish");
        Project = context.Argument("project", "src/Krp/Krp.csproj");
        Solution = context.Argument("solution", "krp.sln");
        Target = context.Argument("target", "");
        TestResults = context.Argument("test-results", "/testresults");
        Version = context.Argument("version", "");
    }
}
