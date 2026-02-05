using System.IO;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace DumbTrader.Services
{
    public class RoslynScriptRunner
    {
        private readonly ScriptOptions _options;

        public RoslynScriptRunner()
        {
            // 필요한 참조/네임스페이스만 명시적으로 추가 — 보안상 주의
            _options = ScriptOptions.Default
                .AddReferences(
                    typeof(object).Assembly,
                    typeof(System.Linq.Enumerable).Assembly,
                    typeof(Task).Assembly)
                .AddImports("System", "System.Linq", "System.Collections.Generic", "System.Threading.Tasks");
        }

        public async Task<object?> RunScriptAsync(string code, object? globals = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (timeout.HasValue)
                cts.CancelAfter(timeout.Value);

            try
            {
                return await CSharpScript.EvaluateAsync<object>(code, _options, globals, cancellationToken: cts.Token).ConfigureAwait(false);
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
            return await RunScriptAsync(code, globals, timeout, cancellationToken).ConfigureAwait(false);
        }
    }
}
