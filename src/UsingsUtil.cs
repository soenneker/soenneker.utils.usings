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
using Microsoft.Extensions.Logging;

namespace Soenneker.Utils.Usings;

/// <inheritdoc cref="IUsingsUtil"/>
public sealed class UsingsUtil : IUsingsUtil
{
    private readonly IFileUtil _fileUtil;
    private readonly ILogger<UsingsUtil> _logger;

    public UsingsUtil(IFileUtil fileUtil, ILogger<UsingsUtil> logger)
    {
        _fileUtil = fileUtil;
        _logger = logger;
    }

    public async ValueTask AddMissing(string csprojPath, bool loopUntilNoChanges = false, int maxPasses = 5, CancellationToken cancellationToken = default)
    {
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
            _logger.LogDebug("MSBuildLocator registered.");
        }

        int pass = 0;
        bool changesMade;

        do
        {
            pass++;
            _logger.LogInformation("Starting pass {Pass}...", pass);
            changesMade = false;

            using var workspace = MSBuildWorkspace.Create();
            Project project = await workspace.OpenProjectAsync(csprojPath, cancellationToken: cancellationToken).NoSync();
            _logger.LogInformation("Project loaded: {ProjectName}", project.Name);

            OptionSet options = workspace.Options
                .WithChangedOption(FormattingOptions.UseTabs, LanguageNames.CSharp, false)
                .WithChangedOption(FormattingOptions.TabSize, LanguageNames.CSharp, 4);

            foreach (Document originalDoc in project.Documents)
            {
                string docPath = originalDoc.FilePath ?? "unknown";
                Document document = originalDoc;

                SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken).NoSync();
                SemanticModel? semanticModel = await document.GetSemanticModelAsync(cancellationToken).NoSync();

                List<Diagnostic> diagnostics = semanticModel.GetDiagnostics(root!.FullSpan, cancellationToken).ToList();
                List<Diagnostic> filtered = diagnostics.Where(d => d.Id is "CS0246" or "CS0103" or "CS0738").ToList();

                if (filtered.Count == 0)
                    continue;

                _logger.LogInformation("Found {Count} missing using diagnostics in {DocPath}", filtered.Count, docPath);

                var type = Type.GetType("Microsoft.CodeAnalysis.CSharp.AddImport.CSharpAddImportCodeFixProvider, Microsoft.CodeAnalysis.CSharp.Features");

                if (type == null)
                {
                    _logger.LogError("CSharpAddImportCodeFixProvider not found.");
                    throw new InvalidOperationException("CSharpAddImportCodeFixProvider not found. Ensure the Roslyn features package is referenced.");
                }

                var provider = (CodeFixProvider)Activator.CreateInstance(type)!;

                foreach (Diagnostic diagnostic in filtered)
                {
                    var actions = new List<CodeAction>();

                    var context = new CodeFixContext(document, diagnostic, (action, _) =>
                    {
                        _logger.LogDebug("Registering code fix: {Title} for diagnostic {DiagnosticId}", action.Title, diagnostic.Id);
                        actions.Add(action);
                    }, cancellationToken);

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

                SemanticModel? updatedSemanticModel = await document.GetSemanticModelAsync(cancellationToken).NoSync();
                ImmutableArray<Diagnostic> newDiagnostics = updatedSemanticModel.GetDiagnostics(cancellationToken: cancellationToken);

                bool hasHarmfulDiagnostics = newDiagnostics.Any(d => d.Id is "CS0104" or "CS0433");

                if (hasHarmfulDiagnostics)
                {
                    _logger.LogWarning("Harmful diagnostics detected in {DocPath}, skipping write.", docPath);
                    continue;
                }

                SourceText originalText = await originalDoc.GetTextAsync(cancellationToken).NoSync();
                SourceText updatedText = await document.GetTextAsync(cancellationToken).NoSync();

                if (!originalText.ContentEquals(updatedText))
                {
                    await _fileUtil.Write(docPath, updatedText.ToString(), cancellationToken).NoSync();
                    changesMade = true;
                    _logger.LogInformation("Applied missing usings to: {DocPath}", docPath);
                }
                else
                {
                    _logger.LogDebug("No changes detected for {DocPath}, skipping write.");
                }
            }

            if (loopUntilNoChanges && changesMade)
                _logger.LogInformation("Changes detected. Preparing for next pass...");

            if (loopUntilNoChanges && pass >= maxPasses)
            {
                _logger.LogWarning("Maximum number of passes ({MaxPasses}) reached. Stopping iteration.", maxPasses);
                break;
            }

        } while (loopUntilNoChanges && changesMade);

        _logger.LogInformation("Completed adding missing usings.");
    }
}
