using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Simplification;
using Microsoft.CodeAnalysis.Text;
using Soenneker.Extensions.Task;
using Soenneker.Extensions.ValueTask;
using Soenneker.Utils.File.Abstract;
using Soenneker.Utils.Usings.Abstract;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Utils.Usings;

/// <inheritdoc cref="IUsingsUtil"/>
public sealed class UsingsUtil : IUsingsUtil
{
    private readonly IFileUtil _fileUtil;

    public UsingsUtil(IFileUtil fileUtil)
    {
        _fileUtil = fileUtil;
    }

    public async ValueTask AddMissing(string csprojPath, CancellationToken cancellationToken = default)
    {
        // TODO: Need global guaranteed singleton here
        if (!MSBuildLocator.IsRegistered)
            MSBuildLocator.RegisterDefaults();

        using var workspace = MSBuildWorkspace.Create();

        Project project = await workspace.OpenProjectAsync(csprojPath, cancellationToken: cancellationToken).NoSync();

        OptionSet options = workspace.Options.WithChangedOption(FormattingOptions.UseTabs, LanguageNames.CSharp, false)
                                     .WithChangedOption(FormattingOptions.TabSize, LanguageNames.CSharp, 4);

        foreach (Document originalDoc in project.Documents)
        {
            Document document = originalDoc;

            SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken).NoSync();
            SemanticModel? semanticModel = await document.GetSemanticModelAsync(cancellationToken).NoSync();

            List<Diagnostic> diagnostics = semanticModel.GetDiagnostics(root!.FullSpan, cancellationToken).Where(d => d.Id is "CS0246" or "CS0103").ToList();

            if (!diagnostics.Any())
                continue;

            var type = Type.GetType("Microsoft.CodeAnalysis.CSharp.AddImport.CSharpAddImportCodeFixProvider, Microsoft.CodeAnalysis.CSharp.Features");

            if (type == null)
                throw new InvalidOperationException("CSharpAddImportCodeFixProvider not found. Make sure the Roslyn features package is referenced.");

            var provider = (CodeFixProvider)Activator.CreateInstance(type)!;

            foreach (Diagnostic diagnostic in diagnostics)
            {
                var actions = new List<CodeAction>();

                var context = new CodeFixContext(document, diagnostic,
                    (action, _) => actions.Add(action), cancellationToken);

                await provider.RegisterCodeFixesAsync(context).NoSync();

                foreach (CodeAction action in actions)
                {
                    ImmutableArray<CodeActionOperation> operations = await action.GetOperationsAsync(cancellationToken);

                    foreach (ApplyChangesOperation op in operations.OfType<ApplyChangesOperation>())
                    {
                        document = op.ChangedSolution.GetDocument(document.Id)!;
                    }
                }
            }

            document = await Simplifier.ReduceAsync(document, options, cancellationToken).NoSync();
            document = await Formatter.FormatAsync(document, options, cancellationToken).NoSync();

            SourceText updatedCode = await document.GetTextAsync(cancellationToken).NoSync();
            string path = document.FilePath!;
            await _fileUtil.Write(path, updatedCode.ToString(), cancellationToken).NoSync();
        }
    }
}