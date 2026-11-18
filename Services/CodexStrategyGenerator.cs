using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using DumbTrader.Models;

namespace DumbTrader.Services;

public sealed record GeneratedStrategy(string Code, string Summary);

public interface ICodexStrategyGenerator
{
    Task<GeneratedStrategy> GenerateAsync(Guid id, string prompt, StrategyEvolutionOptions options, CancellationToken token);
}

public sealed class CodexStrategyGenerator(StrategyCandidateRepository repository) : ICodexStrategyGenerator
{
    private const string ResponseSchema = """
        {"type":"object","properties":{"Code":{"type":"string"},"Summary":{"type":"string"}},
         "required":["Code","Summary"],"additionalProperties":false}
        """;

    public async Task<GeneratedStrategy> GenerateAsync(Guid id, string prompt, StrategyEvolutionOptions options, CancellationToken token)
    {
        var root = string.IsNullOrWhiteSpace(options.WorkRoot) ? Path.Combine(repository.Root, "jobs") : options.WorkRoot;
        var work = Directory.CreateDirectory(Path.Combine(Path.GetFullPath(root), id.ToString("N"))).FullName;
        var output = Path.Combine(work, "response.json");
        var schema = Path.Combine(work, "schema.json");
        await File.WriteAllTextAsync(schema, ResponseSchema, token).ConfigureAwait(false);
        var info = new ProcessStartInfo
        {
            FileName = ResolveExecutable(options.CliPath),
            WorkingDirectory = work,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in new[] { "exec", "--cd", work, "--skip-git-repo-check", "--sandbox", "read-only",
            "--ephemeral", "--ignore-user-config", "--ignore-rules", "--disable", "shell_tool",
            "--disable", "enable_mcp_apps", "-c", "approval_policy=\"never\"", "-c", "web_search=\"disabled\"",
            "--output-schema", schema, "--output-last-message", output, "--json" })
            info.ArgumentList.Add(argument);
        if (!string.IsNullOrWhiteSpace(options.Model))
        {
            info.ArgumentList.Add("--model");
            info.ArgumentList.Add(options.Model);
        }
        info.ArgumentList.Add("-");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        using var process = new Process { StartInfo = info };
        var clock = Stopwatch.StartNew();
        var outcome = "실행 시작 실패";
        int? exitCode = null;
        try
        {
            token.ThrowIfCancellationRequested();
            if (!process.Start()) throw new InvalidOperationException("Codex CLI를 시작하지 못했습니다.");
            var stdout = DrainAsync(process.StandardOutput, deadline.Token);
            var stderr = DrainAsync(process.StandardError, deadline.Token);
            try
            {
                await process.StandardInput.WriteAsync(prompt.AsMemory(), deadline.Token).ConfigureAwait(false);
                process.StandardInput.Close();
                await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
                exitCode = process.ExitCode;
                var diagnostic = (await stderr.ConfigureAwait(false)) + (await stdout.ConfigureAwait(false));
                if (exitCode != 0)
                    throw new InvalidOperationException(ClassifyFailure(diagnostic, exitCode.Value));
                if (!File.Exists(output) || new FileInfo(output).Length is 0 or > 65536)
                    throw new InvalidOperationException("Codex 응답이 비어 있거나 크기 제한을 초과했습니다.");
                var result = JsonSerializer.Deserialize<GeneratedStrategy>(await File.ReadAllTextAsync(output, deadline.Token).ConfigureAwait(false));
                if (result is null || string.IsNullOrWhiteSpace(result.Code) || result.Code.Length > 4096
                    || string.IsNullOrWhiteSpace(result.Summary) || result.Summary.Length > 2000)
                    throw new InvalidOperationException("Codex 응답이 전략 소스와 변경 설명 규약에 맞지 않습니다.");
                outcome = "성공";
                return result;
            }
            finally
            {
                deadline.Cancel();
                try
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) when (process.HasExited) { }
                await Task.WhenAll((Task)stdout, stderr).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            outcome = "시간 초과";
            throw new TimeoutException("Codex 후보 생성 시간이 초과되었습니다.");
        }
        catch (OperationCanceledException) { outcome = "취소됨"; throw; }
        catch (JsonException)
        {
            outcome = "잘못된 JSON 응답";
            throw new InvalidOperationException("Codex 응답 JSON을 읽을 수 없습니다.");
        }
        catch (InvalidOperationException ex) { outcome = ex.Message; throw; }
        finally
        {
            repository.WriteArtifact(id, "codex-run.json", JsonSerializer.Serialize(new
            {
                Outcome = outcome, ExitCode = exitCode, DurationMs = clock.ElapsedMilliseconds,
                Model = options.Model, options.TimeoutSeconds
            }));
            File.Delete(output);
        }
    }

    private static string ResolveExecutable(string configured)
    {
        if (!string.Equals(configured, "codex.exe", StringComparison.OrdinalIgnoreCase))
        {
            if (!Path.IsPathFullyQualified(configured) || !Path.GetExtension(configured).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Codex CLI는 실행 파일(.exe)의 전체 경로를 지정하세요.");
            return configured;
        }
        var npm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm",
            "node_modules", "@openai", "codex", "node_modules", "@openai");
        if (Directory.Exists(npm))
        {
            var architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
            var vendor = Path.Combine(npm, "codex-win32-" + architecture, "vendor");
            if (Directory.Exists(vendor))
            {
                var native = Directory.EnumerateFiles(vendor, "codex.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (native is not null) return native;
            }
        }
        return configured;
    }

    private static async Task<string> DrainAsync(StreamReader reader, CancellationToken token)
    {
        var buffer = new char[2048];
        var tail = new StringBuilder();
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
        {
            tail.Append(buffer, 0, read);
            if (tail.Length > 8192) tail.Remove(0, tail.Length - 8192);
        }
        return tail.ToString();
    }

    private static string ClassifyFailure(string output, int exitCode)
    {
        var reason = output.Contains("401", StringComparison.OrdinalIgnoreCase) || output.Contains("auth", StringComparison.OrdinalIgnoreCase)
            ? "Codex 인증을 확인하세요."
            : output.Contains("model", StringComparison.OrdinalIgnoreCase) ? "Codex 모델 또는 연결 설정을 확인하세요."
            : "Codex CLI 실행에 실패했습니다.";
        return $"{reason} 종료 코드 {exitCode}";
    }
}
