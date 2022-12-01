using System.Text.RegularExpressions;
using MediatRMapper;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Cocona;

MSBuildLocator.RegisterDefaults();

CoconaApp.Run(RunMapper);

async Task RunMapper(string? solutionPath, bool verbose, bool stripNamespaces = true)
{
    var namespaceRegex = new Regex(@"\w+\.");
    var handlers = new List<HandlerInfo>();
    var senders = new Dictionary<string, List<string>>();
    var workspace = MSBuildWorkspace.Create();
    
    string GetHandler(string type)
    {
        var handler = handlers.FirstOrDefault(x => x.Response == type);
        if (handler is not null) return handler.Name;
        handler = handlers.FirstOrDefault(x => x.Request == type);

        return handler?.Name ?? "Unknown";
    }

    string GetRequest(string type)
    {
        var handler = handlers!.FirstOrDefault(x => x.Response == type);
        return handler?.Request ?? type;
    }

    string FormatTypeName(string type)
    {
        if (!stripNamespaces) return type;
        return namespaceRegex.Replace(type, string.Empty);
    }

    if (solutionPath is null)
    {
        var solutions = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.sln");
        solutionPath = solutions.FirstOrDefault();

        if (solutionPath is null)
        {
            Console.WriteLine("Solution file could not be located");
            return;
        }
    }
    
    var sln = await workspace.OpenSolutionAsync(solutionPath);
    foreach (var project in sln.Projects)
    {
        if (verbose) Console.WriteLine($"Processing {project.Name}");
        
        var compilation = await project.GetCompilationAsync();

        if (compilation is null) continue;
        
        foreach (var tree in compilation.SyntaxTrees)
        {
            var root = await tree.GetRootAsync();
            foreach (var typeDeclaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                var model = compilation.GetSemanticModel(typeDeclaration.SyntaxTree);
                var typeSymbol = model.GetDeclaredSymbol(typeDeclaration);

                var handlerInterface = typeSymbol!.Interfaces.FirstOrDefault(x =>
                    x is { Name: "IRequestHandler" or "INotificationHandler", ContainingNamespace.Name: "MediatR" });
                
                if (handlerInterface is not null)
                {
                    var requestType = handlerInterface.TypeArguments.First().ToString();
                    var responseType = handlerInterface.TypeArguments.Length == 1
                        ? null
                        : handlerInterface.TypeArguments.Last().ToString();
                    
                    handlers.Add(new(typeSymbol.ToString()!, requestType!, responseType));
                }

                var sentTypes = typeDeclaration.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Select(x => model.GetSymbolInfo(x).Symbol)
                    .Where(x => x!.ContainingNamespace.Name == "MediatR" && x.Name is "Send" or "Publish")
                    .Cast<IMethodSymbol>()
                    .Select(x => x.TypeArguments.First().ToString())
                    .ToList();

                senders[typeSymbol.ToString()!] = sentTypes!;
            }
        }
    }

    foreach (var (sender, types) in senders)
    {
        Console.WriteLine(FormatTypeName(sender));
        int count = 1;
        foreach (var type in types)
        {
            Console.WriteLine($" {(count == types.Count ? '└' : '├')} {FormatTypeName(GetRequest(type))} -> {FormatTypeName(GetHandler(type))}");
            count++;
        }
    }
}

