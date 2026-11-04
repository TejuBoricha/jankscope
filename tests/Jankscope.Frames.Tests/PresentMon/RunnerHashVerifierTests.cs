using Jankscope.Frames.PresentMon;

namespace Jankscope.Frames.Tests.PresentMon;

/// <summary>Size and SHA-256 are checked against the pin before EVERY launch, on the handle that stays open across it (HC-7).</summary>
public class RunnerHashVerifierTests
{
    private static readonly byte[] Content = Enumerable.Range(0, 5000).Select(i => (byte)(i * 31 % 251)).ToArray();

    private static PinnedExecutable PinFor(string path, byte[]? content = null) =>
        new(path, (content ?? Content).Length, TempDir.Sha256Hex(content ?? Content));

    [Fact]
    public void A_file_that_matches_size_and_hash_is_verified_and_reports_what_it_checked()
    {
        using var dir = new TempDir();
        var path = dir.Write("pm.exe", Content);

        using var result = FileHashVerifier.Open(PinFor(path));
        Assert.Equal(ExecutableCheck.Verified, result.Outcome);
        Assert.True(result.IsVerified);
        Assert.False(result.IsPermanent);
        Assert.NotNull(result.Executable);
        Assert.Equal(path, result.Executable!.Path);
        Assert.Equal(Content.Length, result.Executable.SizeBytes);
        Assert.Equal(TempDir.Sha256Hex(Content), result.Executable.Sha256);
    }

    [Fact]
    public void An_upper_case_pin_matches_the_same_bytes()
    {
        using var dir = new TempDir();
        var path = dir.Write("pm.exe", Content);
        using var result = FileHashVerifier.Open(new PinnedExecutable(path, Content.Length, TempDir.Sha256Hex(Content).ToUpperInvariant()));
        Assert.True(result.IsVerified);
    }

    [Fact]
    public void A_different_size_is_refused_before_anything_is_hashed()
    {
        using var dir = new TempDir();
        var path = dir.Write("pm.exe", Content);
        using var result = FileHashVerifier.Open(new PinnedExecutable(path, Content.Length + 1, TempDir.Sha256Hex(Content)));
        Assert.Equal(ExecutableCheck.SizeMismatch, result.Outcome);
        Assert.True(result.IsPermanent);
        Assert.Null(result.Executable);
        Assert.Contains("5000", result.Reason);
        Assert.Contains("5001", result.Reason);
    }

    [Fact]
    public void One_flipped_bit_at_the_same_size_is_a_hash_mismatch_that_names_both_hashes()
    {
        using var dir = new TempDir();
        var tampered = (byte[])Content.Clone();
        tampered[2500] ^= 0x01;
        var path = dir.Write("pm.exe", tampered);

        using var result = FileHashVerifier.Open(PinFor(path, Content));
        Assert.Equal(ExecutableCheck.HashMismatch, result.Outcome);
        Assert.True(result.IsPermanent);
        Assert.Null(result.Executable);
        Assert.Contains(TempDir.Sha256Hex(Content), result.Reason);
        Assert.Contains(TempDir.Sha256Hex(tampered), result.Reason);
        Assert.Contains("refusing to launch", result.Reason);
    }

    [Fact]
    public void The_hash_is_recomputed_on_every_open_so_a_file_changed_after_the_first_launch_is_refused()
    {
        using var dir = new TempDir();
        var path = dir.Write("pm.exe", Content);
        var pin = PinFor(path);

        using (var first = FileHashVerifier.Open(pin))
        {
            Assert.True(first.IsVerified);
        }

        var tampered = (byte[])Content.Clone();
        tampered[10] ^= 0xFF;
        File.WriteAllBytes(path, tampered);

        using var second = FileHashVerifier.Open(pin);
        Assert.Equal(ExecutableCheck.HashMismatch, second.Outcome);
    }

    [Theory]
    [InlineData("pm.exe")]
    [InlineData(".\\pm.exe")]
    [InlineData("..\\pm.exe")]
    [InlineData("sub\\pm.exe")]
    [InlineData("\\pm.exe")]
    [InlineData("C:pm.exe")]
    [InlineData("C:/Windows/System32/cmd.exe")]
    [InlineData("C:\\Windows\\..\\Windows\\System32\\cmd.exe")]
    [InlineData("C:\\Windows\\System32\\.\\cmd.exe")]
    [InlineData("C:\\Windows\\System32\\cmd.exe.")]
    [InlineData("C:\\Windows\\System32\\cmd.exe:stream")]
    [InlineData("\\\\server\\share\\pm.exe")]
    [InlineData("\\\\?\\C:\\Windows\\System32\\cmd.exe")]
    [InlineData("\\\\.\\C:\\Windows\\System32\\cmd.exe")]
    [InlineData("")]
    [InlineData("C:")]
    [InlineData("C:\\")]
    public void Relative_network_device_and_non_normalized_paths_are_refused_without_touching_the_file_system(string path)
    {
        using var result = FileHashVerifier.Open(new PinnedExecutable(path, 1, new string('a', 64)));
        Assert.Equal(ExecutableCheck.PathNotAbsolute, result.Outcome);
        Assert.True(result.IsPermanent);
        Assert.Contains("absolute", result.Reason);
    }

    [Fact]
    public void A_relative_name_is_refused_even_when_a_matching_file_sits_in_the_working_directory()
    {
        using var dir = new TempDir();
        dir.Write("pm.exe", Content);
        var previous = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = dir.Path;
            using var result = FileHashVerifier.Open(PinFor("pm.exe"));
            Assert.Equal(ExecutableCheck.PathNotAbsolute, result.Outcome);
        }
        finally
        {
            Environment.CurrentDirectory = previous;
        }
    }

    [Fact]
    public void A_missing_file_and_a_missing_directory_are_reported_as_not_found()
    {
        using var dir = new TempDir();
        using var missingFile = FileHashVerifier.Open(PinFor(dir.File("nope.exe")));
        Assert.Equal(ExecutableCheck.NotFound, missingFile.Outcome);
        Assert.True(missingFile.IsPermanent);

        using var missingDirectory = FileHashVerifier.Open(PinFor(dir.File("no\\such\\dir\\pm.exe")));
        Assert.Equal(ExecutableCheck.NotFound, missingDirectory.Outcome);
    }

    [Fact]
    public void A_directory_is_not_a_regular_file()
    {
        using var dir = new TempDir();
        var folder = dir.File("folder.exe");
        Directory.CreateDirectory(folder);
        using var result = FileHashVerifier.Open(PinFor(folder));
        Assert.Equal(ExecutableCheck.NotARegularFile, result.Outcome);
        Assert.True(result.IsPermanent);
    }

    [SymbolicLinkFact]
    public void A_symbolic_link_is_not_a_regular_file_even_when_its_target_matches()
    {
        using var dir = new TempDir();
        var target = dir.Write("real.exe", Content);
        var link = dir.File("link.exe");
        File.CreateSymbolicLink(link, target);

        using var result = FileHashVerifier.Open(PinFor(link));
        Assert.Equal(ExecutableCheck.NotARegularFile, result.Outcome);
    }

    [Fact]
    public void A_file_someone_else_has_open_for_writing_cannot_be_verified_and_that_is_not_called_permanent()
    {
        using var dir = new TempDir();
        var path = dir.Write("pm.exe", Content);
        using var writer = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);

        using var result = FileHashVerifier.Open(PinFor(path));
        Assert.Equal(ExecutableCheck.CannotOpen, result.Outcome);
        Assert.False(result.IsPermanent);
        Assert.Null(result.Executable);
    }

    [Fact]
    public void While_the_verified_file_is_held_it_cannot_be_rewritten_deleted_or_renamed_and_neither_can_its_folders()
    {
        // This is the time-of-check/time-of-use defence: the hash was taken from this handle, and the operating system
        // refuses every normal way of swapping the file for another before the process is created from the same path.
        using var dir = new TempDir();
        var folder = Path.Combine(dir.Path, "outer", "inner");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "pm.exe");
        File.WriteAllBytes(path, Content);

        var result = FileHashVerifier.Open(PinFor(path));
        Assert.True(result.IsVerified);

        Assert.Throws<IOException>(() => File.WriteAllBytes(path, [1, 2, 3]));
        Assert.Throws<IOException>(() => File.Delete(path));
        Assert.Throws<IOException>(() => File.Move(path, Path.Combine(folder, "other.exe")));
        Assert.Throws<IOException>(() => Directory.Move(folder, Path.Combine(dir.Path, "outer", "inner2")));
        Assert.Throws<IOException>(() => Directory.Move(Path.Combine(dir.Path, "outer"), Path.Combine(dir.Path, "outer2")));
        Assert.Throws<IOException>(() => new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite).Dispose());

        // Reading is still allowed, which is what the operating system needs to map the image.
        using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Equal(Content.Length, reader.Length);
        }

        result.Dispose();
        File.WriteAllBytes(path, [1, 2, 3]); // released: ordinary writes work again
        File.Delete(path);
    }

    [Fact]
    public void Disposing_twice_is_harmless()
    {
        using var dir = new TempDir();
        var result = FileHashVerifier.Open(PinFor(dir.Write("pm.exe", Content)));
        result.Dispose();
        result.Dispose();
        result.Executable!.Dispose();
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("zz2a706bc6ad475749e3b7e3409263aa1e6906d45bdcf993f6dbc0f660188f1af")]
    [InlineData("b2a706bc6ad475749e3b7e3409263aa1e6906d45bdcf993f6dbc0f660188f1af00")]
    public void A_pin_with_a_malformed_hash_cannot_be_constructed(string sha)
    {
        Assert.Throws<ArgumentException>(() => new PinnedExecutable("C:\\x\\pm.exe", 10, sha));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_pin_needs_a_positive_size(long size)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PinnedExecutable("C:\\x\\pm.exe", size, new string('a', 64)));
    }

    // ---- the lock file ----

    [Fact]
    public void The_real_lock_file_yields_the_owner_approved_pin_for_the_bundled_executable()
    {
        var pin = PresentMonLockFile.Read(RunnerTestPaths.PinnedLockFile);
        Assert.Equal(980320, pin.SizeBytes);
        Assert.Equal("b2a706bc6ad475749e3b7e3409263aa1e6906d45bdcf993f6dbc0f660188f1af", pin.Sha256);
        Assert.Equal(Path.Combine(RunnerTestPaths.RepoRoot, "third_party", "PresentMon", "2.6.0", "PresentMon-2.6.0-x64.exe"), pin.Path);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"file\":\"a.exe\",\"sizeBytes\":0,\"sha256\":\"%H\"}")]
    [InlineData("{\"file\":\"a.exe\",\"sizeBytes\":\"12\",\"sha256\":\"%H\"}")]
    [InlineData("{\"file\":\"a.exe\",\"sizeBytes\":12}")]
    [InlineData("{\"file\":\"a.exe\",\"sizeBytes\":12,\"sha256\":\"short\"}")]
    [InlineData("{\"file\":\"..\\\\evil.exe\",\"sizeBytes\":12,\"sha256\":\"%H\"}")]
    [InlineData("{\"file\":\"C:\\\\Windows\\\\System32\\\\cmd.exe\",\"sizeBytes\":12,\"sha256\":\"%H\"}")]
    [InlineData("{\"file\":\"\\\\\\\\server\\\\share\\\\a.exe\",\"sizeBytes\":12,\"sha256\":\"%H\"}")]
    [InlineData("{\"file\":\"\",\"sizeBytes\":12,\"sha256\":\"%H\"}")]
    public void A_malformed_or_escaping_lock_file_is_rejected_with_a_reason(string json)
    {
        using var dir = new TempDir();
        var lockPath = dir.File("test.lock.json");
        File.WriteAllText(lockPath, json.Replace("%H", new string('a', 64)));
        Assert.Throws<InvalidDataException>(() => PresentMonLockFile.Read(lockPath));
    }

    [Fact]
    public void A_nested_relative_file_resolves_below_the_lock_files_directory()
    {
        using var dir = new TempDir();
        var lockPath = dir.File("test.lock.json");
        File.WriteAllText(lockPath, "{\"file\":\"2.0/x.exe\",\"sizeBytes\":7,\"sha256\":\"" + new string('b', 64) + "\"}");
        var pin = PresentMonLockFile.Read(lockPath);
        Assert.Equal(Path.Combine(dir.Path, "2.0", "x.exe"), pin.Path);
        Assert.Equal(7, pin.SizeBytes);
    }
}
