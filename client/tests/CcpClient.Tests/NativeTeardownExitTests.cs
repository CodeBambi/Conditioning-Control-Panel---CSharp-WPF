using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// <b>The Release-mode exit gate the startup/shutdown contract makes a condition of the FIRST native
/// dependency admission</b> — <i>"that row must test Release-mode exit for native teardown faults,
/// per the first-attempt Release-native-crash lesson"</i>
/// (<c>client/docs/startup-shutdown-contract.md:95</c>, which names LibVLC, OpenCV and ONNX by name).
/// It is written here because <c>client/docs/onnxruntime-package-admission.md</c> is the admission
/// document that owes it.
///
/// <para><b>Why a Release build and not this assembly.</b> The lesson it discharges is precisely that
/// a Debug pass is not the same measurement: <i>"The smoke harness was Debug-only while native
/// teardown failures occurred in Release"</i>
/// (<c>client/docs/first-attempt-systemic-lessons.md:67</c>), with four commits tracing one
/// intermittent Release native crash that debug evidence never settled (<c>:68</c>). So the process
/// under measurement is compiled <c>-c Release</c> and says so about itself, from its own
/// <see cref="AssemblyConfigurationAttribute"/> and <see cref="DebuggableAttribute"/>, rather than
/// being assumed.</para>
///
/// <para><b>WHAT THIS DOES NOT MEASURE, said plainly because it is the whole limit of the gate.</b>
/// ONNX Runtime is <b>not admitted and not referenced</b>, so nothing here loads it, creates an
/// <c>InferenceSession</c>, or tears one down; no model is opened and no inference runs. The subject
/// is a Release-configuration .NET process that loads a native library, calls into it through a
/// function pointer, frees it, and returns from <c>Main</c> — the SHAPE an admitted inference runtime
/// would occupy — plus the proof that a native fault in such a process is REPORTED here rather than
/// read as success. It is also not a statement about the client's own Release artifact, which is a
/// publish gate (<c>client/docs/release-publish-gates.md:42</c>) and a different subject.</para>
///
/// <para><b>Why the probe is generated rather than being the product.</b> Building
/// <c>CcpClient.Desktop</c> or this test project in Release costs <b>1.2 GB</b> of duplicated output
/// per worktree — measured, not estimated: 569 MB of per-RID natives, 521 MB of linked web payload
/// and 101 MB of libvlc — for a claim about process exit that a 200 KB console app makes just as
/// well. Redirecting that build's output with <c>-o</c> instead is worse rather than cheaper: the
/// flag becomes a GLOBAL property, so all three projects in the graph write and copy into the same
/// directory. One run of it here emitted <c>MSB3026</c> file-copy retries on SkiaSharp natives and a
/// repeat emitted none, which makes it a race — and an intermittent build failure inside a gate is
/// worse than a loud one. The generated probe is the cheap honest option, and what it costs is
/// stated above rather than implied.</para>
/// </summary>
public sealed class NativeTeardownExitTests
{
    /// <summary>
    /// <b>The anti-vacuity leg, and it is not a formality.</b> Every claim below is "in Release", and
    /// a harness that quietly measured a Debug build would satisfy the contract's words while
    /// discharging none of its lesson. So the probe reports what it is — from the two attributes the
    /// compiler writes, not from the flag the harness passed — and the SAME readout is applied to
    /// this assembly, which is built Debug by the floor. Two different answers from one readout is
    /// what makes the first one mean something.
    /// </summary>
    [Fact]
    public void TheProcessUnderMeasurementIsAReleaseBuild_AndThisAssemblyIsNot()
    {
        var run = NativeExitProbe.Observed;

        Assert.Equal("Release", run.Clean.Configuration);
        Assert.True(run.Clean.Optimized,
            "the probe's own DebuggableAttribute says the JIT optimizer is DISABLED for it, so whatever "
            + "was built, it was not an optimized assembly — and every 'in Release' claim below would be "
            + $"about a Debug build. It reported configuration '{run.Clean.Configuration}'.");

        var self = typeof(NativeTeardownExitTests).Assembly;
        var debuggable = self.GetCustomAttribute<DebuggableAttribute>();
        Assert.Equal("Debug", self.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration);
        Assert.True(debuggable is not null && debuggable.IsJITOptimizerDisabled,
            "this test assembly reports itself as OPTIMIZED, so the readout above cannot tell Debug from "
            + "Release and the probe's 'Release' answer is worth nothing. The floor builds this assembly "
            + "with -c Debug.");
    }

    /// <summary>
    /// <b>The gate itself.</b> In Release: load a native library by name, resolve an export, call
    /// through it on a real allocation, free the library, free the allocation, return from
    /// <c>Main</c>. The process must leave the process table with a normal exit code.
    ///
    /// <para>The printed trail is asserted, not just the code, because a process that exited 0 without
    /// reaching the teardown would pass a bare exit-code check while proving nothing about teardown at
    /// all.</para>
    /// </summary>
    [Fact]
    public void AReleaseProcessThatLoadsCallsAndFreesANativeLibrary_ExitsNormally()
    {
        var run = NativeExitProbe.Observed;

        Assert.Contains("LOADED", run.Clean.Output, StringComparison.Ordinal);
        Assert.Contains("CALLED", run.Clean.Output, StringComparison.Ordinal);
        Assert.Contains("FREED", run.Clean.Output, StringComparison.Ordinal);

        Assert.True(run.Clean.Exited,
            "the Release probe never left the process table, so there is no exit to report on");
        Assert.True(run.Clean.ExitCode == 0,
            $"the Release probe exited {run.Clean.ExitCode} (0x{run.Clean.ExitCode:X8}) after loading, calling and "
            + "freeing a native library — which is the native-teardown-at-exit fault this gate exists for. "
            + $"Its output was: {Compact(run.Clean.Output)} / {Compact(run.Clean.Error)}");
    }

    /// <summary>
    /// <b>The control, without which the fact above is "a program that does nothing exits 0".</b> The
    /// same Release probe, the same loaded library, the same export, one argument different: the call
    /// is made against address zero, so the fault happens INSIDE the native library at the P/Invoke
    /// boundary rather than in managed code. .NET does not let a corrupted-state exception be caught,
    /// so the process dies there.
    ///
    /// <para>What is asserted is what is portable: the probe reached the faulting call, never printed
    /// the line after it, and did not exit 0. The platform's crash code is REPORTED rather than
    /// asserted — this machine answers <c>0xC0000005</c>, and no claim is made here about any other
    /// operating system.</para>
    /// </summary>
    [Fact]
    public void ANativeFaultInThatSameReleaseProcess_IsReportedRatherThanReadAsSuccess()
    {
        var run = NativeExitProbe.Observed;

        Assert.Contains("FAULTING", run.Faulting.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("SURVIVED", run.Faulting.Output, StringComparison.Ordinal);

        Assert.True(run.Faulting.Exited,
            "the faulting probe never left the process table, so this control reached no verdict");
        Assert.True(run.Faulting.ExitCode != 0,
            "a Release process that took an access violation inside a native library exited 0, so this "
            + "harness would read a native teardown crash as a clean exit and the gate above is blind. "
            + $"Its output was: {Compact(run.Faulting.Output)} / {Compact(run.Faulting.Error)}");
    }

    private static string Compact(string text) =>
        string.Join(" | ", text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}

/// <summary>
/// <b>The probe: a minimal console program, generated into the OS temp directory, built once in
/// Release, and run twice.</b>
///
/// <para>It is generated rather than committed because its whole content is two dozen lines that must
/// be compiled in a DIFFERENT configuration from everything else in this tree, and a second project
/// in <c>client/CcpClient.sln</c> would either join the test floor (which it is not) or sit outside
/// it (which is what the floor's project discovery exists to prevent). It is written outside the
/// worktree because <c>GoonPracticeTests</c> hashes every file under <c>client/</c> and a build in
/// flight there has already failed this suite once.</para>
///
/// <para>The directory is keyed by a digest of the repository root, so two lanes in two worktrees
/// never share one build, and the sources are rewritten only when they differ — which is what keeps
/// the second and every later run down to an incremental build of about two seconds.</para>
/// </summary>
internal static class NativeExitProbe
{
    /// <summary>What the parent reads back from one probe process. Strings and an integer — nothing
    /// about the machine, nothing about the user.</summary>
    internal sealed record Attempt(
        bool Exited, int ExitCode, string Configuration, bool Optimized, string Output, string Error);

    internal sealed record Run(Attempt Clean, Attempt Faulting);

    /// <summary>One build and two launches for the whole class, because a fresh build per fact would
    /// pay the same cost three times to learn the same thing.</summary>
    internal static Run Observed => Lazily.Value;

    private static readonly Lazy<Run> Lazily = new(Observe, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];

    private const string ProjectFile = "CcpNativeExitProbe.csproj";

    /// <summary>The probe's project. No package reference of any kind — in particular NOT ONNX
    /// Runtime, which is the dependency this gate is a condition for and which stays the owner's to
    /// admit.</summary>
    private const string ProjectSource = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net10.0</TargetFramework>
            <AssemblyName>CcpNativeExitProbe</AssemblyName>
            <RootNamespace>CcpNativeExitProbe</RootNamespace>
            <Nullable>enable</Nullable>
            <ImplicitUsings>disable</ImplicitUsings>
            <LangVersion>12.0</LangVersion>
            <SatelliteResourceLanguages>en</SatelliteResourceLanguages>
          </PropertyGroup>
        </Project>
        """;

    /// <summary>
    /// The probe program. It reports what configuration it was compiled in, loads a native library
    /// present on every supported machine, resolves an export, calls it, and frees both the block and
    /// the library. Given the argument <c>fault</c> it makes the SAME call against address zero
    /// instead, which faults inside the native library.
    ///
    /// <para><c>Marshal.GetDelegateForFunctionPointer</c> rather than a function-pointer type, so the
    /// probe needs no <c>AllowUnsafeBlocks</c>; and <c>NativeLibrary.Load</c>/<c>Free</c> rather than
    /// a <c>DllImport</c>, so the library's teardown is an explicit statement in the program rather
    /// than something the runtime does invisibly at exit.</para>
    /// </summary>
    private const string ProgramSource = """
        using System;
        using System.Diagnostics;
        using System.Reflection;
        using System.Runtime.InteropServices;

        internal static class Probe
        {
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
            private delegate IntPtr Memset(IntPtr destination, int value, UIntPtr count);

            private static int Main(string[] args)
            {
                var assembly = typeof(Probe).Assembly;
                var configuration = assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "none";
                var debuggable = assembly.GetCustomAttribute<DebuggableAttribute>();
                var optimized = debuggable is null || !debuggable.IsJITOptimizerDisabled;
                Console.Out.WriteLine("CONFIGURATION " + configuration);
                Console.Out.WriteLine("OPTIMIZED " + (optimized ? "1" : "0"));
                Console.Out.Flush();

                var library = OperatingSystem.IsWindows() ? "msvcrt.dll" : "libc.so.6";
                var handle = NativeLibrary.Load(library);
                var memset = Marshal.GetDelegateForFunctionPointer<Memset>(NativeLibrary.GetExport(handle, "memset"));
                Console.Out.WriteLine("LOADED " + library);
                Console.Out.Flush();

                var block = Marshal.AllocHGlobal(64);
                memset(block, 0, 64);
                Marshal.FreeHGlobal(block);
                Console.Out.WriteLine("CALLED");
                Console.Out.Flush();

                if (args.Length > 0 && args[0] == "fault")
                {
                    Console.Out.WriteLine("FAULTING");
                    Console.Out.Flush();
                    memset(IntPtr.Zero, 0, 64);
                    Console.Out.WriteLine("SURVIVED");
                    Console.Out.Flush();
                    return 2;
                }

                NativeLibrary.Free(handle);
                Console.Out.WriteLine("FREED " + library);
                Console.Out.Flush();
                return 0;
            }
        }
        """;

    private static Run Observe()
    {
        var directory = ProbeDirectory();
        Directory.CreateDirectory(directory);
        WriteIfDifferent(Path.Combine(directory, ProjectFile), ProjectSource);
        WriteIfDifferent(Path.Combine(directory, "Program.cs"), ProgramSource);

        // THE OUTPUT IS DELETED BEFORE THE BUILD, AND THAT IS NOT TIDINESS — IT IS THE ONLY THING
        // THAT MAKES "IN RELEASE" CHECKABLE. Found by mutation: pointing the build at `-c Debug` left
        // the previous run's Release apphost sitting in bin/Release, the harness ran THAT, and every
        // fact stayed green while measuring a build nobody had asked for. It is the same
        // stale-measurement failure check-floor.mjs carries its own guard against
        // (<c>client/tests/floor/check-floor.mjs:255-264</c>), reached here by a different route.
        // With the tree gone, a build that does not produce a Release apphost produces no subject and
        // this gate fails loudly instead of reporting a confident number about the wrong binary.
        var output = Path.Combine(directory, "bin", "Release");
        if (Directory.Exists(output))
        {
            Directory.Delete(output, recursive: true);
        }

        Build(directory);
        var executable = Executable(directory);

        return new Run(Launch(executable, null), Launch(executable, "fault"));
    }

    /// <summary>A per-repository directory outside the worktree. Keyed by a digest of the repo root
    /// so two lanes never contend for one build directory, and stable across runs so the build is
    /// incremental.</summary>
    private static string ProbeDirectory()
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(RepoRoot())))[..12].ToLowerInvariant();
        return Path.Combine(Path.GetTempPath(), $"ccp-native-exit-{key}");
    }

    private static void WriteIfDifferent(string path, string content)
    {
        // Rewriting an identical file would touch its timestamp and force a full rebuild every run,
        // which is the difference between a two-second gate and a thirty-second one.
        if (File.Exists(path) && File.ReadAllText(path) == content)
        {
            return;
        }

        File.WriteAllText(path, content);
    }

    private static void Build(string directory)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = directory,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        start.ArgumentList.Add("build");
        start.ArgumentList.Add(ProjectFile);
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("Release");
        start.ArgumentList.Add("--nologo");
        start.ArgumentList.Add("-v");
        start.ArgumentList.Add("quiet");

        // GLOBAL properties, not project ones: the SDK imports Directory.Build.props before the
        // project body is evaluated, so setting these inside the generated csproj would be a no-op
        // that reads like a guarantee. Nothing in the temp directory's ancestry may change what this
        // probe is built from, because a stray props file there would silently redefine the
        // configuration this whole gate is about.
        start.ArgumentList.Add("-p:ImportDirectoryBuildProps=false");
        start.ArgumentList.Add("-p:ImportDirectoryBuildTargets=false");

        Process process;
        try
        {
            process = Process.Start(start)!;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // Never a skip: a machine that cannot run `dotnet` cannot run this suite at all, and
            // allowedSkips is not a quarantine list.
            throw new InvalidOperationException(
                "could not start `dotnet` to build the Release probe. The .NET SDK is a hard requirement of "
                + "this tree, so this gate fails rather than quietly reporting nothing", ex);
        }

        using (process)
        {
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            TestWait.UntilSync(
                () => process.HasExited,
                $"`dotnet build -c Release` of the native-exit probe in {directory} to finish",
                () => $"exited={process.HasExited}",
                TestWait.InjectedBudget);

            if (process.ExitCode != 0)
            {
                throw new Xunit.Sdk.XunitException(
                    $"the Release build of the native-exit probe failed with exit {process.ExitCode}. "
                    + "This gate has no subject without it, and a build failure here is a real failure "
                    + $"rather than a missing precondition. Output:\n{output.Result}\n{error.Result}");
            }
        }
    }

    private static string Executable(string directory)
    {
        var stem = Path.Combine(directory, "bin", "Release", "net10.0", "CcpNativeExitProbe");
        var windows = $"{stem}.exe";
        if (File.Exists(windows))
        {
            return windows;
        }

        if (File.Exists(stem))
        {
            return stem;
        }

        throw new Xunit.Sdk.XunitException(
            $"the Release probe build reported success but produced no apphost at {windows} or {stem}. "
            + "The whole bin/Release tree is deleted before every build precisely so this cannot be "
            + "answered by a leftover binary from an earlier run, so there is nothing to measure.");
    }

    private static Attempt Launch(string executable, string? argument)
    {
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (argument is not null)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)
            ?? throw new Xunit.Sdk.XunitException($"could not start the Release probe at {executable}");

        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        TestWait.UntilSync(
            () => process.HasExited && output.IsCompleted && error.IsCompleted,
            $"the Release probe ({argument ?? "clean"}) to exit and close its streams",
            () => $"exited={process.HasExited}");

        var text = output.Result;
        return new Attempt(
            process.HasExited,
            process.HasExited ? process.ExitCode : int.MinValue,
            Field(text, "CONFIGURATION ") ?? "unreported",
            Field(text, "OPTIMIZED ") == "1",
            text,
            error.Result);
    }

    /// <summary>The value of one <c>KEY value</c> line the probe printed, or null when it never
    /// printed one — which is itself a reportable answer rather than a default.</summary>
    private static string? Field(string text, string prefix) => text
        .Split('\n')
        .Select(line => line.Trim())
        .FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine([dir.FullName, .. RepoAnchorParts])))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"repo root not found walking up from {AppContext.BaseDirectory} "
            + $"(anchor: {string.Join('/', RepoAnchorParts)})");
    }
}
