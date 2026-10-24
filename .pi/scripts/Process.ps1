# Shared bounded process runner. Dot-source from PowerShell 7; no shell evaluation.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))

if (-not ('BrutalSupport.ProcessRunner' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
namespace BrutalSupport {
    public sealed class ProcessResult {
        public int ExitCode;
        public bool TimedOut;
        public bool OutputTruncated;
        public string Output = "";
        public string[] GogWarnings = Array.Empty<string>();
    }
    public static class ProcessRunner {
        public static ProcessResult Run(string file, string[] args, string cwd, int seconds) {
            var result = new ProcessResult();
            var text = new StringBuilder();
            var warnings = new HashSet<string>();
            var gate = new object();
            var start = new ProcessStartInfo(file) {
                WorkingDirectory = cwd, UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var arg in args) start.ArgumentList.Add(arg);
            using (var process = new Process { StartInfo = start }) {
                DataReceivedEventHandler receive = (_, e) => {
                    if (e.Data == null) return;
                    lock (gate) {
                        if (e.Data.IndexOf("warning", StringComparison.OrdinalIgnoreCase) >= 0 &&
                            (e.Data.IndexOf("GOG", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             e.Data.IndexOf("GalaxyOverlayVkLayer", StringComparison.OrdinalIgnoreCase) >= 0) &&
                            e.Data.IndexOf("naming", StringComparison.OrdinalIgnoreCase) >= 0 && warnings.Count < 10)
                            warnings.Add(e.Data.Substring(0, Math.Min(1000, e.Data.Length)));
                        text.AppendLine(e.Data);
                        if (text.Length > 8000) {
                            text.Remove(0, text.Length - 8000);
                            result.OutputTruncated = true;
                        }
                    }
                };
                var stdoutDone = new ManualResetEventSlim(false);
                var stderrDone = new ManualResetEventSlim(false);
                process.OutputDataReceived += (s, e) => { if (e.Data == null) stdoutDone.Set(); else receive(s, e); };
                process.ErrorDataReceived += (s, e) => { if (e.Data == null) stderrDone.Set(); else receive(s, e); };
                process.Start();
                try {
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    if (!process.WaitForExit(seconds * 1000)) {
                        result.TimedOut = true;
                        process.Kill(true);
                        if (!process.WaitForExit(5000)) throw new TimeoutException("Process tree did not exit after kill.");
                    }
                    // Bound drain time too: descendants might retain inherited pipe handles.
                    if (!stdoutDone.Wait(1000) || !stderrDone.Wait(1000)) result.OutputTruncated = true;
                    result.ExitCode = result.TimedOut ? 124 : process.ExitCode;
                    lock (gate) {
                        result.Output = text.ToString();
                        result.GogWarnings = new List<string>(warnings).ToArray();
                    }
                } finally {
                    if (!process.HasExited) process.Kill(true);
                }
            }
            return result;
        }
    }
}
'@
}

function Invoke-BoundedProcess {
    param(
        [Parameter(Mandatory)][string] $File,
        [string[]] $Arguments = @(),
        [string] $WorkingDirectory = $script:RepositoryRoot,
        [ValidateRange(1, 600)][int] $TimeoutSeconds = 120
    )
    $result = [BrutalSupport.ProcessRunner]::Run($File, $Arguments, $WorkingDirectory, $TimeoutSeconds)
    [pscustomobject]@{
        command = $File
        arguments = $Arguments
        cwd = $WorkingDirectory
        exitCode = $result.ExitCode
        timedOut = $result.TimedOut
        outputTruncated = $result.OutputTruncated
        outputTail = $result.Output
        gogNamingWarnings = @($result.GogWarnings)
    }
}
