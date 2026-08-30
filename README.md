[![](https://img.shields.io/nuget/v/soenneker.utils.usings.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.utils.usings/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.utils.usings/publish-package.yml?style=for-the-badge)](https://github.com/soenneker/soenneker.utils.usings/actions/workflows/publish-package.yml)
[![](https://img.shields.io/nuget/dt/soenneker.utils.usings.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.utils.usings/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.utils.usings/codeql.yml?label=CodeQL&style=for-the-badge)](https://github.com/soenneker/soenneker.utils.usings/actions/workflows/codeql.yml)

# ![](https://user-images.githubusercontent.com/4441470/224455560-91ed3ee7-f510-4041-a8d2-3fc093025112.png) Soenneker.Utils.Usings
Loads a C# project with Roslyn and writes first-ranked missing-import fixes directly to source files.

## Installation

```bash
dotnet add package Soenneker.Utils.Usings
```

## Registration

```csharp
using Soenneker.Utils.Usings.Registrars;

services.AddUsingsUtilAsSingleton();
```

Then inject `IUsingsUtil` wherever you need it.

## Apply missing imports

```csharp
await usingsUtil.AddMissing(
    csprojPath: @"C:\git\Acme\src\Acme.Api\Acme.Api.csproj",
    cancellationToken: cancellationToken);
```

The project must be loadable by `MSBuildWorkspace` in the current process. The utility registers the default MSBuild instance when none has already been registered, compiles the project, and considers source diagnostics with these IDs:

- `CS0246`: type or namespace not found
- `CS0103`: name does not exist in the current context
- `CS0738`: interface member implementation mismatch
- `CS1061`: member or extension method not found

For each diagnostic, the utility asks Roslyn's C# add-import provider for fixes and applies only its first-ranked action. A document is simplified and formatted before being written. If the changed document contains `CS0104` or `CS0433` ambiguity diagnostics, that document is skipped.

Diagnostics do not guarantee that a missing `using` is the correct fix, and only those two ambiguity diagnostics are used as a write guard. Review and build the resulting project.

## Multiple passes

```csharp
await usingsUtil.AddMissing(
    csprojPath,
    loopUntilNoChanges: true,
    maxPasses: 5,
    cancellationToken);
```

The default is one pass. With looping enabled, the project is recompiled after a pass that wrote changes and processing stops when a pass makes no changes or `maxPasses` is reached.

## File-safety expectations

`AddMissing` edits project source files in place. It has no dry-run, backup, transaction, or automatic rollback. Run it only in a clean source-controlled working tree where its exact diff can be reviewed. Successfully changed documents are written as processing proceeds, so cancellation or a later failure can leave earlier edits on disk.

Project-load failures, Roslyn failures, file-write failures, and cancellation propagate to the caller. If Roslyn cannot produce a compilation, the method logs the failure and returns without editing.

Singleton and scoped registrations are both available. The utility creates a new Roslyn workspace per call; do not run concurrent calls against the same project files.
