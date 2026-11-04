using System.Text;
using System.Text.RegularExpressions;

namespace Jankscope.Core.Tests;

/// <summary>A named managed-API ban. <see cref="OnlyIfCodeMatches"/> scopes a rule to files that mention a type (for example <c>Process</c>).</summary>
internal sealed record BannedPattern(string Name, Regex Pattern, string Why, Regex? OnlyIfCodeMatches = null);

/// <summary>
/// One native-import attribute found in code, in any spelling. <see cref="Dll"/> and <see cref="EntryPoint"/> are set only when the
/// attribute gives both as plain string literals; anything else (a constant, a concatenation, an alias, a bare mention) leaves them
/// null, and the exact-set test then fails: an import the lint cannot read is an import nobody reviewed.
/// </summary>
internal sealed record NativeImport(string Token, string? Dll, string? EntryPoint)
{
    public bool IsFullyLiteral => Dll is not null && EntryPoint is not null;
}

/// <summary>
/// The lint rules as pure functions over scanned source, so the engine can be tested against evasion samples without touching the
/// repository (review #9). The symbol-based layer is the banned-API analyzer (BannedSymbols.txt); these rules are the independent
/// second layer, and the only layer that can read attribute arguments such as native entry points.
/// </summary>
internal static class ProductLints
{
    private static Regex R(string pattern) => new(pattern, RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Native and loader names that product code may never reference, even inside a string (an EntryPoint, a split literal).</summary>
    private static readonly string[] NativeNamePatterns =
    [
        // injection, hooks, foreign memory and threads (HC-1); Nt and Zw are the same system calls
        "SetWindowsHookEx[AW]?", "SetWinEventHook", "ReadProcessMemory", "WriteProcessMemory", "CreateRemoteThread(?:Ex)?",
        "VirtualAllocEx", "VirtualProtectEx", "Toolhelp32ReadProcessMemory", "RtlCreateUserThread",
        "(?:Nt|Zw)(?:OpenProcess|ReadVirtualMemory|WriteVirtualMemory|AllocateVirtualMemory|ProtectVirtualMemory|CreateThreadEx|" +
            "OpenThread|SetContextThread|SuspendProcess|ResumeProcess|SuspendThread|ResumeThread|TerminateProcess|TerminateThread|" +
            "DuplicateObject|QueueApcThread(?:Ex)?|LoadDriver|UnloadDriver)",
        "OpenProcess", "QueueUserAPC2?", "SetThreadContext", "OpenThread", "SuspendThread", "DebugActiveProcess", "DuplicateHandle",
        "MiniDumpWriteDump", "EnumProcessModules(?:Ex)?", "Module32(?:First|Next)W?", "GetModuleFileNameEx[AW]?", "QueryFullProcessImageName[AW]?",
        // launching and ending processes, synthetic input
        "TerminateProcess", "CreateProcess(?:AsUser|WithToken|WithLogon)?[AW]?", "ShellExecute(?:Ex)?[AW]?", "WinExec",
        "SendInput", "keybd_event", "mouse_event", "BlockInput", "SetCursorPos",
        // keystroke state (HC-4: no keylogging), process and window control, device I/O, system settings, network natives, tokens
        "GetAsyncKeyState", "GetKeyState", "GetKeyboardState",
        "SetPriorityClass", "SetProcessAffinityMask", "SetProcessWorkingSetSize(?:Ex)?", "EmptyWorkingSet", "(?:Nt|Zw)SetInformation(?:Process|Thread)",
        "PostMessage[AW]?", "SendMessage(?:Timeout|Callback)?[AW]?", "EndTask",
        "DeviceIoControl", "(?:Nt|Zw)DeviceIoControlFile",
        "(?:Nt|Zw)(?:SetValueKey|CreateKey|DeleteKey|DeleteValueKey)", "SH(?:SetValue|DeleteKey|DeleteValue)[AW]?", "RegSaveKey(?:Ex)?[AW]?", "RegLoadKey[AW]?", "RegRestoreKey[AW]?",
        "PowerSetActiveScheme", @"PowerWrite\w*ValueIndex", "SystemParametersInfo[AW]?", "ChangeDisplaySettings(?:Ex)?[AW]?", "(?:Nt|Zw)SetTimerResolution", "timeBeginPeriod",
        "WSAStartup", @"WinHttp[A-Z]\w+", @"Internet(?:Open|Connect|ReadFile)\w*", "URLDownloadToFile[AW]?", "getaddrinfo",
        "AdjustTokenPrivileges", "ImpersonateLoggedOnUser", "LogonUser[AW]?",
        // registry writes by native name (HC-8)
        "Reg(?:SetValue|SetKeyValue|SetValueEx|CreateKey|CreateKeyEx|DeleteKey|DeleteKeyEx|DeleteValue|DeleteKeyValue)[AW]?",
        // drivers and service control (HC-2): reading service state is allowed, changing it is not
        "NtLoadDriver", "CreateService(?:Ex)?[AW]?", "ChangeServiceConfig2?[AW]?", "ControlService(?:Ex)?[AW]?", "StartService[AW]?", "DeleteService",
        // dynamic loading would hide a native call from the exact-entry-point test
        "LoadLibrary(?:Ex)?[AW]?", "GetProcAddress", "NativeLibrary", @"Assembly\s*\.\s*Load(?:From|File)?", "AssemblyLoadContext", "LoadFromAssemblyPath",
        // the attribute form that cannot be reviewed as an exact set; LibraryImport is the only allowed spelling
        "DllImport(?:Attribute)?",
    ];

    private static readonly Regex NativeNames = new($@"\b(?:{string.Join("|", NativeNamePatterns)})\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex UsesProcessType = R(@"\bProcess\b");

    public static readonly IReadOnlyList<BannedPattern> ManagedBans =
    [
        // Launching and attaching to other processes (HC-1, HC-5)
        new("Process.Start", R(@"\bProcess\s*\.\s*Start\b"), "launches a process; only an allowlisted runner may"),
        new("ProcessStartInfo", R(@"\bProcessStartInfo\b"), "launches a process; only an allowlisted runner may"),
        new("new Process()", R(@"\bnew\s+(?:[\w.]+\.)?Process\s*[({]|\b(?:[\w.]+\.)?Process\s+\w+\s*=\s*new\s*\("), "launches or attaches to a process"),
        new("Process.GetProcessById", R(@"\bProcess\s*\.\s*GetProcessById\b"), "opens a foreign process by id"),
        new("Process.GetProcessesByName", R(@"\bProcess\s*\.\s*GetProcessesByName\b"), "foreign process objects by name"),
        new("Process members that open handles", R(@"\.\s*(?:MainModule|Modules|Handle|SafeHandle|StartTime|Kill|PriorityClass|ProcessorAffinity|MainWindowHandle|MainWindowTitle|HasExited|ExitCode|TotalProcessorTime|UserProcessorTime|CloseMainWindow|WaitForExit|EnableRaisingEvents|Threads|Responding|MaxWorkingSet|MinWorkingSet|StartInfo|StandardInput|StandardOutput)\b"),
            "each of these opens a handle to a foreign process or changes it (HC-1: names only)", UsesProcessType),
        // Other ways to start or control things on this machine without naming Process
        new("WMI methods, shell and COM activation", R(@"\b(?:ManagementClass|ManagementObject|InvokeMethod|Win32_Process|Win32_Service|Interaction\s*\.\s*Shell|GetTypeFromProgID|GetTypeFromCLSID)\b"),
            "WMI method calls, shell execution and COM activation can start or stop programs"),
        new("COM, remote and loader families", R(@"\b(?:ComImport|ComImportAttribute|CoClass|CoClassAttribute|GeneratedComInterface|GeneratedComClass|ComWrappers|GetObjectForIUnknown|GetTypedObjectForIUnknown|GetFunctionPointerForDelegate|UnsafeLoadFrom|AppDomain|NamedPipeClientStream|OpenRemoteBaseKey)\b|\bAssembly\s*\.\s*CreateInstance\b"),
            "COM activation, remote targets and run-time loading can start or reach code without naming a banned API"),
        new("code generation", R(@"\bSystem\s*\.\s*Reflection\s*\.\s*Emit\b|\b(?:ILGenerator|DynamicMethod|AssemblyBuilder)\b"), "run-time code generation hides calls from review"),
        new("dynamic keyword", R(@"\bdynamic\b"), "dynamic dispatch hides calls from every name- and symbol-based rule"),
        // Evasions a name-based lint would otherwise miss: renaming the type, or calling it without naming it
        new("Process type alias", R(@"\busing\s+(?:static\s+)?(?:\w+\s*=\s*)?(?:global\s*::\s*)?System\s*\.\s*Diagnostics\s*\.\s*Process\s*;"),
            "an alias hides Process.Start from the name-based rules"),
        new("reflection invocation", R(@"\.\s*(?:InvokeMember|GetMethod|GetMethods|GetMethodImpl)\s*\(|\bActivator\s*\.\s*CreateInstance\b|\bType\s*\.\s*GetType\s*\(|\bMethodInfo\b|\bDynamicInvoke\b"),
            "reflection could call a banned API without naming it"),
        // Network (HC-5, HC-7): nothing leaves the machine from this code base
        new("network types", R(@"\b(?:HttpClient|HttpClientHandler|SocketsHttpHandler|HttpMessageHandler|HttpListener|WebClient|WebRequest|HttpWebRequest|FtpWebRequest|TcpClient|TcpListener|UdpClient|Socket|NetworkStream|SmtpClient|ClientWebSocket|WebSocket|QuicConnection|QuicListener|QuicStream|Ping|Dns|NetworkInterface)\b"),
            "network access is not allowed until the network-probe stage allowlists a specific file"),
        new("network namespaces", R(@"\bSystem\s*\.\s*Net\s*\.\s*(?:Http|Sockets|NetworkInformation|WebSockets|Mail|Security|Quic)\b"), "network namespace"),
        // Registry writes (HC-8): posture is reported, never changed, until Stage 4
        new("registry writes", R(@"\.\s*(?:SetValue|CreateSubKey|DeleteValue|DeleteSubKey|DeleteSubKeyTree)\b|\bwritable\s*:\s*true\b|\bOpenSubKey\s*\([^()]*,\s*(?:true|RegistryKeyPermissionCheck|RegistryRights)"),
            "changing the registry is Stage 4 behind an approval flow; reads use OpenSubKey(string), which is read-only"),
    ];

    public static IReadOnlyList<string> NativeNameHits(ScannedSource source) =>
        NativeNames.Matches(source.CodeAndStrings).Select(m => m.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static IReadOnlyList<string> ManagedBanHits(ScannedSource source) =>
        ManagedBans
            .Where(b => (b.OnlyIfCodeMatches is null || b.OnlyIfCodeMatches.IsMatch(source.Code)) && b.Pattern.IsMatch(source.Code))
            .Select(b => b.Name)
            .ToList();

    // ---- the launcher file's exact use of System.Diagnostics.Process (review 1a, findings 1 and 18) ----
    // The launcher is exempt from the Process rules above, as a whole file. This pins what it may do with that exemption: a static member of
    // Process other than Start, any call of Kill with an argument (the tree form makes the runtime open a handle to every process on the
    // machine), and the members that look at a process from outside are all violations, whatever the variable is called.

    private static readonly Regex StaticProcessMember = R(@"\bProcess\s*\.\s*(\w+)");

    private static readonly Regex ForbiddenLauncherMember = R(
        @"\.\s*(?:MainModule|Modules|Handle|SafeHandle|StartTime|PriorityClass|ProcessorAffinity|MainWindowHandle|MainWindowTitle|TotalProcessorTime|UserProcessorTime|" +
        @"CloseMainWindow|EnableRaisingEvents|Threads|Responding|MaxWorkingSet|MinWorkingSet|StartInfo|GetProcesses|GetProcessById|GetProcessesByName|" +
        @"GetCurrentProcess|EnterDebugMode|LeaveDebugMode)\b|\bentireProcessTree\b");

    private static readonly Regex KillCall = R(@"\.\s*Kill\s*\(([^)]*)\)");

    public static IReadOnlyList<string> LauncherProcessMemberViolations(ScannedSource source)
    {
        var violations = new List<string>();
        violations.AddRange(StaticProcessMember.Matches(source.Code).Select(m => m.Groups[1].Value).Where(name => name != "Start").Select(name => "Process." + name));
        violations.AddRange(ForbiddenLauncherMember.Matches(source.Code).Select(m => m.Value.Replace(" ", string.Empty, StringComparison.Ordinal)));
        violations.AddRange(KillCall.Matches(source.Code).Where(m => m.Groups[1].Value.Trim().Length != 0).Select(m => "Kill(" + m.Groups[1].Value.Trim() + ")"));
        return violations.Distinct().ToList();
    }

    private static readonly Regex Words = R("[A-Z]+(?![a-z])|[A-Z]?[a-z]+|[0-9]+");

    /// <summary>
    /// Vendor terms of five or more characters match as substrings (so <c>isNvidia</c> and <c>NVIDIAAdapter</c> are found); shorter
    /// terms must equal a whole identifier word, where camelCase and underscores split words (<c>isAmd</c> hits, <c>madmin</c> does not).
    /// </summary>
    public static IReadOnlyList<string> VendorHits(ScannedSource source, IEnumerable<string> terms)
    {
        var lower = source.CodeAndStrings.ToLowerInvariant();
        var words = Words.Matches(source.CodeAndStrings).Select(m => m.Value.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        var hits = new List<string>();
        foreach (var term in terms.Select(t => t.ToLowerInvariant()).Distinct())
        {
            var hit = term.Length >= 5 ? lower.Contains(term, StringComparison.Ordinal) : words.Contains(term);
            if (hit)
            {
                hits.Add(term);
            }
        }

        return hits;
    }

    private static readonly Regex ImportToken = R(@"\b(?:LibraryImport|DllImport)(?:Attribute)?\b");
    private static readonly Regex PlainLiteral = R("^\\s*\"([^\"\\\\]+)\"\\s*$");
    private static readonly Regex EntryPointArgument = R(@"^\s*EntryPoint\s*=\s*(.*?)\s*$");

    /// <summary>
    /// Every mention of <c>LibraryImport</c> or <c>DllImport</c> (with or without <c>Attribute</c>, qualified or not, first in an
    /// attribute list or not), each parsed for its literal DLL and entry point. A mention that cannot be parsed is still returned,
    /// so the count of imports always equals the count of mentions.
    /// </summary>
    public static IReadOnlyList<NativeImport> NativeImports(ScannedSource source)
    {
        var imports = new List<NativeImport>();
        foreach (Match m in ImportToken.Matches(source.RawCode))
        {
            imports.Add(ParseImport(m.Value, source.Aligned, m.Index + m.Length));
        }

        // A mention that only appears after escape decoding (an identifier spelled with an escape sequence) cannot be located: unparsed.
        for (var extra = ImportToken.Matches(source.Code).Count - imports.Count; extra > 0; extra--)
        {
            imports.Add(new NativeImport("(escaped identifier)", null, null));
        }

        return imports;
    }

    private static NativeImport ParseImport(string token, string text, int position)
    {
        var unparsed = new NativeImport(token, null, null);
        var i = position;
        while (i < text.Length && char.IsWhiteSpace(text[i]))
        {
            i++;
        }

        if (i >= text.Length || text[i] != '(')
        {
            return unparsed; // a using alias, typeof(...), or an attribute without arguments
        }

        var args = new List<string>();
        var current = new StringBuilder();
        var depth = 0;
        for (i++; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"' || (c == '@' && i + 1 < text.Length && text[i + 1] == '"'))
            {
                var verbatim = c == '@';
                current.Append(c);
                if (verbatim)
                {
                    i++;
                    current.Append('"');
                }

                for (i++; i < text.Length; i++)
                {
                    current.Append(text[i]);
                    if (!verbatim && text[i] == '\\' && i + 1 < text.Length)
                    {
                        current.Append(text[++i]);
                    }
                    else if (text[i] == '"')
                    {
                        if (verbatim && i + 1 < text.Length && text[i + 1] == '"')
                        {
                            current.Append(text[++i]);
                            continue;
                        }

                        break;
                    }
                }
            }
            else if (c == '$')
            {
                return unparsed; // interpolated or raw: not a plain literal
            }
            else if (c is '(' or '[' or '{')
            {
                depth++;
                current.Append(c);
            }
            else if (c is ')' or ']' or '}')
            {
                if (depth == 0)
                {
                    if (c != ')')
                    {
                        return unparsed;
                    }

                    args.Add(current.ToString());
                    return Interpret(token, args);
                }

                depth--;
                current.Append(c);
            }
            else if (c == ',' && depth == 0)
            {
                args.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        return unparsed;
    }

    private static NativeImport Interpret(string token, List<string> args)
    {
        var dll = args.Count > 0 ? PlainLiteral.Match(args[0]) : Match.Empty;
        string? entry = null;
        foreach (var arg in args.Skip(1))
        {
            var e = EntryPointArgument.Match(arg);
            if (e.Success)
            {
                var literal = PlainLiteral.Match(e.Groups[1].Value);
                entry = literal.Success ? literal.Groups[1].Value : null;
            }
        }

        return new NativeImport(token, dll.Success ? dll.Groups[1].Value.ToLowerInvariant() : null, dll.Success ? entry : null);
    }

    public static bool DeclaresSystem32OnlyDllSearch(ScannedSource source) =>
        Regex.IsMatch(source.Code, @"\[\s*assembly\s*:\s*DefaultDllImportSearchPaths\s*\(\s*DllImportSearchPath\s*\.\s*System32\s*\)\s*\]");
}
