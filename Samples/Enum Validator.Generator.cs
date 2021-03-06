using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace SourceGenerator
{
    [Generator]
    public class Generator : ISourceGenerator
    {
        private const string EnumValidatorStub = @"
namespace EnumValidation
{ 
    internal static class EnumValidator
    {
        public static void Validate(System.Enum enumToValidate)
        {
            // This will be filled in by the generator once you call EnumValidator.Validate()
            // Trust me.
        }
    }
}
";

        public void Initialize(GeneratorInitializationContext context)
        {
            // I should probably put something here
        }

        public void Execute(GeneratorExecutionContext context)
        {
            var compilation = GenerateHelperClasses(context);

            var enumValidatorType = compilation.GetTypeByMetadataName("EnumValidation.EnumValidator")!;

            var infos = GetEnumValidationInfo(compilation, enumValidatorType);

            if (infos.Any())
            {
                var sb = new StringBuilder();
                sb.AppendLine(@"namespace EnumValidation
{ 
    internal static class EnumValidator
    {");

                foreach (var info in infos)
                {
                    sb.AppendLine("        public static void Validate(" + info.EnumType.ToString() + " enumToValidate)");
                    sb.AppendLine("        {");

                    GenerateValidator(sb, info, "            ");

                    sb.AppendLine("        }");
                    sb.AppendLine();
                }

                sb.AppendLine(@"    }
}");

                context.AddSource("Validation.cs", sb.ToString());
            }
            else
            {
                context.AddSource("Validator.cs", EnumValidatorStub);
            }
        }

        private void GenerateValidator(StringBuilder sb, EnumValidationInfo info, string indent)
        {
            sb.AppendLine($"{indent}int intValue = (int)enumToValidate;");
            foreach ((int min, int max) in GetElementSets(info.Elements))
            {
                sb.AppendLine($"{indent}if (intValue >= {min} && intValue <= {max}) return;");
            }
            sb.AppendLine($"{indent}throw new System.ComponentModel.InvalidEnumArgumentException(\"{info.ArgumentName}\", intValue, typeof({info.EnumType}));");
        }

        private IEnumerable<(int min, int max)> GetElementSets(List<(string Name, int Value)> elements)
        {
            int min = 0;
            int? max = null;
            foreach (var info in elements)
            {
                if (max == null || info.Value != max + 1)
                {
                    if (max != null)
                    {
                        yield return (min, max.Value);
                    }
                    min = info.Value;
                    max = info.Value;
                }
                else
                {
                    max = info.Value;
                }
            }
            yield return (min, max.Value);
        }

        private static IEnumerable<EnumValidationInfo> GetEnumValidationInfo(Compilation compilation, INamedTypeSymbol enumValidatorType)
        {
            foreach (SyntaxTree? tree in compilation.SyntaxTrees)
            {
                var semanticModel = compilation.GetSemanticModel(tree);
                foreach (var invocation in tree.GetRoot().DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
                {
                    var symbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                    if (symbol == null)
                    {
                        continue;
                    }

                    if (SymbolEqualityComparer.Default.Equals(symbol.ContainingType, enumValidatorType))
                    {
                        // Note: This assumes the only method on enumValidatorType is the one we want.
                        // ie, I'm too lazy to check which invocation is being made :)
                        var argument = invocation.ArgumentList.Arguments.First().Expression;
                        var enumType = semanticModel.GetTypeInfo(argument).Type;
                        if (enumType == null)
                        {
                            continue;
                        }

                        var info = new EnumValidationInfo(enumType, argument.ToString());
                        foreach (var member in enumType.GetMembers())
                        {
                            if (member is IFieldSymbol
                                {
                                    IsStatic: true,
                                    IsConst: true,
                                    ConstantValue: int value
                                } field)
                            {
                                info.Elements.Add((field.Name, value));
                            }
                        }

                        info.Elements.Sort((e1, e2) => e1.Value.CompareTo(e2.Value));

                        yield return info;
                    }
                }
            }
        }

        private static Compilation GenerateHelperClasses(GeneratorExecutionContext context)
        {
            var compilation = context.Compilation;

            var options = (compilation as CSharpCompilation)?.SyntaxTrees[0].Options as CSharpParseOptions;
            var tempCompilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(SourceText.From(EnumValidatorStub, Encoding.UTF8), options));

            return tempCompilation;
        }

        private class EnumValidationInfo
        {
            public List<(string Name, int Value)> Elements = new();

            public ITypeSymbol EnumType { get; }
            public string ArgumentName { get; internal set; }

            public EnumValidationInfo(ITypeSymbol enumType, string argumentName)
            {
                this.EnumType = enumType;
                this.ArgumentName = argumentName;
            }
        }
    }
}
