// See https://aka.ms/new-console-template for more information

using MediatRMapper;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;

MSBuildLocator.RegisterDefaults();

var handlers = new List<HandlerInfo>();
var senders = new Dictionary<string, List<string>>();

var workspace = MSBuildWorkspace.Create();
var sln = await workspace.OpenSolutionAsync(args[0]);
foreach (var project in sln.Projects)
{
    Console.WriteLine($"Processing {project.Name}");
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
            
            var constructors = typeDeclaration.DescendantNodes().OfType<ConstructorDeclarationSyntax>().ToList();
            
            if (constructors.Count != 1) continue;
            
            var ctor = model.GetDeclaredSymbol(constructors.First());
            var mediatorParameter = ctor!.Parameters.FirstOrDefault(x => x.Type.Name == "IMediator");
         
            if (mediatorParameter is null) continue;
            
            var sentTypes = typeDeclaration.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Select(x => model.GetSymbolInfo(x).Symbol)
                .Where(x => x.ContainingNamespace.Name == "MediatR" && x.Name is "Send" or "Publish")
                .Cast<IMethodSymbol>()
                .Select(x => x.TypeArguments.First().ToString())
                .ToList();

            senders[typeSymbol.ToString()!] = sentTypes!;
        }
    }
}

Console.WriteLine("Handlers");
foreach (var handler in handlers)
{
    Console.WriteLine(" - " + handler.Name);
}

foreach (var (s, m) in senders)
{
    Console.WriteLine("Sender " + s);
    foreach (var mes in m)
    {
        Console.WriteLine(" - " + mes);
    }
}