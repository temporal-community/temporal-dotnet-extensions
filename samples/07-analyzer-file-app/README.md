# Analyzer file-based app

This sample is a minimal .NET 10 file-based app that consumes the general Temporal analyzer
package without a project file. It intentionally contains workflow violations so you can inspect
diagnostics and try the available IDE code fixes.

Run it from this directory with:

```bash
dotnet run --file Program.cs
```

The build is expected to fail with diagnostics for these triggers:

```csharp
await Task.CompletedTask.ConfigureAwait(false); // TEMP001
await Task.Delay(100);                          // TEMP002
_ = DateTime.UtcNow;                             // TEMP003
await Task.Run(() => Task.CompletedTask);       // TEMP004
Thread.Sleep(100);                               // TEMP007
var id = Guid.NewGuid();                         // TEMP008
_ = Random.Shared;                               // TEMP008
```

The compiler reports diagnostics during the file-app build. The sample targets the `0.3.1`
analyzer package and reports `TEMP001`–`TEMP004`, `TEMP007`, and `TEMP008`. Opening the file in an
IDE with the package installed also exposes code fixes for
`ConfigureAwait(false)`, `Task.Delay`, system-clock reads, `Task.Run`, `Guid.NewGuid`, and
supported `Random` replacements.
