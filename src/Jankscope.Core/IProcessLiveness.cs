namespace Jankscope.Core;

/// <summary>
/// Checks whether a process with the given ID is still running on the system.
/// Used to detect orphaned trace sessions whose parent process crashed.
/// </summary>
public interface IProcessLiveness
{
    bool IsAlive(int processId);
}
