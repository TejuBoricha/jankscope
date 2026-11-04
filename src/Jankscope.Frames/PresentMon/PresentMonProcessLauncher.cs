using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Jankscope.Frames.PresentMon;

// This file is the ONLY place in the product that starts a process (BannedSymbols.txt bans the Process type and ProcessStartInfo everywhere
// else; RepoLintTests pins this file by name and counts its pragma region). What makes it acceptable:
//  - it starts exactly one kind of program: an executable that FileHashVerifier has verified against the pin (the request carries a
//    VerifiedExecutable, whose constructor is internal, so a caller cannot hand it a bare path) from an absolute local path, with an
//    argument vector that PresentMonArguments.EnsureSafe has re-checked here (allowlist grammar: --restart_as_admin and every other
//    unlisted option is refused). The class is internal: only this assembly (the runner) can reach it;
//  - no shell (UseShellExecute=false), arguments via ArgumentList (each element quoted by the framework), no window, an empty input
//    stream, a minimal environment, and a private temporary working directory that is removed afterwards;
//  - the only process handle it ever holds is the one returned by the start call. It never looks up, opens, enumerates or signals any
//    other process by id or name, and it never waits on or kills anything it did not start (HC-1). In particular it ends the child
//    with Process.Kill() and never with Kill(entireProcessTree: true): the tree form makes the runtime enumerate every process on the
//    machine and open a handle to each (docs/reviews/stage-1a.md, finding 1). PresentMon starts no children of its own, so there is no
//    tree to end. RepoLintTests pins the exact set of Process members this file may use;
//  - there are no native imports in this file.
#pragma warning disable RS0030 // The reviewed launcher for the pinned PresentMon console executable: only a process this class started is ever touched (HC-1, HC-5, HC-7, ADR 0003).

/// <summary>Starts the pinned PresentMon console executable. See the notes at the top of this file for what it does and does not do.</summary>
internal sealed class PresentMonProcessLauncher : IProcessLauncher
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public IChildProcess Start(ProcessLaunchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Executable is null)
        {
            throw new ProcessLaunchException("Refused to start: there is no verified executable.");
        }

        try
        {
            PresentMonArguments.EnsureSafe(request.Arguments);
        }
        catch (ArgumentException e)
        {
            throw new ProcessLaunchException("Refused to start: the argument vector is not on the allowlist. " + e.Message, e);
        }

        if (!FileHashVerifier.IsCanonicalLocalAbsolutePath(request.ExecutablePath))
        {
            throw new ProcessLaunchException("Refused to start: the executable path is not an absolute, normalized local path.");
        }

        DirectoryInfo workDirectory;
        try
        {
            workDirectory = Directory.CreateTempSubdirectory("Jankscope-pm-");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new ProcessLaunchException("Could not create a private working directory (" + e.GetType().Name + ").", e);
        }

        Process? started = null;
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = request.ExecutablePath,
                WorkingDirectory = workDirectory.FullName,
                UseShellExecute = false,
                CreateNoWindow = true,
                ErrorDialog = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Utf8,
                StandardErrorEncoding = Utf8,
            };

            foreach (var argument in request.Arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            ApplyMinimalEnvironment(startInfo.Environment, workDirectory.FullName);

            started = Process.Start(startInfo) ?? throw new ProcessLaunchException("The operating system did not create a process.");
            started.StandardInput.Close(); // the child gets an empty input stream and never shares ours
            return new StartedChild(started, workDirectory);
        }
        catch (Exception e)
        {
            EndQuietly(started);
            DeleteQuietly(workDirectory);
            if (e is ProcessLaunchException)
            {
                throw;
            }

            var detail = e is Win32Exception win32 ? $"Windows error {win32.NativeErrorCode}" : e.GetType().Name;
            throw new ProcessLaunchException($"The executable could not be started ({detail}).", e);
        }
    }

    /// <summary>Only what a console executable needs to find its system libraries; nothing from the user's environment (no user name, profile, tokens or search path).</summary>
    private static void ApplyMinimalEnvironment(IDictionary<string, string?> environment, string temp)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        environment.Clear();
        environment["SystemRoot"] = windows;
        environment["windir"] = windows;
        environment["SystemDrive"] = Path.GetPathRoot(windows)!.TrimEnd('\\');
        environment["PATH"] = Environment.SystemDirectory;
        environment["TEMP"] = temp;
        environment["TMP"] = temp;
    }

    private static void EndQuietly(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            process.Kill();
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // already gone, or not ours to end: nothing more to do
        }

        process.Dispose();
    }

    private static void DeleteQuietly(DirectoryInfo directory)
    {
        try
        {
            directory.Delete(recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // best effort: it is an empty directory below the user's own temporary folder
        }
    }

    private sealed class StartedChild : IChildProcess
    {
        private readonly Process _process;
        private readonly DirectoryInfo _workDirectory;
        private readonly BoundedLineReader _output;
        private readonly BoundedLineReader _errors;
        private int _disposed;

        public StartedChild(Process process, DirectoryInfo workDirectory)
        {
            _process = process;
            _workDirectory = workDirectory;
            ProcessId = process.Id;
            _output = new BoundedLineReader(process.StandardOutput.BaseStream);
            _errors = new BoundedLineReader(process.StandardError.BaseStream);
        }

        public int ProcessId { get; }

        public ValueTask<ChildLine?> ReadOutputLineAsync(CancellationToken cancellationToken) => _output.ReadLineAsync(cancellationToken);

        public ValueTask<ChildLine?> ReadErrorLineAsync(CancellationToken cancellationToken) => _errors.ReadLineAsync(cancellationToken);

        public async Task<int> WaitForExitAsync(CancellationToken cancellationToken)
        {
            await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return _process.ExitCode;
        }

        public bool TryKill(out string? failure)
        {
            failure = null;
            try
            {
                if (_process.HasExited)
                {
                    return true;
                }

                _process.Kill();
                return true;
            }
            catch (InvalidOperationException)
            {
                return true; // exited in the meantime, or already disposed
            }
            catch (Exception e) when (e is Win32Exception or NotSupportedException)
            {
                failure = "ending the child failed: " + e.GetType().Name;
                return false;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }

            // A child that is still running when its owner lets go of it is ended: nothing outlives its handle.
            TryKill(out _);
            try
            {
                _process.WaitForExit(2000);
            }
            catch (Exception e) when (e is InvalidOperationException or Win32Exception)
            {
                // see above
            }

            _process.Dispose();
            DeleteQuietly(_workDirectory);
        }
    }
}

#pragma warning restore RS0030
