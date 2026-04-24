using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using DumbTrader.Models;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace DumbTrader.Services
{
    public class RoslynScriptRunner
    {
        private readonly ScriptOptions _options;
        private readonly ConcurrentDictionary<string, Script<object>> _scriptCache = new();

        public RoslynScriptRunner()
        {
            // 필요한 참조/네임스페이스만 명시적으로 추가 — 보안상 주의
            _options = ScriptOptions.Default
                .AddReferences(
                    typeof(object).Assembly,
                    typeof(System.Linq.Enumerable).Assembly,
                    typeof(Task).Assembly,
                    typeof(ScriptResult).Assembly)
                .AddImports(
                    "System",
                    "System.Linq",
                    "System.Collections.Generic",
                    "System.Threading.Tasks",
                    "DumbTrader.Models");
        }

        public async Task<object?> RunScriptAsync(string code, object? globals = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (timeout.HasValue)
                cts.CancelAfter(timeout.Value);

            var globalsType = globals?.GetType() ?? typeof(object);
            var key = $"{globalsType.FullName}:{ComputeHash(code)}";

            try
            {
                // 캐시에서 Script 객체를 가져오거나 새로 생성(처음 RunAsync 시 컴파일 발생)
                var script = _scriptCache.GetOrAdd(key, _ => CSharpScript.Create<object>(code, _options, globalsType));
                var state = await script.RunAsync(globals, cancellationToken: cts.Token).ConfigureAwait(false);
                return state.ReturnValue;
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("Script execution timed out.");
            }
            catch (CompilationErrorException cex)
            {
                var diag = string.Join(Environment.NewLine, cex.Diagnostics.Select(d => d.ToString()));
                throw new InvalidOperationException("Script compilation failed: " + Environment.NewLine + diag);
            }
        }

        public async Task<object?> RunScriptFromFileAsync(string filePath, object? globals = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Script file not found", filePath);

            var code = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
            var lastWriteTicks = File.GetLastWriteTimeUtc(filePath).Ticks;
            var globalsType = globals?.GetType() ?? typeof(object);
            var key = $"{filePath}:{lastWriteTicks}:{globalsType.FullName}";

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (timeout.HasValue)
                cts.CancelAfter(timeout.Value);

            try
            {
                var script = _scriptCache.GetOrAdd(key, _ => CSharpScript.Create<object>(code, _options, globalsType));
                var state = await script.RunAsync(globals, cancellationToken: cts.Token).ConfigureAwait(false);
                return state.ReturnValue;
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("Script execution timed out.");
            }
            catch (CompilationErrorException cex)
            {
                var diag = string.Join(Environment.NewLine, cex.Diagnostics.Select(d => d.ToString()));
                throw new InvalidOperationException("Script compilation failed: " + Environment.NewLine + diag);
            }
        }

        private static string ComputeHash(string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(bytes);
            return Convert.ToHexString(hash);
        }
    }
}

