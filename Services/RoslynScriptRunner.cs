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
        private readonly ConcurrentDictionary<(Type Globals, string Hash), Lazy<ScriptRunner<object>>> _scriptCache = new();

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
            var key = (globalsType, ComputeHash(code));

            try
            {
                cts.Token.ThrowIfCancellationRequested();
                // Retain the executable delegate so completed compilations can be collected.
                var runner = _scriptCache.GetOrAdd(key, _ => new Lazy<ScriptRunner<object>>(
                    () => CSharpScript.Create<object>(code, _options, globalsType).CreateDelegate())).Value;
                return await runner(globals, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
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

            var code = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            return await RunScriptAsync(code, globals, timeout, cancellationToken).ConfigureAwait(false);
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

