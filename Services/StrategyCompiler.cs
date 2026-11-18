using System.Globalization;
using DumbTrader.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DumbTrader.Services;

public sealed class StrategyCompiler
{
    public const string Baseline = "Count < 21 ? 0 : PreviousFast <= PreviousSlow && Fast > Slow ? 1 : PreviousFast >= PreviousSlow && Fast < Slow ? -1 : 0";
    public const string StockBaseline = "Count < 21 ? 0 : PreviousFast <= PreviousSlow && Fast > Slow ? 1 : PreviousFast >= PreviousSlow && Fast < Slow && Position > 0 ? -1 : 0";
    public const string Contract = """
        Return one C# numeric expression, or a tuple (quantity, nextState). Both values must be integers.
        Positive quantity means BUY, negative means SELL, zero means HOLD.
        State starts at zero and persists within each run. Use only finite numeric literals,
        parentheses, arithmetic (+ - * / %), comparisons, && || ! and conditionals (?:).
        Double inputs: Open, High, Low, Close, Volume, Fast (SMA 5), Slow (SMA 20),
        PreviousFast, PreviousSlow, Volatility (mean absolute close change over up to 20 observations),
        AverageVolume (20), Count, Position (signed quantity), State. Wait for Count >= 21 to use the averages.
        The host sets the reason and enforces position and quantity limits. No calls, members, statements or directives.
        """;

    private static readonly string[] Inputs =
        ["Open", "High", "Low", "Close", "Volume", "Fast", "Slow", "PreviousFast", "PreviousSlow",
         "Volatility", "AverageVolume", "Count", "Position", "State"];
    private static readonly MetadataReference Core = MetadataReference.CreateFromFile(typeof(double).Assembly.Location);

    public Func<double[], StrategySignal> Compile(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 4096)
            throw new InvalidOperationException("전략 식은 1~4096자여야 합니다.");

        var syntax = SyntaxFactory.ParseExpression(code);
        var diagnostics = syntax.GetDiagnostics().Select(item => item.ToString()).ToArray();
        if (diagnostics.Length > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, diagnostics));
        if (syntax.ContainsDirectives || syntax.DescendantNodes().Take(257).Count() > 256)
            throw new InvalidOperationException("전략 식이 허용 범위를 초과했습니다.");

        var quantityNode = syntax;
        ExpressionSyntax stateNode = SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(0));
        var hasState = syntax is TupleExpressionSyntax;
        if (syntax is TupleExpressionSyntax tuple)
        {
            if (tuple.Arguments.Count != 2 || tuple.Arguments.Any(arg => arg.NameColon is not null))
                throw new InvalidOperationException("전략 튜플은 (수량, 상태) 형식이어야 합니다.");
            quantityNode = tuple.Arguments[0].Expression;
            stateNode = tuple.Arguments[1].Expression;
        }
        var quantity = Build(quantityNode);
        var state = Build(stateNode);
        var normalized = new NumericLiteralRewriter();
        var parameters = string.Join(", ", Inputs.Select(name => $"double {name}"));
        var tree = CSharpSyntaxTree.ParseText($$"""
            static class Candidate {
                static double Quantity({{parameters}}) => {{normalized.Visit(quantityNode)!.WithoutTrivia()}};
                static double StateValue({{parameters}}) => {{normalized.Visit(stateNode)!.WithoutTrivia()}};
            }
            """);
        var errors = CSharpCompilation.Create("Candidate", [tree], [Core],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .GetDiagnostics().Where(item => item.Severity == DiagnosticSeverity.Error).ToArray();
        if (errors.Length > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, errors.Select(item => item.ToString())));

        // Execute only host-created delegates for the verified syntax. Candidate IL is never loaded.
        return input =>
        {
            var signed = Integer(quantity(input));
            var nextState = Integer(hasState ? state(input) : input[^1]);
            var action = signed > 0 ? StrategyAction.Buy : signed < 0 ? StrategyAction.Sell : StrategyAction.Hold;
            return new StrategySignal(action, Math.Abs(signed), "전략 식", nextState);
        };
    }

    private static int Integer(double value)
    {
        if (!double.IsFinite(value) || value != Math.Truncate(value) || Math.Abs(value) > int.MaxValue)
            throw new InvalidOperationException("전략은 유효한 정수 수량과 상태를 반환해야 합니다.");
        return (int)value;
    }

    private static Func<double[], double> Build(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.NumericLiteralExpression):
                var value = Convert.ToDouble(literal.Token.Value, CultureInfo.InvariantCulture);
                if (double.IsFinite(value)) return _ => value;
                break;
            case IdentifierNameSyntax identifier:
                var index = Array.IndexOf(Inputs, identifier.Identifier.ValueText);
                if (index >= 0) return input => input[index];
                break;
            case ParenthesizedExpressionSyntax parenthesized:
                return Build(parenthesized.Expression);
            case ConditionalExpressionSyntax conditional:
                var test = Build(conditional.Condition);
                var whenTrue = Build(conditional.WhenTrue);
                var whenFalse = Build(conditional.WhenFalse);
                return input => test(input) != 0 ? whenTrue(input) : whenFalse(input);
            case PrefixUnaryExpressionSyntax unary:
                var operand = Build(unary.Operand);
                return unary.Kind() switch
                {
                    SyntaxKind.UnaryPlusExpression => operand,
                    SyntaxKind.UnaryMinusExpression => input => -operand(input),
                    SyntaxKind.LogicalNotExpression => input => Bool(operand(input) == 0),
                    _ => throw Invalid(unary)
                };
            case BinaryExpressionSyntax binary:
                return BuildBinary(binary);
        }
        throw Invalid(expression);
    }

    private static Func<double[], double> BuildBinary(BinaryExpressionSyntax binary)
    {
        var left = Build(binary.Left);
        var right = Build(binary.Right);
        return binary.Kind() switch
        {
            SyntaxKind.AddExpression => input => left(input) + right(input),
            SyntaxKind.SubtractExpression => input => left(input) - right(input),
            SyntaxKind.MultiplyExpression => input => left(input) * right(input),
            SyntaxKind.DivideExpression => input => left(input) / right(input),
            SyntaxKind.ModuloExpression => input => left(input) % right(input),
            SyntaxKind.EqualsExpression => input => Bool(left(input) == right(input)),
            SyntaxKind.NotEqualsExpression => input => Bool(left(input) != right(input)),
            SyntaxKind.LessThanExpression => input => Bool(left(input) < right(input)),
            SyntaxKind.LessThanOrEqualExpression => input => Bool(left(input) <= right(input)),
            SyntaxKind.GreaterThanExpression => input => Bool(left(input) > right(input)),
            SyntaxKind.GreaterThanOrEqualExpression => input => Bool(left(input) >= right(input)),
            SyntaxKind.LogicalAndExpression => input => Bool(left(input) != 0 && right(input) != 0),
            SyntaxKind.LogicalOrExpression => input => Bool(left(input) != 0 || right(input) != 0),
            _ => throw Invalid(binary)
        };
    }

    private static double Bool(bool value) => value ? 1 : 0;
    private static InvalidOperationException Invalid(SyntaxNode node)
        => new($"허용되지 않는 전략 구문: {node.Kind()}");

    private sealed class NumericLiteralRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode VisitLiteralExpression(LiteralExpressionSyntax node)
        {
            if (!node.IsKind(SyntaxKind.NumericLiteralExpression)) return node;
            var value = Convert.ToDouble(node.Token.Value, CultureInfo.InvariantCulture);
            var text = value.ToString("R", CultureInfo.InvariantCulture) + "D";
            return SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(text, value)).WithTriviaFrom(node);
        }
    }
}
