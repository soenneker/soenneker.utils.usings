using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Simplification;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using Soenneker.Extensions.Task;
using Soenneker.Utils.File.Abstract;
using Soenneker.Utils.Usings.Abstract;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Utils.Usings;

/// <inheritdoc cref="IUsingsUtil"/>
public sealed class UsingsUtil : IUsingsUtil
{
    private readonly IFileUtil _fileUtil;
    private readonly ILogger<UsingsUtil> _logger;

    private static readonly HashSet<string> _missingUsingDiagnosticIds = new(StringComparer.Ordinal)
    {
        "CS0246", // type or namespace not found
        "CS0103", // name does not exist in current context
        "CS0738", // does not implement interface member
        "CS1061", // does not contain definition
    };

    private static readonly HashSet<string> _harmfulDiagnosticIds = new(StringComparer.Ordinal)
    {
        "CS0104", // ambiguous reference
        "CS0433", // type exists in both assemblies
    };

    private static readonly Lazy<CodeFixProvider> _addImportProvider = new(() =>
    {
        var type = Type.GetType(
            "Microsoft.CodeAnalysis.CSharp.AddImport.CSharpAddImportCodeFixProvider, Microsoft.CodeAnalysis.CSharp.Features",
            throwOnError: false);

        if (type is null)
            throw new InvalidOperationException(
                "CSharpAddImportCodeFixProvider not found. Ensure the Roslyn features package is referenced.");

        return (CodeFixProvider)Activator.CreateInstance(type)!;
    });

    public UsingsUtil(IFileUtil fileUtil, ILogger<UsingsUtil> logger)
    {
        _fileUtil = fileUtil;
        _logger = logger;
    }

    public async ValueTask AddMissing(
        string csprojPath,
        bool loopUntilNoChanges = false,
        int maxPasses = 5,
        CancellationToken cancellationToken = default)
    {
        EnsureMsBuildRegistered();

        var totalResolved = 0;
        var totalDetected = 0;
        var pass = 0;
        bool changesMade;

        do
        {
            pass++;
            _logger.LogInformation("Starting pass {Pass}...", pass);
            changesMade = false;

            using var workspace = MSBuildWorkspace.Create();

            _logger.LogInformation("Project loading: {ProjectPath}...", csprojPath);
            Project project = await workspace.OpenProjectAsync(csprojPath, cancellationToken: cancellationToken).NoSync();
            _logger.LogInformation("Project loaded: {ProjectName}", project.Name);

            _logger.LogInformation("Compiling project: {ProjectName}...", project.Name);
            Compilation? compilation = await project.GetCompilationAsync(cancellationToken).NoSync();

            if (compilation is null)
            {
                _logger.LogError("Failed to compile project: {ProjectName}", project.Name);
                return;
            }

            _logger.LogInformation("Compilation complete: {AssemblyName}", compilation.AssemblyName);

            Dictionary<string, List<Diagnostic>> diagByPath = BuildDiagnosticsByPath(compilation, cancellationToken);

            if (diagByPath.Count == 0)
            {
                _logger.LogInformation("No missing-using diagnostics detected.");
                break;
            }

            OptionSet options = workspace.Options
                .WithChangedOption(FormattingOptions.UseTabs, LanguageNames.CSharp, false)
                .WithChangedOption(FormattingOptions.TabSize, LanguageNames.CSharp, 4);

            CodeFixProvider provider = _addImportProvider.Value;

            // Reuse list to reduce allocations.
            var actions = new List<CodeAction>(capacity: 4);

            foreach (Document originalDoc in project.Documents)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string? docPath = originalDoc.FilePath;
                if (docPath is null)
                    continue;

                if (!diagByPath.TryGetValue(docPath, out List<Diagnostic>? filtered) || filtered.Count == 0)
                    continue;

                Document document = originalDoc;
                totalDetected += filtered.Count;

                for (int i = 0; i < filtered.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    Diagnostic diagnostic = filtered[i];

                    actions.Clear();

                    // Correct constructor: (document, diagnostic, registerCodeFix, cancellationToken)
                    var context = new CodeFixContext(
                        document,
                        diagnostic,
                        (action, _) => actions.Add(action),
                        cancellationToken);

                    await provider.RegisterCodeFixesAsync(context).NoSync();

                    // Apply all registered actions (matches your behavior).
                    // If you want a speed win, apply only actions[0] (often enough).
                    for (int a = 0; a < actions.Count; a++)
                    {
                        ImmutableArray<CodeActionOperation> operations =
                            await actions[a].GetOperationsAsync(cancellationToken).NoSync();

                        for (int opIndex = 0; opIndex < operations.Length; opIndex++)
                        {
                            if (operations[opIndex] is ApplyChangesOperation apply)
                            {
                                Document? changed = apply.ChangedSolution.GetDocument(document.Id);
                                if (changed is not null)
                                    document = changed;
                            }
                        }
                    }
                }

                SyntaxNode? originalRoot = await originalDoc.GetSyntaxRootAsync(cancellationToken).NoSync();
                SyntaxNode? updatedRoot = await document.GetSyntaxRootAsync(cancellationToken).NoSync();

                if (originalRoot is null || updatedRoot is null)
                    continue;

                if (originalRoot.IsEquivalentTo(updatedRoot, topLevel: false))
                    continue;

                document = await Simplifier.ReduceAsync(document, options, cancellationToken).NoSync();
                document = await Formatter.FormatAsync(document, options, cancellationToken).NoSync();

                SemanticModel? semanticModel = await document.GetSemanticModelAsync(cancellationToken).NoSync();
                if (semanticModel is null)
                {
                    _logger.LogWarning("Could not get updated semantic model for {DocPath}, skipping.", docPath);
                    continue;
                }

                // Only now do a full scan for harmful diagnostics (because doc changed).
                if (ContainsAnyDiagnostic(semanticModel.GetDiagnostics(cancellationToken: cancellationToken), _harmfulDiagnosticIds))
                {
                    _logger.LogWarning("Harmful diagnostics in {DocPath}, skipping write.", docPath);
                    continue;
                }

                totalResolved += CountResolved(filtered, semanticModel, cancellationToken);

                SourceText originalText = await originalDoc.GetTextAsync(cancellationToken).NoSync();
                SourceText updatedText = await document.GetTextAsync(cancellationToken).NoSync();

                if (!originalText.ContentEquals(updatedText))
                {
                    await _fileUtil.Write(docPath, updatedText.ToString(), true, cancellationToken).NoSync();
                    changesMade = true;
                    _logger.LogInformation("Applied missing usings to: {DocPath}", docPath);
                }
            }

            if (loopUntilNoChanges && changesMade)
                _logger.LogInformation("Changes detected. Preparing for next pass...");

            if (loopUntilNoChanges && pass >= maxPasses)
            {
                _logger.LogWarning("Maximum number of passes ({MaxPasses}) reached. Stopping iteration.", maxPasses);
                break;
            }
        }
        while (loopUntilNoChanges && changesMade);

        _logger.LogInformation("Completed adding missing usings.");
        _logger.LogInformation("Total missing using diagnostics found: {TotalDetected}", totalDetected);
        _logger.LogInformation("Total diagnostics resolved: {TotalResolved}", totalResolved);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureMsBuildRegistered()
    {
        if (MSBuildLocator.IsRegistered)
            return;

        MSBuildLocator.RegisterDefaults();
        _logger.LogDebug("MSBuildLocator registered.");
    }

    private static Dictionary<string, List<Diagnostic>> BuildDiagnosticsByPath(Compilation compilation, CancellationToken cancellationToken)
    {
        var map = new Dictionary<string, List<Diagnostic>>(StringComparer.Ordinal);

        ImmutableArray<Diagnostic> diags = compilation.GetDiagnostics(cancellationToken);

        for (int i = 0; i < diags.Length; i++)
        {
            Diagnostic d = diags[i];

            if (!_missingUsingDiagnosticIds.Contains(d.Id))
                continue;

            Location loc = d.Location;
            if (loc == Location.None || loc.SourceTree is null)
                continue;

            string? path = loc.SourceTree.FilePath;
            if (string.IsNullOrEmpty(path))
                continue;

            if (!map.TryGetValue(path, out List<Diagnostic>? list))
            {
                list = new List<Diagnostic>(capacity: 4);
                map.Add(path, list);
            }

            list.Add(d);
        }

        return map;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ContainsAnyDiagnostic(ImmutableArray<Diagnostic> diagnostics, HashSet<string> ids)
    {
        for (int i = 0; i < diagnostics.Length; i++)
        {
            if (ids.Contains(diagnostics[i].Id))
                return true;
        }

        return false;
    }

    private static int CountResolved(List<Diagnostic> originalDiagnostics, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        int resolved = 0;

        for (int i = 0; i < originalDiagnostics.Count; i++)
        {
            Diagnostic d = originalDiagnostics[i];
            TextSpan span = d.Location.SourceSpan;

            // Only look at diagnostics overlapping the original span.
            ImmutableArray<Diagnostic> now = semanticModel.GetDiagnostics(span, cancellationToken);

            bool stillThere = false;

            for (int j = 0; j < now.Length; j++)
            {
                if (now[j].Id == d.Id)
                {
                    stillThere = true;
                    break;
                }
            }

            if (!stillThere)
                resolved++;
        }

        return resolved;
    }
}
