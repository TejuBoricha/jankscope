namespace Jankscope.Frames.PresentMon;

/// <summary>
/// One line a child wrote. <see cref="WasTruncated"/> means it exceeded the reader's limit: <see cref="Text"/> is then empty and the line must
/// not be parsed. <see cref="Unterminated"/> means the stream ended before the line break (the child was ended in the middle of a line): the text
/// may be cut off anywhere, so a consumer that parses by field must drop it.
/// </summary>
public readonly record struct ChildLine(string Text, bool WasTruncated, bool Unterminated = false);

/// <summary>
/// What to start: an executable that <see cref="FileHashVerifier"/> has verified (its constructor is internal, so the type system proves the
/// size and SHA-256 check happened and is still held open) and the argument vector that <see cref="PresentMonArguments"/> produced.
/// Nothing else is configurable.
/// </summary>
public sealed record ProcessLaunchRequest(VerifiedExecutable Executable, IReadOnlyList<string> Arguments)
{
    /// <summary>The verified file's path.</summary>
    public string ExecutablePath => Executable.Path;
}

/// <summary>The launcher could not (or refused to) start the executable. The message never contains a path.</summary>
public sealed class ProcessLaunchException : Exception
{
    public ProcessLaunchException(string message)
        : base(message)
    {
    }

    public ProcessLaunchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// A child that we started. The only handle the product ever holds to another program is a handle to one of these; there is no way to
/// obtain one for a process we did not start (HC-1). Disposing a child that is still running ends it, so a forgotten child cannot outlive its owner.
/// </summary>
public interface IChildProcess : IDisposable
{
    /// <summary>The operating-system id of the child, for logs and for tests that prove nothing was left behind.</summary>
    int ProcessId { get; }

    /// <summary>Next line of standard output, or null when the stream has ended.</summary>
    ValueTask<ChildLine?> ReadOutputLineAsync(CancellationToken cancellationToken);

    /// <summary>Next line of standard error, or null when the stream has ended.</summary>
    ValueTask<ChildLine?> ReadErrorLineAsync(CancellationToken cancellationToken);

    /// <summary>Completes with the exit code once the child has exited.</summary>
    Task<int> WaitForExitAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Ends the child itself. No process tree is walked: finding the children of a process means opening a handle to every process on the
    /// machine (docs/reviews/stage-1a.md, finding 1), and PresentMon starts no children (<c>--restart_as_admin</c> is refused). Returns true
    /// when nothing is left running. Never throws; on failure <paramref name="failure"/> names the kind of error (never a path).
    /// </summary>
    bool TryKill(out string? failure);
}

/// <summary>Starts the pinned executable. The production implementation is <see cref="PresentMonProcessLauncher"/>; tests use an in-memory one.</summary>
public interface IProcessLauncher
{
    /// <exception cref="ProcessLaunchException">The request was refused or the executable could not be started.</exception>
    IChildProcess Start(ProcessLaunchRequest request);
}
