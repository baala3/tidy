namespace Tidy.Core.VirtualDisks;

public enum VirtualDiskOwner
{
    /// <summary>A Linux distro installed in WSL.</summary>
    Wsl,
    /// <summary>Docker Desktop's own disks, which run in WSL too.</summary>
    Docker,
}

/// <summary>A WSL or Docker disk image. It grows as Linux writes and never shrinks by itself.</summary>
public sealed record VirtualDisk(string Name, string Path, VirtualDiskOwner Owner, VhdxUsage Usage)
{
    /// <summary>Less than this isn't worth stopping WSL for.</summary>
    public const long WorthCompactingBytes = 100L << 20;

    public long FileBytes => Usage.FileBytes;
    public long ReclaimableBytes => Usage.ReclaimableBytes;

    /// <summary>
    /// Docker's data disk doesn't mark deleted files' space free as it goes, so what compacting
    /// gives back only shows after the trim it runs first.
    /// </summary>
    public bool FreesSpaceOnlyAfterTrim { get; init; }

    public bool IsWorthCompacting => FreesSpaceOnlyAfterTrim || ReclaimableBytes >= WorthCompactingBytes;

    /// <summary>The space compacting is known to give back now.</summary>
    public long KnownReclaimableBytes => ReclaimableBytes >= WorthCompactingBytes ? ReclaimableBytes : 0;
}

/// <summary>A distro as WSL registers it for this user.</summary>
public sealed record WslDistro(string Name, string BasePath, string VhdFileName);

public sealed record CommandResult(int ExitCode, string Output);
