using System;
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Overby.LazyProps;

/// <summary>
/// A sample source generator that creates a custom report based on class properties. The target class should be annotated with the 'Generators.ReportAttribute' attribute.
/// When using the source code as a baseline, an incremental source generator is preferable because it reduces the performance overhead.
/// </summary>
[Generator]
public class LazyPropGenerator : IIncrementalGenerator
{
    private const string Namespace = "Overby.LazyProps";
    private const string AttributeName = "LazyPropAttribute";

    private const string PropertyNamePropertyName = "PropertyName";
    private const string ThreadSafePropertyName = "ThreadSafe";
    private const string FieldPrefixPropertyName = "FieldPrefix";

    private const string AttributeSourceCode = $@"// <auto-generated/>

namespace {Namespace}
{{
    [System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = true)]
    public class {AttributeName}(string propertyName) : System.Attribute
    {{
        public string {PropertyNamePropertyName} {{ get; }} = propertyName;
        public bool {ThreadSafePropertyName} {{ get; set; }}
        public string {FieldPrefixPropertyName} {{ get; set; }}
    }}
}}";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Add the marker attribute to the compilation.
        context.RegisterPostInitializationOutput(ctx => ctx.AddSource(
            "LazyPropAttribute.g.cs",
            SourceText.From(AttributeSourceCode, Encoding.UTF8)));

        // Filter classes annotated with the [Report] attribute. Only filtered Syntax Nodes can trigger code generation.
        var provider = context.SyntaxProvider
            .CreateSyntaxProvider(
                (s, _) => s is MethodDeclarationSyntax
                          {
                              AttributeLists.Count: > 0,
                              ParameterList.Parameters.Count: 0,
                              ReturnType: not PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.VoidKeyword },
                              Parent: TypeDeclarationSyntax typeDecl,
                          }
                          && typeDecl.Modifiers.Any(SyntaxKind.PartialKeyword),
                (ctx, _) => GetMethodDeclarationForSourceGen(ctx))
            .Where(t => t.lazyPropAttributeFound)
            .Select((t, _) => t.Item1);

        // Generate the source code.
        context.RegisterSourceOutput(context.CompilationProvider.Combine(provider.Collect()),
            (ctx, t) => GenerateCode(ctx, t.Left, t.Right));
    }

    /// <summary>
    /// Checks whether the Node is annotated with the [LazyProp] attribute and maps syntax context to the specific node type (MethodDeclarationSyntax).
    /// </summary>
    /// <param name="context">Syntax context, based on CreateSyntaxProvider predicate</param>
    /// <returns>The specific cast and whether the attribute was found.</returns>
    private static (MethodDeclarationSyntax, bool lazyPropAttributeFound) GetMethodDeclarationForSourceGen(
        GeneratorSyntaxContext context)
    {
        var methodDeclarationSyntax = (MethodDeclarationSyntax)context.Node;

        // Go through all attributes of the method.
        foreach (AttributeListSyntax attributeListSyntax in methodDeclarationSyntax.AttributeLists)
        foreach (AttributeSyntax attributeSyntax in attributeListSyntax.Attributes)
        {
            if (ModelExtensions.GetSymbolInfo(context.SemanticModel, attributeSyntax).Symbol is not IMethodSymbol
                attributeSymbol)
                continue; // if we can't get the symbol, ignore it

            string attributeName = attributeSymbol.ContainingType.ToDisplayString();

            // Check the full name of the [LazyProp] attribute.
            if (attributeName == $"{Namespace}.{AttributeName}")
                return (methodDeclarationSyntax, true);
        }

        return (methodDeclarationSyntax, false);
    }

    /// <summary>
    /// Generate code action.
    /// It will be executed on specific nodes (MethodDeclarationSyntax annotated with the [LazyProp] attribute) changed by the user.
    /// </summary>
    /// <param name="context">Source generation context used to add source files.</param>
    /// <param name="compilation">Compilation used to provide access to the Semantic Model.</param>
    /// <param name="methodDeclarations">Nodes annotated with the [Report] attribute that trigger the generate action.</param>
    private void GenerateCode(SourceProductionContext context, Compilation compilation,
        ImmutableArray<MethodDeclarationSyntax> methodDeclarations)
    {
        // Go through all filtered method declarations.
        foreach (var methodDeclarationSyntax in methodDeclarations)
        {
            if (methodDeclarationSyntax.Parent is not TypeDeclarationSyntax { Keyword: var typeKeyword })
                continue;

            var semanticModel = compilation.GetSemanticModel(methodDeclarationSyntax.SyntaxTree);
            if (ModelExtensions.GetDeclaredSymbol(semanticModel, methodDeclarationSyntax) is not IMethodSymbol
                methodSymbol)
                continue;

            var containingType = methodSymbol.ContainingType;

            var containingTypeName = containingType.Name;
            var methodName = methodDeclarationSyntax.Identifier.Text;
            var propertyCode = new StringBuilder();

            // loop over attrs
            foreach (var attr in methodSymbol.GetAttributes())
            {
                var attributeName = attr.AttributeClass?.ToDisplayString();
                if (attributeName != $"{Namespace}.{AttributeName}")
                    continue;

                if (attr.ConstructorArguments.Length != 1)
                    continue;

                var propName = (string)attr.ConstructorArguments[0].Value;
                propName = propName.Trim();

                var propType = methodSymbol.ReturnType.ToDisplayString();
                var threadSafe = false;
                var fieldPrefix = $"_{char.ToLower(propName[0])}{propName.Substring(1)}";

                foreach (var namedArg in attr.NamedArguments)
                {
                    if (namedArg is { Key: ThreadSafePropertyName, Value.Value: bool b })
                    {
                        threadSafe = b;
                    }
                    else if (namedArg is { Key: FieldPrefixPropertyName, Value.Value: string s })
                    {
                        fieldPrefix = s;
                    }
                }

                var storageVariable = $"{fieldPrefix}Storage";
                var writtenVariable = $"{fieldPrefix}Written";
                propertyCode.AppendLine($"  private {propType} {storageVariable};");
                propertyCode.AppendLine($"  private bool {writtenVariable};");

                if (threadSafe)
                {
                    var mutexVariable = $"{fieldPrefix}Mutex";
                    propertyCode.AppendLine($$"""
                                                  private readonly object {{mutexVariable}} = new object();
                                                  public {{propType}} {{propName}}
                                                  {
                                                      get
                                                      {
                                                          if({{writtenVariable}})
                                                              return {{storageVariable}};
                                                              
                                                          lock({{mutexVariable}})
                                                          {
                                                              if({{writtenVariable}} == false)
                                                              {
                                                                  {{storageVariable}} = {{methodName}}();
                                                                  {{writtenVariable}} = true;
                                                              }
                                                          }
                                                          
                                                          return {{storageVariable}};
                                                      }
                                                  }
                                              """);
                }
                else
                {
                    propertyCode.AppendLine($$"""
                                                  public {{propType}} {{propName}}
                                                  {
                                                      get
                                                      {
                                                          if({{writtenVariable}} == false)
                                                          {
                                                              {{storageVariable}} = {{methodName}}();
                                                              {{writtenVariable}} = true;
                                                          }
                                              
                                                          return {{storageVariable}};
                                                      }
                                                  }
                                              """);
                }
            }

            var namespaceName = methodSymbol.ContainingNamespace.ToDisplayString();
            var code = $@"// <auto-generated/>

using System;
using System.Collections.Generic;

namespace {namespaceName};

partial {typeKeyword.Text} {containingTypeName}
{{
    {propertyCode}
}}
";
            

            // Add the source code to the compilation.
            context.AddSource($"{namespaceName}.{containingTypeName}.{methodName}.g.cs", SourceText.From(code, Encoding.UTF8));
        }
    }
}

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
sealed class LazyPropAttribute(string propertyName) : Attribute
{
    public string PropertyName { get; } = propertyName;
    public bool ThreadSafe { get; set; }
    public string FieldPrefix { get; set; }
}